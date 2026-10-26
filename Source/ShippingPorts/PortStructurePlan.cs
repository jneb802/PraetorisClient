using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace PraetorisClient
{
    // Build the entire plan before writing. Never remove an existing object.
    internal sealed class PortStructurePlan
    {
        internal sealed class Entry
        {
            internal ZNetView Template = null!;
            internal Vector3 Position;
            internal Quaternion Rotation;
            internal int Prefab;
        }

        internal readonly List<ZDO> Existing = new();
        internal readonly List<WearNTear> Damaged = new();
        internal readonly List<Entry> Missing = new();

        internal static PortStructurePlan Create(GameObject asset, LocationProxy proxy, ZDO trader)
        {
            List<Entry> entries = ReadLayout(asset, proxy, trader);
            if (entries.Count == 0 || entries.Count > 2000)
                throw new InvalidOperationException("Unsupported structural piece count: " + entries.Count);
            for (int i = 0; i < entries.Count; i++)
                for (int j = i + 1; j < entries.Count; j++)
                    if (entries[i].Prefab == entries[j].Prefab && Vector3.Distance(entries[i].Position, entries[j].Position) < 0.1f &&
                        Quaternion.Angle(entries[i].Rotation, entries[j].Rotation) < 1f)
                        throw new InvalidOperationException("The template contains duplicate structural pieces.");
            List<ZDO> nearby = new();
            ZDOMan.instance.FindSectorObjects(ZoneSystem.GetZone(proxy.transform.position), new SimulationDistance(3, 0), nearby);
            PortStructurePlan plan = new();
            HashSet<ZDOID> matched = new();
            foreach (Entry entry in entries)
            {
                List<ZDO> occupants = nearby.Where(zdo => Vector3.Distance(zdo.GetPosition(), entry.Position) < 0.1f).ToList();
                List<ZDO> matches = occupants.Where(zdo => zdo.GetPrefab() == entry.Prefab &&
                    Quaternion.Angle(zdo.GetRotation(), entry.Rotation) < 1f).ToList();
                if (matches.Count > 1) throw new InvalidOperationException("Duplicate pieces at " + entry.Position);
                if (matches.Count == 1)
                {
                    ZDO zdo = matches[0];
                    if (zdo.GetLong(ZDOVars.s_creator) != 0)
                        throw new InvalidOperationException("Player construction occupies an original piece position at " + entry.Position);
                    if (!matched.Add(zdo.m_uid)) throw new InvalidOperationException("Multiple template pieces match one saved object.");
                    ZNetView view = ZNetScene.instance.FindInstance(zdo);
                    WearNTear? wear = view == null ? null : view.GetComponent<WearNTear>();
                    if (wear == null) throw new InvalidOperationException("A structural piece is not loaded. Remain beside the port and retry.");
                    if (Vector3.Distance(wear.transform.localScale, entry.Template.transform.localScale) > 0.01f)
                        throw new InvalidOperationException("An existing piece has a different scale at " + entry.Position);
                    plan.Existing.Add(zdo);
                    if (zdo.GetFloat(ZDOVars.s_health, wear.m_health) < wear.m_health) plan.Damaged.Add(wear);
                }
                else
                {
                    if (occupants.Count != 0) throw new InvalidOperationException("Another saved object occupies a missing piece position at " + entry.Position);
                    // Conservative check: do not rebuild through a player-built object's bounds.
                    foreach (ZDO zdo in nearby.Where(zdo => zdo.GetLong(ZDOVars.s_creator) != 0))
                    {
                        ZNetView view = ZNetScene.instance.FindInstance(zdo);
                        if (view == null) throw new InvalidOperationException("Nearby player construction is not loaded.");
                        Bounds target = WorldBounds(entry);
                        if (view.GetComponentsInChildren<Collider>().Any(collider => !collider.isTrigger && collider.bounds.Intersects(target)))
                            throw new InvalidOperationException("Player construction overlaps a missing piece at " + entry.Position);
                    }
                    plan.Missing.Add(entry);
                }
            }
            return plan;
        }

        private static Bounds WorldBounds(Entry entry)
        {
            // Renderer bounds provide a conservative oriented-to-world bounding box without spawning anything.
            Bounds result = new(entry.Position, Vector3.one * 0.05f);
            Transform root = entry.Template.transform;
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                Bounds bounds = renderer.bounds;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 corner = bounds.center + Vector3.Scale(bounds.extents,
                        new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    Vector3 offset = Quaternion.Inverse(root.rotation) * (corner - root.position);
                    result.Encapsulate(entry.Position + entry.Rotation * offset);
                }
            }
            return result;
        }

        private static List<Entry> ReadLayout(GameObject asset, LocationProxy proxy, ZDO trader)
        {
            Location location = asset.GetComponent<Location>();
            if (location == null || location.m_generator != null || location.m_interiorTransform != null ||
                Vector3.Distance(asset.transform.localScale, Vector3.one) > 0.001f)
                throw new InvalidOperationException("This location layout is unsupported.");
            Transform[] transforms = asset.GetComponentsInChildren<Transform>(true);
            bool[] active = transforms.Select(transform => transform.gameObject.activeSelf).ToArray();
            Vector3 position = asset.transform.position;
            Quaternion rotation = asset.transform.rotation;
            UnityEngine.Random.State random = UnityEngine.Random.state;
            Heightmap.Biome biome = location.m_biome;
            List<Entry> entries = new();
            try
            {
                ZNetView[] views = Utils.GetEnabledComponentsInChildren<ZNetView>(asset);
                RandomSpawn[] spawns = Utils.GetEnabledComponentsInChildren<RandomSpawn>(asset);
                RandomObject[] objects = Utils.GetEnabledComponentsInChildren<RandomObject>(asset);
                foreach (RandomSpawn spawn in spawns) spawn.Prepare();
                asset.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                UnityEngine.Random.InitState(proxy.GetComponent<ZNetView>().GetZDO().GetInt(ZDOVars.s_seed));
                foreach (RandomSpawn spawn in spawns) spawn.Randomize(proxy.transform.position + proxy.transform.rotation * spawn.transform.position, location);
                foreach (RandomObject item in objects) item.Randomize(proxy.transform.position + proxy.transform.rotation * item.transform.position, location);
                int traderMatches = 0;
                foreach (ZNetView view in views)
                {
                    if (!view.gameObject.activeSelf) continue;
                    Vector3 target = proxy.transform.position + proxy.transform.rotation * view.transform.position;
                    Quaternion targetRotation = proxy.transform.rotation * view.transform.rotation;
                    int hash = Utils.GetPrefabName(view.gameObject).GetStableHashCode();
                    if (hash == trader.GetPrefab() && Vector3.Distance(target, trader.GetPosition()) < 0.1f &&
                        Quaternion.Angle(targetRotation, trader.GetRotation()) < 1f) traderMatches++;
                    Piece piece = view.GetComponent<Piece>();
                    if (piece == null || view.GetComponent<WearNTear>() == null ||
                        (piece.m_category != Piece.PieceCategory.BuildingWorkbench && piece.m_category != Piece.PieceCategory.BuildingStonecutter)) continue;
                    if (view.GetComponentsInChildren<ZNetView>(true).Length != 1 ||
                        view.GetComponentInChildren<Container>(true) != null || view.GetComponent<ItemStand>() != null ||
                        view.GetComponent<Smelter>() != null || view.GetComponent<Fermenter>() != null ||
                        view.GetComponent<Fireplace>() != null || view.GetComponent<Plant>() != null) continue;
                    if (Vector3.Distance(target, proxy.transform.position) > 100f)
                        throw new InvalidOperationException("A template piece is outside the supported port radius.");
                    if (ZNetScene.instance.GetPrefab(hash) == null)
                        throw new InvalidOperationException("A structural prefab is not registered: " + view.name);
                    entries.Add(new Entry { Template = view, Position = target, Rotation = targetRotation, Prefab = hash });
                }
                if (traderMatches != 1) throw new InvalidOperationException("The saved trader does not match this location's original layout.");
            }
            finally
            {
                asset.transform.SetPositionAndRotation(position, rotation);
                for (int i = 0; i < transforms.Length; i++) transforms[i].gameObject.SetActive(active[i]);
                location.m_biome = biome;
                UnityEngine.Random.state = random;
            }
            return entries;
        }
    }
}
