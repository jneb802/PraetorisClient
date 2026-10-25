using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace PraetorisClient
{
    // LocationProxy reconstructs the non-networked gateway from today's prefab.
    // An existing dungeon still uses its saved generator and room transforms.
    internal sealed class FrostCaveInterior : MonoBehaviour
    {
        private readonly List<ZDO> _objects = new List<ZDO>();
        private Location _location = null!;
        private float _nextCheck;

        internal void Initialize(Location location)
        {
            _location = location;
            Update();
        }

        private void Update()
        {
            if (_location == null || ZDOMan.instance == null || Time.unscaledTime < _nextCheck)
            {
                return;
            }

            _nextCheck = Time.unscaledTime + 1f;
            DungeonGenerator generator = _location.m_generator;
            Transform interior = _location.m_interiorTransform;
            if (generator == null || interior == null || !generator.transform.IsChildOf(interior))
            {
                Destroy(this);
                return;
            }

            int prefabHash = Utils.GetPrefabName(generator.gameObject).GetStableHashCode();
            Vector3 expectedPosition = generator.transform.position;
            _objects.Clear();
            ZDOMan.instance.FindSectorObjects(ZoneSystem.GetZone(expectedPosition),
                new SimulationDistance(0, 0, classic: true), _objects);

            ZDO? savedGenerator = null;
            foreach (ZDO candidate in _objects)
            {
                if (candidate.GetPrefab() != prefabHash ||
                    Mathf.Abs(candidate.GetPosition().y - expectedPosition.y) > 1f)
                {
                    continue;
                }

                if (savedGenerator != null)
                {
                    PraetorisClientPlugin.Log.LogWarning("Frost cave interior has multiple saved dungeon generators; leaving its transform unchanged.");
                    Destroy(this);
                    return;
                }

                savedGenerator = candidate;
            }

            // On a remote client the generator ZDO may arrive after the proxy.
            if (savedGenerator == null)
            {
                return;
            }

            Vector3 savedPosition = savedGenerator.GetPosition();
            Quaternion savedRotation = savedGenerator.GetRotation();
            if (Vector3.Distance(expectedPosition, savedPosition) > 0.01f ||
                Quaternion.Angle(generator.transform.rotation, savedRotation) > 0.01f)
            {
                Quaternion correction = savedRotation * Quaternion.Inverse(generator.transform.rotation);
                interior.rotation = correction * interior.rotation;
                interior.position += savedPosition - generator.transform.position;
                PraetorisClientPlugin.Log.LogInfo("Aligned frost cave interior with saved dungeon: " +
                    expectedPosition + " -> " + savedPosition + ".");
            }

            Destroy(this);
        }
    }

    [HarmonyPatch(typeof(LocationProxy), "SpawnLocation")]
    internal static class FrostCaveInteriorSpawnPatch
    {
        [HarmonyPostfix]
        private static void Postfix(LocationProxy __instance, bool __result)
        {
            if (!__result || __instance.m_instance == null)
            {
                return;
            }

            Location location = __instance.m_instance.GetComponent<Location>();
            if (location == null || Utils.GetPrefabName(location.gameObject) != "MountainCave02")
            {
                return;
            }

            location.gameObject.AddComponent<FrostCaveInterior>().Initialize(location);
        }
    }
}
