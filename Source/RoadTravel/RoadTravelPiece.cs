using System;
using BepInEx.Configuration;
using HarmonyLib;
using Jotunn.Configs;
using Jotunn.Entities;
using Jotunn.Managers;
using UnityEngine;

namespace PraetorisClient.RoadTravel;

internal static class RoadTravelPiece
{
    internal const string StopPrefab = "PraetorisRoadStop";
    internal static ConfigEntry<bool> Enabled = null!;
    internal static ConfigEntry<int> Coins = null!, Wood = null!, Iron = null!, Cores = null!, Fare = null!;
    internal static ConfigEntry<float> SnapDistance = null!, Clearance = null!;
    private static Player? snapPlayer;
    private static bool snapValid;
    private static Vector3 snapPosition;
    private static Quaternion snapRotation;
    internal static void Initialize(ConfigFile config)
    {
        ConfigDescription Synced(string text) => new(text, null, new ConfigurationManagerAttributes { IsAdminOnly = true });
        Enabled = config.Bind("RoadTravel", "Enabled", true, Synced("Enable road stop placement and road travel. Existing stops remain in the world when disabled."));
        Coins = config.Bind("RoadTravel", "BuildCoins", 10000, Synced("Provisional road stop construction cost in coins. Requires a restart after changes."));
        Wood = config.Bind("RoadTravel", "BuildFineWood", 100, Synced("Provisional construction cost in fine wood. Requires a restart after changes."));
        Iron = config.Bind("RoadTravel", "BuildIron", 50, Synced("Provisional construction cost in iron. Requires a restart after changes."));
        Cores = config.Bind("RoadTravel", "BuildSurtlingCores", 20, Synced("Provisional construction cost in surtling cores. Requires a restart after changes."));
        Fare = config.Bind("RoadTravel", "FareCoins", 100, Synced("Provisional flat coin fare in each direction. FreeDeparturePortGuid also makes road departures from that port free. Road stops remain shared, without build limits or progression gates."));
        SnapDistance = config.Bind("RoadTravel", "SnapDistance", 10f, Synced("Maximum horizontal distance from the aimed point to a road centerline, in meters. Clamped to 4 through 20."));
        Clearance = config.Bind("RoadTravel", "EdgeClearance", 2f, Synced("Road stop clearance outside the road edge, in meters. Clamped to 1 through 4."));
        PrefabManager.OnVanillaPrefabsAvailable += Register;
    }
    internal static void Shutdown() => PrefabManager.OnVanillaPrefabsAvailable -= Register;
    private static void Register()
    {
        GameObject prefab = PrefabManager.Instance.CreateClonedPrefab(StopPrefab, "sign");
        if (prefab == null) { PraetorisClientPlugin.Log.LogError("Could not register the road travel stop."); return; }
        Sign sign = prefab.GetComponent<Sign>();
        if (sign != null)
        {
            if (sign.m_textWidget != null) sign.m_textWidget.text = "ROAD\nTRAVEL";
            UnityEngine.Object.DestroyImmediate(sign);
        }
        Piece piece = prefab.GetComponent<Piece>();
        piece.m_noClipping = true; piece.m_groundOnly = true; piece.m_canRotate = false;
        prefab.AddComponent<RoadStop>();
        PieceManager.Instance.AddPiece(new CustomPiece(prefab, false, new PieceConfig
        {
            Name = "Road travel stop", Description = "Shared travel to shipping ports connected by road. Build beside a generated road.",
            PieceTable = PieceTables.Hammer, Category = PieceCategories.Misc,
            Requirements = new[] { new RequirementConfig("Coins", Math.Max(0, Coins.Value), 0, true),
                new RequirementConfig("FineWood", Math.Max(0, Wood.Value), 0, true), new RequirementConfig("Iron", Math.Max(0, Iron.Value), 0, true),
                new RequirementConfig("SurtlingCore", Math.Max(0, Cores.Value), 0, true) }
        }));
        PrefabManager.OnVanillaPrefabsAvailable -= Register;
    }
    internal static bool IsStop(Piece piece) => piece != null && Utils.GetPrefabName(piece.gameObject) == StopPrefab;

    internal static void RefreshRecipe()
    {
        Piece? piece = PrefabManager.Instance.GetPrefab(StopPrefab)?.GetComponent<Piece>();
        if (piece == null) return;
        foreach (Piece.Requirement resource in piece.m_resources)
        {
            if (resource.m_resItem == null) continue;
            int? amount = Utils.GetPrefabName(resource.m_resItem.gameObject) switch
            { "Coins" => Coins.Value, "FineWood" => Wood.Value, "Iron" => Iron.Value, "SurtlingCore" => Cores.Value, _ => null };
            if (amount.HasValue) resource.m_amount = Math.Max(0, amount.Value);
        }
    }

