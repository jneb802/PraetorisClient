using System;
using HarmonyLib;
using Jotunn.Entities;
using Jotunn.Managers;
using UnityEngine;

namespace PraetorisClient.BodyBounties;

internal static class BountyFeature
{
    internal const string NpcAnchorPrefab = "PraetorisBountyNPC";
    private static GameObject? _npc;

    internal static void Initialize()
    {
        BountyWindow.Initialize();
        PrefabManager.OnVanillaPrefabsAvailable += RegisterPrefabs;
        PrefabManager.OnPrefabsRegistered += AttachTombstone;
    }

    private static void RegisterPrefabs()
    {
        if (ItemManager.Instance.GetItem(BountyInventory.ParcelPrefab) == null)
        {
            CustomItem parcel = new CustomItem(BountyInventory.ParcelPrefab, "BarleyFlour");
            ItemDrop.ItemData.SharedData data = parcel.ItemDrop.m_itemData.m_shared;
            data.m_name = "Tombstone parcel";
            data.m_description = "Return this sealed parcel to a body retrieval NPC at a shipping port.";
            data.m_maxStackSize = 1;
            data.m_teleportable = false;
            data.m_toolTier = 1000; // Native hard restriction, including metal portals and TeleportAll worlds.
            data.m_weight = 0;
            data.m_icons = new[] { BountyParcel.CreateIcon() };
            parcel.ItemPrefab.AddComponent<BountyParcel>();
            ItemManager.Instance.AddItem(parcel);
        }
        if (_npc == null)
        {
            GameObject original = PrefabManager.Instance.GetPrefab("Hildir");
            if (original == null) { PraetorisClientPlugin.Log.LogWarning("Body bounties: Hildir prefab is unavailable."); return; }
            _npc = PrefabManager.Instance.CreateClonedPrefab("Praetoris_BountyHildir", original);
            _npc.name = "Praetoris_BountyHildir";
            _npc.SetActive(false);
            // Local visual and interaction only. The authored marker ZDO is the server's
            // authority anchor; this clone must not register a second network NPC.
            foreach (MonoBehaviour behaviour in _npc.GetComponentsInChildren<MonoBehaviour>(true))
                UnityEngine.Object.DestroyImmediate(behaviour);
            foreach (Rigidbody body in _npc.GetComponentsInChildren<Rigidbody>(true)) body.isKinematic = true;
            _npc.AddComponent<BountyNpc>();
        }
        if (PrefabManager.Instance.GetPrefab(NpcAnchorPrefab) == null)
        {
            // Jotunn replaces authored JVLmock_PraetorisBountyNPC objects with
            // this persistent prefab. MWL does not register or discover NPCs.
            GameObject anchor = PrefabManager.Instance.CreateEmptyPrefab(NpcAnchorPrefab);
            UnityEngine.Object.DestroyImmediate(anchor.GetComponent<MeshRenderer>());
            UnityEngine.Object.DestroyImmediate(anchor.GetComponent<MeshFilter>());
            UnityEngine.Object.DestroyImmediate(anchor.GetComponent<Collider>());
            anchor.GetComponent<ZNetView>().m_persistent = true;
            anchor.AddComponent<BountyNpcAnchor>();
            PrefabManager.Instance.AddPrefab(new CustomPrefab(anchor, false));
        }
    }

    private static void AttachTombstone()
    {
        GameObject stone = PrefabManager.Instance.GetPrefab("Player_tombstone");
        if (stone != null && stone.GetComponent<TombstoneBounty>() == null) stone.AddComponent<TombstoneBounty>();
    }

    internal static void AttachNpc(ZNetView port, Transform marker)
    {
        if (_npc == null || ZNet.instance == null || ZNet.instance.IsDedicated() || port == null || !port.IsValid()) return;
        if (marker.Find("BountyHildir") != null) return;
        GameObject npc = UnityEngine.Object.Instantiate(_npc, marker, false);
        npc.name = "BountyHildir";
        npc.transform.localPosition = Vector3.zero;
        npc.transform.localRotation = Quaternion.identity;
        npc.GetComponent<BountyNpc>().Port = port;
        npc.SetActive(true);
    }
}

public sealed class BountyNpcAnchor : MonoBehaviour
{
    private void Start() => BountyFeature.AttachNpc(GetComponent<ZNetView>(), transform);
}

public sealed class BountyNpc : MonoBehaviour, Interactable, Hoverable
{
    public float GetHoverOffset() => 0f;
    internal ZNetView Port = null!;
    public string GetHoverName() => "Body retrieval";
    public string GetHoverText() => Localization.instance.Localize("Body retrieval\n[<color=yellow><b>$KEY_Use</b></color>] Open bounties");
    public bool Interact(Humanoid user, bool hold, bool alt)
    {
        if (hold || Port == null || !Port.IsValid()) return false;
        BountyWindow.Open(Port.GetZDO().m_uid.ToString());
        return true;
    }
    public bool UseItem(Humanoid user, ItemDrop.ItemData item) => false;
}

[HarmonyPatch(typeof(ItemDrop.ItemData), nameof(ItemDrop.ItemData.GetTooltip), new Type[] { typeof(ItemDrop.ItemData), typeof(int), typeof(bool), typeof(float), typeof(int), typeof(bool) })]
internal static class BountyParcelTooltip
{
    private static void Postfix(ItemDrop.ItemData item, ref string __result)
    {
        if (!BountyInventory.IsParcel(item)) return;
        item.m_customData.TryGetValue(BountyInventory.OwnerKey, out string owner);
        item.m_customData.TryGetValue(BountyInventory.RewardKey, out string reward);
        __result += "\nOwner: " + System.Security.SecurityElement.Escape(owner) + "\nReward: " + reward + " Coins";
    }
}

[HarmonyPatch(typeof(Player), nameof(Player.OnSpawned))]
internal static class BountyJoin
{
    private static void Postfix(Player __instance)
    {
        if (__instance == Player.m_localPlayer)
        {
            BountyWindow.EndSelection();
            BountyWindow.Close();
            BountyService.Request(new BountyMessage { Action = "join" });
            TombstoneBounty.OnPlayerJoined();
        }
    }
}
