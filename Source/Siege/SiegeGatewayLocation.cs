using Jotunn.Entities;
using Jotunn.Managers;
using UnityEngine;

namespace PraetorisClient
{
    internal static class SiegeGatewayLocation
    {
        internal const string LocationName = "valheim_creative_siege_gateway_troll_gate_test";
        private const string SourceLocationName = "TrollCave02";
        private const string GatewayPath = "Gateway";
        private const string SiegeId = "troll_gate_test";
        private static bool _subscribed;

        internal static void Register()
        {
            if (_subscribed)
            {
                return;
            }

            _subscribed = true;
            ZoneManager.OnVanillaLocationsAvailable += AddLocationClone;
        }

        internal static void Unregister()
        {
            if (!_subscribed)
            {
                return;
            }

            _subscribed = false;
            ZoneManager.OnVanillaLocationsAvailable -= AddLocationClone;
        }

        private static void AddLocationClone()
        {
            if (CustomLocation.IsCustomLocation(LocationName))
            {
                return;
            }

            ZoneSystem.ZoneLocation sourceLocation = ZoneManager.Instance.GetZoneLocation(SourceLocationName);
            if (sourceLocation == null)
            {
                PraetorisClientPlugin.Log.LogWarning($"Could not find source location {SourceLocationName} for siege gateway clone.");
                return;
            }

            CustomLocation location = ZoneManager.Instance.CreateClonedLocation(LocationName, SourceLocationName);
            if (location == null || location.Prefab == null)
            {
                PraetorisClientPlugin.Log.LogWarning($"Could not clone siege gateway location from {SourceLocationName}.");
                return;
            }

            ConfigureLocation(location);
            ConfigureGateway(location.Prefab);
            PraetorisClientPlugin.Log.LogInfo($"Registered siege gateway location clone {LocationName} from {SourceLocationName}.");
        }

        private static void ConfigureLocation(CustomLocation location)
        {
            location.ZoneLocation.m_quantity = 0;
            location.ZoneLocation.m_biome = Heightmap.Biome.BlackForest;
            location.ZoneLocation.m_biomeArea = Heightmap.BiomeArea.Median;
            location.ZoneLocation.m_group = LocationName;
            location.ZoneLocation.m_minAltitude = 3f;
            location.ZoneLocation.m_minTerrainDelta = 5f;
            location.ZoneLocation.m_exteriorRadius = 12f;
            location.ZoneLocation.m_clearArea = true;
        }

        private static void ConfigureGateway(GameObject prefab)
        {
            Transform gatewayTransform = prefab.transform.Find(GatewayPath);
            if (gatewayTransform == null)
            {
                PraetorisClientPlugin.Log.LogWarning($"Siege gateway location {LocationName} did not contain {GatewayPath}.");
                return;
            }

            SiegeGateway gateway = gatewayTransform.GetComponent<SiegeGateway>();
            if (gateway == null)
            {
                gateway = gatewayTransform.gameObject.AddComponent<SiegeGateway>();
            }

            gateway.m_siegeId = SiegeId;
            gateway.m_entryPosition = Vector3.zero;
        }
    }
}
