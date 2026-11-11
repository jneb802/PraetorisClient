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
    private const string BundleName = "praetorisclient";
    private const string GuideName = "Guide";
    internal static ConfigEntry<bool> Enabled = null!;
    internal static ConfigEntry<int> Stone = null!, Wood = null!, Corewood = null!, Fare = null!;
    internal static ConfigEntry<float> SnapDistance = null!, Clearance = null!, MaxGroundDifference = null!;
    private static bool slopeRefused;
    private static Player? snapPlayer;
    private static bool snapValid;
    private static Vector3 snapPosition;
    private static Quaternion snapRotation;
    private static readonly int GroundMask = LayerMask.GetMask("terrain", "piece", "static_solid");
    internal static void Initialize(ConfigFile config)
    {
        ConfigDescription Synced(string text) => new(text, null, new ConfigurationManagerAttributes { IsAdminOnly = true });
        Enabled = config.Bind("RoadTravel", "Enabled", true, Synced("Enable road stop placement and road travel. Existing stops remain in the world when disabled."));
        Stone = config.Bind("RoadTravel", "BuildStone", 1000, Synced("Road travel wagon construction cost in stone."));
        Wood = config.Bind("RoadTravel", "BuildWood", 2000, Synced("Road travel wagon construction cost in wood."));
        Corewood = config.Bind("RoadTravel", "BuildCorewood", 1000, Synced("Road travel wagon construction cost in corewood, which places the wagon in the Black Forest tier."));
        Fare = config.Bind("RoadTravel", "FareCoins", 100, Synced("Provisional flat coin fare in each direction. FreeDeparturePortGuid also makes road departures from that port free. Road stops remain shared, without build limits or progression gates."));
        SnapDistance = config.Bind("RoadTravel", "SnapDistance", 10f, Synced("Maximum horizontal distance from the aimed point to a road centerline, in meters. Clamped to 4 through 20."));
        Clearance = config.Bind("RoadTravel", "EdgeClearance", 2f, Synced("Road stop clearance outside the road edge, in meters. Clamped to 1 through 4."));
        MaxGroundDifference = config.Bind("RoadTravel", "MaxGroundDifference", 1f, Synced("Largest ground height difference under the wagon's corners, compared with the driver's ground, in meters. Higher values allow sloped spots where the wagon floats or sinks. Clamped to 0.5 through 3."));
        PrefabManager.OnVanillaPrefabsAvailable += Register;
    }
    internal static void Shutdown() => PrefabManager.OnVanillaPrefabsAvailable -= Register;
    private static void Register()
    {
        PrefabManager.OnVanillaPrefabsAvailable -= Register;
        // The wagon and the Haldor guide are Jotunn mocks in the bundle, resolved to the vanilla models.
        AssetBundle bundle = Jotunn.Utils.AssetUtils.LoadAssetBundleFromResources(BundleName, typeof(RoadTravelPiece).Assembly);
        GameObject? source = bundle != null ? bundle.LoadAsset<GameObject>(StopPrefab) : null;
        // Jotunn replaces mock children only in a non-persistent copy, not in the bundle asset itself.
        GameObject? prefab = source != null ? PrefabManager.Instance.CreateClonedPrefab(StopPrefab, source) : null;
        Transform? guide = prefab != null ? prefab.transform.Find(GuideName) : null;
        if (guide == null) { PraetorisClientPlugin.Log.LogError("Could not load the road travel wagon from the PraetorisClient bundle."); return; }
        guide.gameObject.AddComponent<RoadStop>();
        prefab!.GetComponent<Piece>().m_canRotate = false; // The road snap sets the rotation.
        PieceManager.Instance.AddPiece(new CustomPiece(prefab, true, new PieceConfig
        {
            Name = "Road travel wagon", Description = "Shared travel to shipping ports connected by road. Build on level ground beside a generated road. Monsters attack it; protect it with a ward.",
            PieceTable = PieceTables.Hammer, Category = PieceCategories.Misc,
            Requirements = new[] { new RequirementConfig("Stone", Math.Max(0, Stone.Value), 0, true),
                new RequirementConfig("Wood", Math.Max(0, Wood.Value), 0, true), new RequirementConfig("RoundLog", Math.Max(0, Corewood.Value), 0, true) }
        }));
        PieceManager.OnPiecesRegistered += FinishPrefab;
    }

    // Runs after Jotunn replaced the mock children with the vanilla wagon and Haldor models.
    private static void FinishPrefab()
    {
        PieceManager.OnPiecesRegistered -= FinishPrefab;
        GameObject prefab = PrefabManager.Instance.GetPrefab(StopPrefab);
        if (prefab == null) return;
        int pieceLayer = LayerMask.NameToLayer("piece");
        // The vanilla wagon collider is on the Default layer; pieces must use the piece layer for building and damage.
        foreach (Collider collider in prefab.GetComponentsInChildren<Collider>(true)) collider.gameObject.layer = pieceLayer;
        // Monsters target the wagon through StaticTarget.FindClosestPoint, which needs convex colliders. With the vanilla
        // concave mesh they pick the wagon as a target but never find a point to attack.
        foreach (MeshCollider mesh in prefab.GetComponentsInChildren<MeshCollider>(true)) mesh.convex = true;
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
            { "Stone" => Stone.Value, "Wood" => Wood.Value, "RoundLog" => Corewood.Value, _ => null };
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
        if (!LevelUnderWagon(position, side, direction)) { slopeRefused = true; return false; }
        // A close parallel road can occupy the chosen roadside position. Use the same projection as the server.
        if (!RoadTravelBridge.Find(position, 32, out string closestRoute, out _, out _, out _) || closestRoute != route) return false;
        rotation = Quaternion.LookRotation(-side);
        return true;
    }

    // The wagon is a rigid 10 m model behind the driver, parallel to the road. On a slope one end floats and the other
    // sinks into the ground, so require the ground at its corners to be within MaxGroundDifference of the driver's ground.
    private const float WagonNear = 1.1f, WagonFar = 4.9f, WagonHalfLength = 4.9f;
    private static bool LevelUnderWagon(Vector3 position, Vector3 away, Vector3 along)
    {
        along.y = 0; along.Normalize();
        foreach (float depth in new[] { WagonNear, WagonFar })
            foreach (float offset in new[] { -WagonHalfLength, WagonHalfLength })
            {
                Vector3 corner = position + away * depth + along * offset;
                if (!ZoneSystem.instance.FindFloor(corner + Vector3.up * 5, out float ground) ||
                    Mathf.Abs(ground - position.y) > Mathf.Clamp(MaxGroundDifference.Value, 0.5f, 3f)) return false;
            }
        return true;
    }

    // Change the ray result before vanilla applies collision, ward, biome and no-build checks.
    [HarmonyPatch(typeof(Player), nameof(Player.PieceRayTest))]
    private static class RayPatch
    {
        private static void Postfix(Player __instance, ref bool __result, ref Vector3 point, ref Vector3 normal, ref Piece piece, ref Heightmap heightmap)
        {
            if (!IsStop(__instance.GetSelectedPiece())) return;
            snapValid = false; slopeRefused = false;
            if (!__result) return;
            if (!Snap(point, out Vector3 target, out Quaternion rotation, out _)) return;
            if (Vector3.Distance(__instance.GetEyePoint(), target) > __instance.m_maxPlaceDistance) { __result = false; return; }
            snapPlayer = __instance; snapPosition = target; snapRotation = rotation; snapValid = true;
            point = target; piece = null!; heightmap = null!;
            if (Physics.Raycast(target + Vector3.up * 2, Vector3.down, out RaycastHit hit, 4, GroundMask))
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
            if (!IsStop(piece)) return true;
            string? refusal = !RoadTravelService.Ready ? RoadTravelService.Status
                : slopeRefused ? "The wagon needs level ground beside the road. Level the ground and try again." : null;
            if (refusal == null) return true;
            __result = false; __instance.Message(MessageHud.MessageType.Center, refusal); return false;
        }
    }
}

internal sealed class RoadStop : MonoBehaviour, Hoverable, Interactable
{
    internal const string IdentityKey = "Praetoris.RoadStop.Id1";
    private void Start()
    {
        ZNetView view = GetComponentInParent<ZNetView>();
        if (view != null && view.IsValid() && view.IsOwner() && string.IsNullOrEmpty(view.GetZDO().GetString(IdentityKey)))
            view.GetZDO().Set(IdentityKey, Guid.NewGuid().ToString("N"));
    }
    public float GetHoverOffset() => 0;
    public string GetHoverName() => "Road travel driver";
    public string GetHoverText() => RoadTravelService.Ready ? Localization.instance.Localize("Road travel driver\n[<color=yellow><b>$KEY_Use</b></color>] Choose a port") : RoadTravelService.Status;
    public bool Interact(Humanoid user, bool hold, bool alt)
    {
        ZNetView view = GetComponentInParent<ZNetView>();
        if (hold || user != Player.m_localPlayer || view == null || !view.IsValid()) return false;
        RoadTravelService.Open(view.GetZDO().m_uid, false); return true;
    }
    public bool UseItem(Humanoid user, ItemDrop.ItemData item) => false;
}
