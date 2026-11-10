using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace PraetorisClient.RoadTravel;

/// <summary>Local presentation of a server-validated port endpoint. No extra saved ports or shipping traders.</summary>
internal static class RoadTravelAgents
{
    private static readonly Dictionary<ZDOID, GameObject> Agents = new();
    private static float _next;
    internal static void Clear()
    {
        foreach (GameObject agent in Agents.Values) if (agent != null) Object.Destroy(agent);
        Agents.Clear(); _next = 0;
    }
    internal static void Update()
    {
        if (Time.unscaledTime < _next || Player.m_localPlayer == null || ZNetScene.instance == null) return;
        _next = Time.unscaledTime + 3;
        HashSet<ZDOID> nearby = new();
        // Ports are few and MWL already maintains their registry. Never enumerate world objects each frame.
        foreach (ZDO port in RoadTravelBridge.Ports())
        {
            if (Vector3.Distance(Player.m_localPlayer.transform.position, port.GetPosition()) > 180) continue;
            if (!RoadTravelService.TryResolve(port.m_uid, true, out RoadTravelService.Destination destination) ||
                Vector3.Distance(Player.m_localPlayer.transform.position, destination.Position) > 120) continue;
            if (Agents.TryGetValue(port.m_uid, out GameObject existing) && existing != null &&
                Utils.DistanceXZ(existing.transform.position, destination.Position) < 0.05f)
            { nearby.Add(port.m_uid); continue; }
            // Remove the old collider before finding the floor at a changed endpoint.
            if (existing != null) { Object.Destroy(existing); Agents.Remove(port.m_uid); continue; }
            if (!ZoneSystem.instance.FindFloor(destination.Position + Vector3.up * 3, out float floor) || floor <= ZoneSystem.instance.m_waterLevel + 0.5f) continue;
            nearby.Add(port.m_uid);
            Vector3 position = destination.Position; position.y = floor;
            GameObject? actor = CreateGuide(position);
            if (actor == null) continue;
            actor.AddComponent<RoadTravelGuide>().PortId = port.m_uid;
            Agents[port.m_uid] = actor;
        }
        foreach (ZDOID id in Agents.Keys.ToList())
            if (!nearby.Contains(id)) { Object.Destroy(Agents[id]); Agents.Remove(id); }
    }
    private static GameObject? CreateGuide(Vector3 position)
    {
        GameObject source = ZNetScene.instance.GetPrefab("Haldor");
        if (source == null) return null;
        // Copy only the character model under an inactive local root. This cannot register a trader or a ZDO.
        GameObject root = new("Road travel guide"); root.SetActive(false); root.transform.position = position;
        Transform? visual = source.transform.Find("Visual");
        if (visual == null) visual = source.transform.Cast<Transform>().FirstOrDefault(t => t.GetComponentInChildren<Animator>() != null);
        if (visual == null) { Object.Destroy(root); return null; }
        GameObject model = Object.Instantiate(visual.gameObject, root.transform); model.name = "GuideModel";
        foreach (MonoBehaviour behavior in model.GetComponentsInChildren<MonoBehaviour>(true)) Object.DestroyImmediate(behavior);
        foreach (ZNetView view in model.GetComponentsInChildren<ZNetView>(true)) Object.DestroyImmediate(view);
        CapsuleCollider collider = root.AddComponent<CapsuleCollider>(); collider.height = 2; collider.radius = 0.5f; collider.center = Vector3.up;
        root.layer = LayerMask.NameToLayer("piece"); root.SetActive(true); return root;
    }
}

internal sealed class RoadTravelGuide : MonoBehaviour, Hoverable, Interactable
{
    public float GetHoverOffset() => 0;
    internal ZDOID PortId;
    public string GetHoverName() => "Road travel guide";
    public string GetHoverText() => "Road travel guide\n[<color=yellow><b>$KEY_Use</b></color>] Choose an inland stop";
    public bool Interact(Humanoid user, bool hold, bool alt)
    {
        if (hold || user != Player.m_localPlayer) return false;
        RoadTravelService.Open(PortId, true); return true;
    }
    public bool UseItem(Humanoid user, ItemDrop.ItemData item) => false;
}
