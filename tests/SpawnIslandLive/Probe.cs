using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.IO;
using System.IO.Compression;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

[BepInPlugin("praetoris.validation.spawnisland", "Spawn Island Probe", "1.0.0")]
public sealed class SpawnIslandProbe : BaseUnityPlugin
{
    private readonly List<ZDOID> _created = new List<ZDOID>();
    private MethodInfo _rule;

    private void Awake()
    {
        new Terminal.ConsoleCommand("island_probe", "Validation only: status, point x z, sites, goto x z, prepare, place prefab x z, bypass prefab x z, count x z, cleanup", args =>
        {
            Action<string> output = message => { Logger.LogInfo(message); args.Context.AddString(message); };
            try { Run(args, output); }
            catch (Exception exception) { output("FAIL " + exception); }
        });
    }

    private string Rule(Vector3 position)
    {
        if (_rule == null)
        {
            Type type = AccessTools.TypeByName("PraetorisClient.GuardStoneFeature.SpawnIslandWards");
            _rule = AccessTools.Method(type, "GetBlockReason");
        }
        return (string)_rule.Invoke(null, new object[] { position });
    }

    private static Vector3 Point(float x, float z)
    {
        Vector3 point = new Vector3(x, 0, z);
        point.y = WorldGenerator.instance.GetHeight(point);
        Heightmap map = Heightmap.FindHeightmap(point);
        if (map != null) point.y = ZoneSystem.instance.GetGroundHeight(point);
        return point;
    }

