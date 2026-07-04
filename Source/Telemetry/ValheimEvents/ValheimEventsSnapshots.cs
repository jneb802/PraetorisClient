using System;
using System.Collections.Generic;
using UnityEngine;

namespace PraetorisClient
{
    internal readonly struct ClientZdoSnapshot
    {
        internal static readonly ClientZdoSnapshot Empty = new(false, ZDOID.None, 0, "", Vector3.zero, Quaternion.identity, 0, 0, false, false, false, false, false, false, false, PlayerFields.Empty, ClientEquipmentSnapshot.Empty, ClientFoodSlotsSnapshot.Empty, ClientResistanceSnapshot.Empty);

        internal readonly bool Exists;
        internal readonly ZDOID Id;
        internal readonly long PlayerId;
        internal readonly string PlayerName;

        private readonly int _prefabHash;
        private readonly string _prefabName;
        private readonly Vector3 _position;
        private readonly Quaternion _rotation;
        private readonly long _owner;
        private readonly uint _dataRevision;
        private readonly bool _isPlayer;
        private readonly bool _isCharacter;
        private readonly bool _isMonster;
        private readonly bool _isPiece;
        private readonly bool _isWearNTear;
        private readonly bool _isContainer;
        private readonly bool _isPickable;
        private readonly PlayerFields _fields;
        private readonly ClientEquipmentSnapshot _equipment;
        private readonly ClientFoodSlotsSnapshot _foodSlots;
        private readonly ClientResistanceSnapshot _resistances;

        private ClientZdoSnapshot(
            bool exists,
            ZDOID id,
            int prefabHash,
            string prefabName,
            Vector3 position,
            Quaternion rotation,
            long owner,
            uint dataRevision,
            bool isPlayer,
            bool isCharacter,
            bool isMonster,
            bool isPiece,
            bool isWearNTear,
            bool isContainer,
            bool isPickable,
            PlayerFields fields,
            ClientEquipmentSnapshot equipment,
            ClientFoodSlotsSnapshot foodSlots,
            ClientResistanceSnapshot resistances)
        {
            Exists = exists;
            Id = id;
            PlayerId = fields.PlayerId;
            PlayerName = fields.PlayerName;
            _prefabHash = prefabHash;
            _prefabName = prefabName;
            _position = position;
            _rotation = rotation;
            _owner = owner;
            _dataRevision = dataRevision;
            _isPlayer = isPlayer;
            _isCharacter = isCharacter;
            _isMonster = isMonster;
            _isPiece = isPiece;
            _isWearNTear = isWearNTear;
            _isContainer = isContainer;
            _isPickable = isPickable;
            _fields = fields;
            _equipment = equipment;
            _foodSlots = foodSlots;
            _resistances = resistances;
        }

        internal static ClientZdoSnapshot Capture(Character? character)
        {
            if (character == null || character.m_nview == null)
                return Empty;

            ZDO zdo = character.m_nview.GetZDO();
            return zdo == null || zdo.m_uid.IsNone() ? Empty : Capture(zdo, character.gameObject);
        }

        internal static ClientZdoSnapshot Capture(ZDOID id)
        {
            if (id.IsNone() || ZDOMan.instance == null)
                return Empty;

            ZDO zdo = ZDOMan.instance.GetZDO(id);
            return Capture(zdo);
        }

        internal static ClientZdoSnapshot Capture(ZDO zdo)
        {
            if (zdo == null || zdo.m_uid.IsNone())
                return Empty;

            GameObject? go = null;
            if (ZNetScene.instance != null)
            {
                ZNetView instance = ZNetScene.instance.FindInstance(zdo);
                if (instance != null)
                    go = instance.gameObject;
            }

            return Capture(zdo, go);
        }

        private static ClientZdoSnapshot Capture(ZDO zdo, GameObject? go)
        {
            int prefabHash = zdo.GetPrefab();
            string prefabName = "";
            if (ZNetScene.instance != null)
            {
                GameObject prefab = ZNetScene.instance.GetPrefab(prefabHash);
                if (prefab != null)
                    prefabName = prefab.name;
            }

            Player? player = go != null ? go.GetComponent<Player>() : null;
            Character? character = go != null ? go.GetComponent<Character>() : null;
            PlayerFields fields = PlayerFields.Capture(zdo);

            return new ClientZdoSnapshot(
                true,
                zdo.m_uid,
                prefabHash,
                prefabName,
                zdo.GetPosition(),
                zdo.GetRotation(),
                zdo.GetOwner(),
                zdo.DataRevision,
                player != null,
                character != null,
                go != null && go.GetComponent<MonsterAI>() != null,
                go != null && go.GetComponent<Piece>() != null,
                go != null && go.GetComponent<WearNTear>() != null,
                go != null && go.GetComponent<Container>() != null,
                go != null && go.GetComponent<Pickable>() != null,
                fields,
                ClientEquipmentSnapshot.Capture(player, zdo),
                ClientFoodSlotsSnapshot.Capture(player),
                ClientResistanceSnapshot.Capture(character));
        }

        internal void Write(TelemetryJson json, string propertyName)
        {
            json.BeginObject(propertyName);
            json.Prop("exists", Exists);
            json.Prop("id", FormatId(Id));
            json.Prop("prefabHash", _prefabHash);
            json.Prop("prefabName", _prefabName);
            json.Prop("position", _position);
            json.Prop("rotation", _rotation);
            WriteWorldContext(json, _position);
            json.Prop("ownerPeerId", _owner);
            json.Prop("dataRevision", _dataRevision);
            json.BeginArray("components");
            if (_isPlayer) json.ArrayString("Player");
            if (_isCharacter) json.ArrayString("Character");
            if (_isMonster) json.ArrayString("MonsterAI");
            if (_isPiece) json.ArrayString("Piece");
            if (_isWearNTear) json.ArrayString("WearNTear");
            if (_isContainer) json.ArrayString("Container");
            if (_isPickable) json.ArrayString("Pickable");
            json.EndArray();
            _fields.Write(json, "fields");
            _equipment.Write(json, "equipment");
            _foodSlots.Write(json, "foodSlots");
            _resistances.Write(json, "resistances");
            json.EndObject();
        }

        internal static string FormatId(ZDOID id)
        {
            return id.IsNone() ? "" : id.ToString();
        }

        internal static void WriteWorldContext(TelemetryJson json, Vector3 position)
        {
            Vector2i zone = ZoneSystem.GetZone(position);
            string biome = WorldGenerator.instance != null ? WorldGenerator.instance.GetBiome(position).ToString() : "";
            json.BeginObject("worldContext");
            json.Prop("exists", true);
            json.Prop("biome", biome);
            json.Prop("zoneX", zone.x);
            json.Prop("zoneY", zone.y);
            json.Prop("distanceFromCenter", Utils.DistanceXZ(Vector3.zero, position));
            json.EndObject();
        }

        internal bool IsPlayerSnapshot()
        {
            return Exists && (_isPlayer || PlayerId != 0L || !string.IsNullOrEmpty(PlayerName) || _prefabName == "Player");
        }

        private readonly struct PlayerFields
        {
            internal static readonly PlayerFields Empty = new(0, "", 0f, 0f, 0f, 0f, 0f, false, false, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, "", "", "", "", "", "", "", "", "", "", "", "");

            internal readonly long PlayerId;
            internal readonly string PlayerName;
            private readonly float _health;
            private readonly float _maxHealth;
            private readonly float _stamina;
            private readonly float _eitr;
            private readonly float _adrenaline;
            private readonly bool _dead;
            private readonly bool _pvp;
            private readonly int _rightItem;
            private readonly int _leftItem;
            private readonly int _chestItem;
            private readonly int _legItem;
            private readonly int _helmetItem;
            private readonly int _shoulderItem;
            private readonly int _utilityItem;
            private readonly int _trinketItem;
            private readonly int _leftBackItem;
            private readonly int _rightBackItem;
            private readonly int _hairItem;
            private readonly int _beardItem;
            private readonly string _rightItemName;
            private readonly string _leftItemName;
            private readonly string _chestItemName;
            private readonly string _legItemName;
            private readonly string _helmetItemName;
            private readonly string _shoulderItemName;
            private readonly string _utilityItemName;
            private readonly string _trinketItemName;
            private readonly string _leftBackItemName;
            private readonly string _rightBackItemName;
            private readonly string _hairItemName;
            private readonly string _beardItemName;

            private PlayerFields(long playerId, string playerName, float health, float maxHealth, float stamina, float eitr, float adrenaline, bool dead, bool pvp, int rightItem, int leftItem, int chestItem, int legItem, int helmetItem, int shoulderItem, int utilityItem, int trinketItem, int leftBackItem, int rightBackItem, int hairItem, int beardItem, string rightItemName, string leftItemName, string chestItemName, string legItemName, string helmetItemName, string shoulderItemName, string utilityItemName, string trinketItemName, string leftBackItemName, string rightBackItemName, string hairItemName, string beardItemName)
            {
                PlayerId = playerId;
                PlayerName = playerName;
                _health = health;
                _maxHealth = maxHealth;
                _stamina = stamina;
                _eitr = eitr;
                _adrenaline = adrenaline;
                _dead = dead;
                _pvp = pvp;
                _rightItem = rightItem;
                _leftItem = leftItem;
                _chestItem = chestItem;
                _legItem = legItem;
                _helmetItem = helmetItem;
                _shoulderItem = shoulderItem;
                _utilityItem = utilityItem;
                _trinketItem = trinketItem;
                _leftBackItem = leftBackItem;
                _rightBackItem = rightBackItem;
                _hairItem = hairItem;
                _beardItem = beardItem;
                _rightItemName = rightItemName;
                _leftItemName = leftItemName;
                _chestItemName = chestItemName;
                _legItemName = legItemName;
                _helmetItemName = helmetItemName;
                _shoulderItemName = shoulderItemName;
                _utilityItemName = utilityItemName;
                _trinketItemName = trinketItemName;
                _leftBackItemName = leftBackItemName;
                _rightBackItemName = rightBackItemName;
                _hairItemName = hairItemName;
                _beardItemName = beardItemName;
            }

            internal static PlayerFields Capture(ZDO zdo)
            {
                int rightItem = zdo.GetInt(ZDOVars.s_rightItem);
                int leftItem = zdo.GetInt(ZDOVars.s_leftItem);
                int chestItem = zdo.GetInt(ZDOVars.s_chestItem);
                int legItem = zdo.GetInt(ZDOVars.s_legItem);
                int helmetItem = zdo.GetInt(ZDOVars.s_helmetItem);
                int shoulderItem = zdo.GetInt(ZDOVars.s_shoulderItem);
                int utilityItem = zdo.GetInt(ZDOVars.s_utilityItem);
                int trinketItem = zdo.GetInt(ZDOVars.s_trinketItem);
                int leftBackItem = zdo.GetInt(ZDOVars.s_leftBackItem);
                int rightBackItem = zdo.GetInt(ZDOVars.s_rightBackItem);
                int hairItem = zdo.GetInt(ZDOVars.s_hairItem);
                int beardItem = zdo.GetInt(ZDOVars.s_beardItem);

                return new PlayerFields(
                    zdo.GetLong(ZDOVars.s_playerID),
                    zdo.GetString(ZDOVars.s_playerName),
                    zdo.GetFloat(ZDOVars.s_health),
                    zdo.GetFloat(ZDOVars.s_maxHealth),
                    zdo.GetFloat(ZDOVars.s_stamina),
                    zdo.GetFloat(ZDOVars.s_eitr),
                    zdo.GetFloat(ZDOVars.s_adrenaline),
                    zdo.GetBool(ZDOVars.s_dead),
                    zdo.GetBool(ZDOVars.s_pvp),
                    rightItem,
                    leftItem,
                    chestItem,
                    legItem,
                    helmetItem,
                    shoulderItem,
                    utilityItem,
                    trinketItem,
                    leftBackItem,
                    rightBackItem,
                    hairItem,
                    beardItem,
                    ResolveItemName(rightItem),
                    ResolveItemName(leftItem),
                    ResolveItemName(chestItem),
                    ResolveItemName(legItem),
                    ResolveItemName(helmetItem),
                    ResolveItemName(shoulderItem),
                    ResolveItemName(utilityItem),
                    ResolveItemName(trinketItem),
                    ResolveItemName(leftBackItem),
                    ResolveItemName(rightBackItem),
                    ResolveItemName(hairItem),
                    ResolveItemName(beardItem));
            }

            internal void Write(TelemetryJson json, string propertyName)
            {
                json.BeginObject(propertyName);
                json.Prop("exists", true);
                json.Prop("playerId", PlayerId);
                json.Prop("playerName", PlayerName);
                json.Prop("health", _health);
                json.Prop("maxHealth", _maxHealth);
                json.Prop("stamina", _stamina);
                json.Prop("eitr", _eitr);
                json.Prop("adrenaline", _adrenaline);
                json.Prop("dead", _dead);
                json.Prop("pvp", _pvp);
                WriteItem(json, "rightItem", _rightItem, _rightItemName);
                WriteItem(json, "leftItem", _leftItem, _leftItemName);
                WriteItem(json, "chestItem", _chestItem, _chestItemName);
                WriteItem(json, "legItem", _legItem, _legItemName);
                WriteItem(json, "helmetItem", _helmetItem, _helmetItemName);
                WriteItem(json, "shoulderItem", _shoulderItem, _shoulderItemName);
                WriteItem(json, "utilityItem", _utilityItem, _utilityItemName);
                WriteItem(json, "trinketItem", _trinketItem, _trinketItemName);
                WriteItem(json, "leftBackItem", _leftBackItem, _leftBackItemName);
                WriteItem(json, "rightBackItem", _rightBackItem, _rightBackItemName);
                WriteItem(json, "hairItem", _hairItem, _hairItemName);
                WriteItem(json, "beardItem", _beardItem, _beardItemName);
                json.EndObject();
            }

            private static void WriteItem(TelemetryJson json, string prefix, int hash, string name)
            {
                json.Prop(prefix + "Hash", hash);
                json.Prop(prefix + "Name", name);
            }
        }

        private static string ResolveItemName(int itemHash)
        {
            if (itemHash == 0 || ObjectDB.instance == null)
                return "";

            GameObject prefab = ObjectDB.instance.GetItemPrefab(itemHash);
            return prefab != null ? prefab.name : "";
        }
    }