    internal static bool Snap(Vector3 aim, out Vector3 position, out Quaternion rotation, out string route)
    {
        position = aim; rotation = Quaternion.identity; route = "";
        if (!RoadTravelService.Ready || !RoadTravelBridge.Find(aim, Mathf.Clamp(SnapDistance.Value, 4, 20), out route, out Vector3 center, out Vector3 direction, out float width)) return false;
        Vector3 side = Vector3.Cross(Vector3.up, direction);
        if (Vector3.Dot(aim - center, side) < 0) side = -side;
        position = center + side * (width * 0.5f + Mathf.Clamp(Clearance.Value, 1, 4));
        if (ZoneSystem.instance == null || !ZoneSystem.instance.FindFloor(position + Vector3.up * 2, out float height)) return false;
        position.y = height;
        if (height < ZoneSystem.instance.m_waterLevel + 0.5f || Mathf.Abs(height - center.y) > 3f) return false;
        // A close parallel road can occupy the chosen roadside position. Use the same projection as the server.
        if (!RoadTravelBridge.Find(position, 32, out string closestRoute, out _, out _, out _) || closestRoute != route) return false;
        rotation = Quaternion.LookRotation(-side);
        return true;
    }

    // Change the ray result before vanilla applies collision, ward, biome and no-build checks.
    [HarmonyPatch(typeof(Player), nameof(Player.PieceRayTest))]
    private static class RayPatch
    {
        private static void Postfix(Player __instance, ref bool __result, ref Vector3 point, ref Vector3 normal, ref Piece piece, ref Heightmap heightmap)
        {
            if (!__result || !IsStop(__instance.GetSelectedPiece())) return;
            snapValid = false;
            if (!Snap(point, out Vector3 target, out Quaternion rotation, out _)) return;
            if (Vector3.Distance(__instance.GetEyePoint(), target) > __instance.m_maxPlaceDistance) { __result = false; return; }
            snapPlayer = __instance; snapPosition = target; snapRotation = rotation; snapValid = true;
            point = target; piece = null!; heightmap = null!;
            if (Physics.Raycast(target + Vector3.up * 2, Vector3.down, out RaycastHit hit, 4, LayerMask.GetMask("terrain", "piece", "static_solid")))
            {
                normal = hit.normal;
                piece = hit.collider.GetComponentInParent<Piece>();
                heightmap = hit.collider.GetComponentInParent<Heightmap>();
            }
        }
    }
    [HarmonyPatch(typeof(Player), nameof(Player.UpdatePlacementGhost))]
    private static class PreviewPatch
    {
        private static void Prefix(Player __instance) { snapPlayer = __instance; snapValid = false; }
        private static void Postfix(Player __instance)
        {
            if (!IsStop(__instance.GetSelectedPiece()) || __instance.m_placementGhost == null || !__instance.m_placementGhost.activeSelf) return;
            Vector3 current = __instance.m_placementGhost.transform.position;
            if (snapPlayer != __instance || !snapValid || Utils.DistanceXZ(snapPosition, current) > 0.75f)
                __instance.m_placementStatus = Player.PlacementStatus.Invalid;
            else
            {
                __instance.m_placementGhost.transform.rotation = snapRotation;
                if (__instance.m_placementStatus == Player.PlacementStatus.Valid && __instance.TestGhostClipping(__instance.m_placementGhost, 0.2f))
                    __instance.m_placementStatus = Player.PlacementStatus.Invalid;
            }
            __instance.SetPlacementGhostValid(__instance.m_placementStatus == Player.PlacementStatus.Valid);
        }
    }
    [HarmonyPatch(typeof(Player), nameof(Player.TryPlacePiece))]
    private static class PlacePatch
    {
        private static bool Prefix(Player __instance, Piece piece, ref bool __result)
        {
            if (!IsStop(piece) || RoadTravelService.Ready) return true;
            __result = false; __instance.Message(MessageHud.MessageType.Center, RoadTravelService.Status); return false;
        }
    }
}

internal sealed class RoadStop : MonoBehaviour, Hoverable, Interactable
{
    internal const string IdentityKey = "Praetoris.RoadStop.Id1";
    private void Start()
    {
        ZNetView view = GetComponent<ZNetView>();
        if (view != null && view.IsValid() && view.IsOwner() && string.IsNullOrEmpty(view.GetZDO().GetString(IdentityKey)))
            view.GetZDO().Set(IdentityKey, Guid.NewGuid().ToString("N"));
    }
    public float GetHoverOffset() => 0;
    public string GetHoverName() => "Road travel stop";
    public string GetHoverText() => RoadTravelService.Ready ? "Road travel stop\n[<color=yellow><b>$KEY_Use</b></color>] Choose a port" : RoadTravelService.Status;
    public bool Interact(Humanoid user, bool hold, bool alt)
    {
        ZNetView view = GetComponent<ZNetView>();
        if (hold || user != Player.m_localPlayer || view == null || !view.IsValid()) return false;
        RoadTravelService.Open(view.GetZDO().m_uid, false); return true;
    }
    public bool UseItem(Humanoid user, ItemDrop.ItemData item) => false;
}