    private void Run(Terminal.ConsoleEventArgs args, Action<string> output)
    {
        string action = args[1];
        if (action == "config")
        {
            if (!ZNet.instance.IsServer()) throw new InvalidOperationException("Test server only");
            string value = File.ReadAllText(Path.Combine(Paths.ConfigPath, "PraetorisClient", "spawn-island", ZNet.instance.GetWorldUID() + ".boundary")).Trim();
            if (args[2] == "invalid") value = "invalid-test-boundary";
            else if (args[2] == "wrongworld")
            {
                using (MemoryStream input = new MemoryStream(Convert.FromBase64String(value)))
                using (GZipStream unzip = new GZipStream(input, CompressionMode.Decompress))
                using (MemoryStream raw = new MemoryStream())
                {
                    unzip.CopyTo(raw);
                    byte[] bytes = raw.ToArray();
                    Array.Copy(BitConverter.GetBytes(ZNet.instance.GetWorldUID() + 1), 0, bytes, 4, 8);
                    using (MemoryStream outputStream = new MemoryStream())
                    {
                        using (GZipStream zip = new GZipStream(outputStream, CompressionMode.Compress, true)) zip.Write(bytes, 0, bytes.Length);
                        value = Convert.ToBase64String(outputStream.ToArray());
                    }
                }
            }
            else if (args[2] != "saved") throw new ArgumentException("Use invalid, wrongworld, or saved");
            ConfigFile config = Chainloader.PluginInfos["warpalicious.PraetorisClient"].Instance.Config;
            config[new ConfigDefinition("SpawnIslandWards", "Boundary")].BoxedValue = value;
            config.Save();
            config.Reload();
            output("TEST CONFIG " + args[2]);
            return;
        }
        if (action == "status")
        {
            ZoneSystem.instance.GetLocationIcon("StartTemple", out Vector3 spawn);
            output($"WORLD id={ZNet.instance.GetWorldUID()} name={ZNet.instance.GetWorld().m_name} spawn={spawn} water={ZoneSystem.instance.m_waterLevel} ruleAtSpawn={Rule(spawn) ?? "allow"}");
            return;
        }
        if (action == "sites")
        {
            ZoneSystem.instance.GetLocationIcon("StartTemple", out Vector3 spawn);
            int inside = 0, outside = 0;
            for (int radius = 128; radius <= 3000 && (inside < 4 || outside < 4); radius += 128)
            {
                for (int a = 0; a < 24 && (inside < 4 || outside < 4); a++)
                {
                    float angle = a * Mathf.PI / 12;
                    Vector3 p = Point(Mathf.Round(spawn.x + Mathf.Cos(angle) * radius), Mathf.Round(spawn.z + Mathf.Sin(angle) * radius));
                    if (p.y < ZoneSystem.instance.m_waterLevel + 3) continue;
                    bool blocked = Rule(p) != null;
                    if (blocked ? inside >= 4 : outside >= 4) continue;
                    if (blocked) inside++; else outside++;
                    output($"SITE {(blocked ? "inside" : "outside")} x={p.x} y={p.y} z={p.z}");
                }
            }
            return;
        }
        if (action == "cleanup")
        {
            foreach (ZDOID id in _created)
            {
                ZDO zdo = ZDOMan.instance.GetZDO(id);
                if (zdo == null) continue;
                zdo.SetOwner(ZDOMan.GetSessionID());
                ZDOMan.instance.DestroyZDO(zdo);
            }
            output("CLEANUP tracked objects=" + _created.Count);
            _created.Clear();
            return;
        }

        Player player = Player.m_localPlayer;
        if (action == "prepare")
        {
            Inventory inventory = player.GetInventory();
            foreach (string item in new[] { "Hammer", "FineWood", "GreydwarfEye", "SurtlingCore", "Wood", "Stone", "RoundLog" })
            {
                ItemDrop.ItemData added = inventory.AddItem(item, item == "Hammer" ? 1 : 50, 1, 0, 0L, "", true);
                player.AddKnownItem(added);
                if (item == "Hammer") player.EquipItem(added, false);
            }
            player.SetGodMode(true);
            player.SetGhostMode(true);
            output("PREPARED development player with hammer and materials");
            return;
        }
        int offset = action == "place" || action == "bypass" ? 3 : 2;
        Vector3 point = Point(float.Parse(args[offset], System.Globalization.CultureInfo.InvariantCulture), float.Parse(args[offset + 1], System.Globalization.CultureInfo.InvariantCulture));
        if (action == "point")
        {
            output($"POINT {point} seedHeight={WorldGenerator.instance.GetHeight(point)} rule={Rule(point) ?? "allow"}");
            return;
        }
        if (action == "goto")
        {
            Vector3 landing = Point(point.x, point.z - 4) + Vector3.up;
            player.TeleportTo(landing, Quaternion.identity, true);
            output("TELEPORT " + landing);
            return;
        }
        if (action == "count")
        {
            Dictionary<ZDOID, ZDO> objects = AccessTools.FieldRefAccess<ZDOMan, Dictionary<ZDOID, ZDO>>("m_objectsByID")(ZDOMan.instance);
            foreach (ZDO zdo in objects.Values.Where(z => Vector3.Distance(z.GetPosition(), point) < 8 && z.GetPrefab() == "guard_stone".GetStableHashCode()))
                output($"WARD id={zdo.m_uid} pos={zdo.GetPosition()}");
            output("COUNT query complete");
            return;
        }
        GameObject prefab = ZNetScene.instance.GetPrefab(args[2]);
        if (prefab == null) throw new ArgumentException("Unknown prefab " + args[2]);
        if (action == "bypass")
        {
            GameObject obj = Instantiate(prefab, point, Quaternion.identity);
            obj.GetComponent<Piece>().SetCreator(player.GetPlayerID(), Splatform.PlatformManager.DistributionPlatform.LocalUser.PlatformUserID);
            ZDOID id = obj.GetComponent<ZNetView>().GetZDO().m_uid;
            _created.Add(id);
            output($"BYPASS created={id} at={point}");
            return;
        }
        if (action != "place") throw new ArgumentException("Unknown action " + action);

        Piece piece = prefab.GetComponent<Piece>();
        if (piece.m_craftingStation != null)
        {
            Vector3 stationPoint = Point(player.transform.position.x - 5, player.transform.position.z);
            GameObject station = Instantiate(piece.m_craftingStation.gameObject, stationPoint, Quaternion.identity);
            _created.Add(station.GetComponent<ZNetView>().GetZDO().m_uid);
            player.AddKnownStation(station.GetComponent<CraftingStation>());
        }
        player.SetNoPlacementCost(false);
        AccessTools.Method(typeof(Player), "UpdateAvailablePiecesList").Invoke(player, null);
        if (!player.SetSelectedPiece(piece)) throw new InvalidOperationException("Piece cannot be selected");
        player.AttackTowardsPlayerLookDir = true;
        player.SetLookDir((point - player.GetEyePoint()).normalized);
        player.FaceLookDirection();
        GameCamera.instance.transform.LookAt(point);
        Physics.SyncTransforms();
        AccessTools.Method(typeof(Player), "UpdatePlacementGhost").Invoke(player, new object[] { false });
        Func<string> resources = () => string.Join(",", piece.m_resources.Where(r => r.m_resItem != null).Select(r => r.m_resItem.name + "=" + player.GetInventory().CountItems(r.m_resItem.m_itemData.m_shared.m_name)));
        string before = resources();
        Dictionary<ZDO, ZNetView> instances = AccessTools.FieldRefAccess<ZNetScene, Dictionary<ZDO, ZNetView>>("m_instances")(ZNetScene.instance);
        HashSet<ZDOID> beforeIds = new HashSet<ZDOID>(instances.Keys.Select(z => z.m_uid));
        player.AddStamina(100);
        AccessTools.Field(typeof(Player), "m_lastToolUseTime").SetValue(player, -9999f);
        AccessTools.Field(typeof(Player), "m_placePressedTime").SetValue(player, Time.time);
        AccessTools.Method(typeof(Player), "UpdatePlacement").Invoke(player, new object[] { true, 0.02f });
        int placed = 0;
        foreach (ZDO zdo in instances.Keys)
        {
            if (beforeIds.Contains(zdo.m_uid) || zdo.GetPrefab() != args[2].GetStableHashCode()) continue;
            _created.Add(zdo.m_uid);
            placed++;
        }
        GameObject ghost = (GameObject)AccessTools.Field(typeof(Player), "m_placementGhost").GetValue(player);
        output($"PLACE prefab={args[2]} point={point} ghost={ghost?.transform.position} status={player.GetPlacementStatus()} requirements={player.HaveRequirements(piece, Player.RequirementMode.CanBuild)} placed={placed} before=[{before}] after=[{resources()}] rule={Rule(point) ?? "allow"}");
    }
}