    internal readonly struct ClientEquipmentSnapshot
    {
        internal static readonly ClientEquipmentSnapshot Empty = new(false, ClientEquipmentItemSnapshot.Empty, ClientEquipmentItemSnapshot.Empty, ClientEquipmentItemSnapshot.Empty, ClientEquipmentItemSnapshot.Empty, ClientEquipmentItemSnapshot.Empty, ClientEquipmentItemSnapshot.Empty, ClientEquipmentItemSnapshot.Empty, ClientEquipmentItemSnapshot.Empty);

        private readonly bool _exists;
        private readonly ClientEquipmentItemSnapshot _rightHand;
        private readonly ClientEquipmentItemSnapshot _leftHand;
        private readonly ClientEquipmentItemSnapshot _head;
        private readonly ClientEquipmentItemSnapshot _chest;
        private readonly ClientEquipmentItemSnapshot _legs;
        private readonly ClientEquipmentItemSnapshot _shoulder;
        private readonly ClientEquipmentItemSnapshot _utility;
        private readonly ClientEquipmentItemSnapshot _trinket;

        private ClientEquipmentSnapshot(bool exists, ClientEquipmentItemSnapshot rightHand, ClientEquipmentItemSnapshot leftHand, ClientEquipmentItemSnapshot head, ClientEquipmentItemSnapshot chest, ClientEquipmentItemSnapshot legs, ClientEquipmentItemSnapshot shoulder, ClientEquipmentItemSnapshot utility, ClientEquipmentItemSnapshot trinket)
        {
            _exists = exists;
            _rightHand = rightHand;
            _leftHand = leftHand;
            _head = head;
            _chest = chest;
            _legs = legs;
            _shoulder = shoulder;
            _utility = utility;
            _trinket = trinket;
        }

        internal static ClientEquipmentSnapshot Capture(Player? player, ZDO zdo)
        {
            if (player != null)
            {
                return new ClientEquipmentSnapshot(
                    true,
                    ClientEquipmentItemSnapshot.Capture(player.m_rightItem),
                    ClientEquipmentItemSnapshot.Capture(player.m_leftItem),
                    ClientEquipmentItemSnapshot.Capture(player.m_helmetItem),
                    ClientEquipmentItemSnapshot.Capture(player.m_chestItem),
                    ClientEquipmentItemSnapshot.Capture(player.m_legItem),
                    ClientEquipmentItemSnapshot.Capture(player.m_shoulderItem),
                    ClientEquipmentItemSnapshot.Capture(player.m_utilityItem),
                    ClientEquipmentItemSnapshot.Capture(player.m_trinketItem));
            }

            return new ClientEquipmentSnapshot(
                true,
                ClientEquipmentItemSnapshot.Capture(zdo.GetInt(ZDOVars.s_rightItem)),
                ClientEquipmentItemSnapshot.Capture(zdo.GetInt(ZDOVars.s_leftItem)),
                ClientEquipmentItemSnapshot.Capture(zdo.GetInt(ZDOVars.s_helmetItem)),
                ClientEquipmentItemSnapshot.Capture(zdo.GetInt(ZDOVars.s_chestItem)),
                ClientEquipmentItemSnapshot.Capture(zdo.GetInt(ZDOVars.s_legItem)),
                ClientEquipmentItemSnapshot.Capture(zdo.GetInt(ZDOVars.s_shoulderItem)),
                ClientEquipmentItemSnapshot.Capture(zdo.GetInt(ZDOVars.s_utilityItem)),
                ClientEquipmentItemSnapshot.Capture(zdo.GetInt(ZDOVars.s_trinketItem)));
        }

        internal void Write(TelemetryJson json, string propertyName)
        {
            json.BeginObject(propertyName);
            json.Prop("exists", _exists);
            _rightHand.Write(json, "rightHand");
            _leftHand.Write(json, "leftHand");
            _head.Write(json, "head");
            _chest.Write(json, "chest");
            _legs.Write(json, "legs");
            _shoulder.Write(json, "shoulder");
            _utility.Write(json, "utility");
            _trinket.Write(json, "trinket");
            json.EndObject();
        }
    }

    internal readonly struct ClientEquipmentItemSnapshot
    {
        internal static readonly ClientEquipmentItemSnapshot Empty = new(false, 0, "", "", 0f, 0f, 0f);

        private readonly bool _exists;
        private readonly int _itemHash;
        private readonly string _prefabName;
        private readonly string _itemName;
        private readonly float _armor;
        private readonly float _durability;
        private readonly float _maxDurability;

        private ClientEquipmentItemSnapshot(bool exists, int itemHash, string prefabName, string itemName, float armor, float durability, float maxDurability)
        {
            _exists = exists;
            _itemHash = itemHash;
            _prefabName = prefabName;
            _itemName = itemName;
            _armor = armor;
            _durability = durability;
            _maxDurability = maxDurability;
        }

        internal static ClientEquipmentItemSnapshot Capture(ItemDrop.ItemData? item)
        {
            if (item == null)
                return Empty;

            string prefabName = item.m_dropPrefab != null ? item.m_dropPrefab.name : item.m_shared.m_name;
            return new ClientEquipmentItemSnapshot(true, prefabName.GetStableHashCode(), prefabName, item.m_shared.m_name, item.GetArmor(), item.m_durability, item.GetMaxDurability());
        }

        internal static ClientEquipmentItemSnapshot Capture(int itemHash)
        {
            if (itemHash == 0)
                return Empty;

            string itemName = "";
            if (ObjectDB.instance != null)
            {
                GameObject prefab = ObjectDB.instance.GetItemPrefab(itemHash);
                itemName = prefab != null ? prefab.name : "";
            }

            return new ClientEquipmentItemSnapshot(true, itemHash, itemName, itemName, 0f, 0f, 0f);
        }

        internal void Write(TelemetryJson json, string propertyName)
        {
            json.BeginObject(propertyName);
            json.Prop("exists", _exists);
            json.Prop("itemHash", _itemHash);
            json.Prop("prefabName", _prefabName);
            json.Prop("itemName", _itemName);
            json.Prop("armor", _armor);
            json.Prop("durability", _durability);
            json.Prop("maxDurability", _maxDurability);
            json.EndObject();
        }
    }

    internal readonly struct ClientFoodSlotsSnapshot
    {
        internal static readonly ClientFoodSlotsSnapshot Empty = new(false, new List<ClientFoodSlotSnapshot>());

        private readonly bool _exists;
        private readonly List<ClientFoodSlotSnapshot> _slots;

        private ClientFoodSlotsSnapshot(bool exists, List<ClientFoodSlotSnapshot> slots)
        {
            _exists = exists;
            _slots = slots;
        }

        internal static ClientFoodSlotsSnapshot Capture(Player? player)
        {
            if (player == null)
                return Empty;

            List<ClientFoodSlotSnapshot> slots = new();
            IReadOnlyList<Player.Food> foods = player.GetFoods();
            for (int i = 0; i < foods.Count; i++)
                slots.Add(ClientFoodSlotSnapshot.Capture(i, foods[i]));

            return new ClientFoodSlotsSnapshot(true, slots);
        }

        internal void Write(TelemetryJson json, string propertyName)
        {
            json.BeginObject(propertyName);
            json.Prop("exists", _exists);
            json.BeginArray("slots");
            foreach (ClientFoodSlotSnapshot slot in _slots)
                slot.Write(json);
            json.EndArray();
            json.EndObject();
        }
    }

    internal readonly struct ClientFoodSlotSnapshot
    {
        private readonly int _slot;
        private readonly bool _exists;
        private readonly string _prefabName;
        private readonly string _itemName;
        private readonly float _remainingTime;
        private readonly float _duration;
        private readonly float _health;
        private readonly float _stamina;
        private readonly float _eitr;
        private readonly float _baseHealth;
        private readonly float _baseStamina;
        private readonly float _baseEitr;
        private readonly float _regen;
        private readonly bool _canEatAgain;

        private ClientFoodSlotSnapshot(int slot, bool exists, string prefabName, string itemName, float remainingTime, float duration, float health, float stamina, float eitr, float baseHealth, float baseStamina, float baseEitr, float regen, bool canEatAgain)
        {
            _slot = slot;
            _exists = exists;
            _prefabName = prefabName;
            _itemName = itemName;
            _remainingTime = remainingTime;
            _duration = duration;
            _health = health;
            _stamina = stamina;
            _eitr = eitr;
            _baseHealth = baseHealth;
            _baseStamina = baseStamina;
            _baseEitr = baseEitr;
            _regen = regen;
            _canEatAgain = canEatAgain;
        }

        internal static ClientFoodSlotSnapshot Capture(int slot, Player.Food food)
        {
            ItemDrop.ItemData? item = food.m_item;
            ItemDrop.ItemData.SharedData? shared = item != null ? item.m_shared : null;
            string prefabName = item?.m_dropPrefab != null ? item.m_dropPrefab.name : food.m_name;

            return new ClientFoodSlotSnapshot(slot, true, prefabName, shared?.m_name ?? food.m_name, food.m_time, shared?.m_foodBurnTime ?? 0f, food.m_health, food.m_stamina, food.m_eitr, shared?.m_food ?? 0f, shared?.m_foodStamina ?? 0f, shared?.m_foodEitr ?? 0f, shared?.m_foodRegen ?? 0f, item != null && food.CanEatAgain());
        }

        internal void Write(TelemetryJson json)
        {
            json.BeginArrayObject();
            json.Prop("slot", _slot);
            json.Prop("exists", _exists);
            json.Prop("prefabName", _prefabName);
            json.Prop("itemName", _itemName);
            json.Prop("remainingTime", _remainingTime);
            json.Prop("duration", _duration);
            json.Prop("health", _health);
            json.Prop("stamina", _stamina);
            json.Prop("eitr", _eitr);
            json.Prop("baseHealth", _baseHealth);
            json.Prop("baseStamina", _baseStamina);
            json.Prop("baseEitr", _baseEitr);
            json.Prop("regen", _regen);
            json.Prop("canEatAgain", _canEatAgain);
            json.EndArrayObject();
        }
    }

    internal readonly struct ClientResistanceSnapshot
    {
        internal static readonly ClientResistanceSnapshot Empty = new(false, default);

        private readonly bool _exists;
        private readonly HitData.DamageModifiers _modifiers;

        private ClientResistanceSnapshot(bool exists, HitData.DamageModifiers modifiers)
        {
            _exists = exists;
            _modifiers = modifiers;
        }

        internal static ClientResistanceSnapshot Capture(Character? character)
        {
            return character == null ? Empty : new ClientResistanceSnapshot(true, character.GetDamageModifiers());
        }

        internal void Write(TelemetryJson json, string propertyName)
        {
            json.BeginObject(propertyName);
            json.Prop("exists", _exists);
            json.BeginObject("modifiers");
            json.Prop("blunt", Format(_modifiers.m_blunt));
            json.Prop("slash", Format(_modifiers.m_slash));
            json.Prop("pierce", Format(_modifiers.m_pierce));
            json.Prop("chop", Format(_modifiers.m_chop));
            json.Prop("pickaxe", Format(_modifiers.m_pickaxe));
            json.Prop("fire", Format(_modifiers.m_fire));
            json.Prop("frost", Format(_modifiers.m_frost));
            json.Prop("lightning", Format(_modifiers.m_lightning));
            json.Prop("poison", Format(_modifiers.m_poison));
            json.Prop("spirit", Format(_modifiers.m_spirit));
            json.EndObject();
            json.EndObject();
        }

        private static string Format(HitData.DamageModifier modifier)
        {
            return modifier switch
            {
                HitData.DamageModifier.Resistant => "resistant",
                HitData.DamageModifier.Weak => "weak",
                HitData.DamageModifier.Immune => "immune",
                HitData.DamageModifier.Ignore => "ignore",
                HitData.DamageModifier.VeryResistant => "very_resistant",
                HitData.DamageModifier.VeryWeak => "very_weak",
                HitData.DamageModifier.SlightlyResistant => "slightly_resistant",
                HitData.DamageModifier.SlightlyWeak => "slightly_weak",
                _ => "normal",
            };
        }
    }
}
