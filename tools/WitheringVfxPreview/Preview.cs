using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx;
using UnityEngine;

namespace WitheringVfxPreview
{
    // Separate capture-only plugin. This is not included in PraetorisClient releases.
    [BepInPlugin("praetoris.WitheringVfxPreview", "Withering VFX Preview", "0.1.0")]
    public sealed class PreviewPlugin : BaseUnityPlugin
    {
        private Character subject;

        private void Awake()
        {
            new Terminal.ConsoleCommand("wither_preview", "Local comparison: wither_preview <0-4> [duration]", args =>
            {
                if (!LocalWorld() || args.Length < 2 || !int.TryParse(args[1], out int style) || style < 0 || style > 4)
                {
                    args.Context.AddString("ERROR: Requires a local world and style 0-4.");
                    return;
                }
                subject = Character.GetAllCharacters().Where(c => !c.IsPlayer())
                    .OrderBy(c => Vector3.Distance(c.transform.position, Player.m_localPlayer.transform.position)).FirstOrDefault();
                if (!subject) { args.Context.AddString("ERROR: No subject."); return; }
                const string effect = "SE_PraetorisWithered";
                subject.GetSEMan().RemoveStatusEffect(effect.GetStableHashCode());
                foreach (Visual visual in subject.GetComponents<Visual>()) DestroyImmediate(visual);
                if (style > 0)
                {
                    StatusEffect status = subject.GetSEMan().AddStatusEffect(effect.GetStableHashCode(), true);
                    if (!status) { args.Context.AddString("ERROR: Withered was not applied."); return; }
                    status.m_ttl = args.Length > 2 ? float.Parse(args[2], System.Globalization.CultureInfo.InvariantCulture) : 300f;
                    subject.gameObject.AddComponent<Visual>().Build(subject, style);
                }
                args.Context.AddString($"OK: subject={subject.name} style={style} withered={subject.GetSEMan().HaveStatusEffect(effect.GetStableHashCode())} pos={subject.transform.position}");
            });
            new Terminal.ConsoleCommand("wither_check", "Report preview lifecycle", args =>
            {
                if (subject) args.Context.AddString($"OK: subject={subject.name} withered={subject.GetSEMan().HaveStatusEffect("SE_PraetorisWithered".GetStableHashCode())} visuals={subject.GetComponents<Visual>().Length}");
            });
        }

        private static bool LocalWorld() => Player.m_localPlayer && ZNet.instance && ZNet.instance.IsServer() && ZNet.instance.GetPeerConnections() == 0;
    }

    public sealed class Visual : MonoBehaviour
    {
        private Character character;
        private readonly List<UnityEngine.Object> owned = new();
        private readonly List<(Renderer renderer, Material[] originals)> originals = new();
        private GameObject root;
        private Transform marker;
        private Bounds bounds;

        public static Bounds BodyBounds(Character target)
        {
            SkinnedMeshRenderer[] bodies = target.GetComponentsInChildren<SkinnedMeshRenderer>();
            Bounds result = new(target.GetCenterPoint(), Vector3.one * target.GetRadius() * 2f);
            bool first = true;
            foreach (SkinnedMeshRenderer body in bodies.Where(r => r.enabled && r.sharedMesh))
            {
                if (first) { result = body.bounds; first = false; }
                else result.Encapsulate(body.bounds);
            }
            return result;
        }

        public void Build(Character target, int choice)
        {
            character = target;
            bounds = BodyBounds(target);
            root = new GameObject("WitheringPreview_" + choice);
            root.transform.SetParent(target.transform, false);
            owned.Add(root);
            if (choice == 1) MakeParticles("vfx_Poison", new Color(0.6f, 0.10f, 0.9f, 0.95f), false);
            if (choice == 2)
            {
                MakeParticles("vfx_Smoked", new Color(0.38f, 0.30f, 0.23f, 0.7f), false);
                MakeParticles("vfx_Tared", new Color(0.76f, 0.65f, 0.48f, 1f), true);
            }
            if (choice == 3)
            {
                foreach (SkinnedMeshRenderer body in target.GetComponentsInChildren<SkinnedMeshRenderer>())
                {
                    originals.Add((body, body.sharedMaterials));
                    Material[] copies = body.sharedMaterials.Select(m => m ? new Material(m) : null).ToArray();
                    foreach (Material material in copies.Where(m => m))
                    {
                        owned.Add(material);
                        if (material.HasProperty("_Color")) material.SetColor("_Color", new Color(0.65f, 0.4f, 0.8f));
                        if (material.HasProperty("_EmissionColor")) material.SetColor("_EmissionColor", new Color(0.25f, 0.035f, 0.4f));
                    }
                    body.sharedMaterials = copies;
                }
                MakeParticles("vfx_Tared", new Color(0.65f, 0.3f, 0.8f, 1f), true);
            }
            if (choice == 4) MakeMarker();
        }

        private void MakeParticles(string prefabName, Color color, bool flecks)
        {
            GameObject prefab = ZNetScene.instance.GetPrefab(prefabName);
            ParticleSystemRenderer template = prefab.GetComponentsInChildren<ParticleSystemRenderer>(true)
                .First(r => r.sharedMaterial && r.sharedMaterial.mainTexture);
            Material material = flecks ? new Material(Shader.Find("Sprites/Default")) : new Material(template.sharedMaterial);
            owned.Add(material);
            if (material.HasProperty("_Color")) material.SetColor("_Color", Color.white);
            if (material.HasProperty("_TintColor")) material.SetColor("_TintColor", Color.white);
            float size = Mathf.Clamp(bounds.size.magnitude / 12f, 0.65f, 2.4f);
            SkinnedMeshRenderer body = character.GetComponentsInChildren<SkinnedMeshRenderer>()
                .Where(r => r.enabled && r.sharedMesh).OrderByDescending(r => r.bounds.size.sqrMagnitude).First();
            GameObject emitter = new(flecks ? "Decay flakes" : "Withering wisps");
            emitter.transform.SetParent(root.transform, false);
            ParticleSystem ps = emitter.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ParticleSystem.MainModule main = ps.main;
            main.loop = true;
            main.maxParticles = flecks ? 300 : 350;
            main.startLifetime = flecks ? 2.2f : 3f;
            main.startSpeed = flecks ? 0.2f : 0.12f;
            main.startSize = new ParticleSystem.MinMaxCurve(size * (flecks ? 0.08f : 0.7f), size * (flecks ? 0.16f : 1.5f));
            main.startColor = color;
            main.gravityModifier = flecks ? 0.13f : -0.012f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            ParticleSystem.ShapeModule shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.SkinnedMeshRenderer;
            shape.skinnedMeshRenderer = body;
            shape.meshShapeType = ParticleSystemMeshShapeType.Triangle;
            ParticleSystem.EmissionModule emission = ps.emission;
            emission.rateOverTime = flecks ? 110f : 90f;
            ParticleSystem.ColorOverLifetimeModule fade = ps.colorOverLifetime;
            fade.enabled = true;
            Gradient gradient = new();
            gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(0f, 1f) });
            fade.color = gradient;
            ParticleSystemRenderer renderer = ps.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = material;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.sortingFudge = 1f;
            ps.Play();
        }

        private void MakeMarker()
        {
            GameObject glyph = new("Withering curse marker");
            glyph.transform.SetParent(root.transform, false);
            marker = glyph.transform;
            Material material = new(Shader.Find("Sprites/Default"));
            owned.Add(material);
            float radius = Mathf.Clamp(bounds.size.y * 0.12f, 0.55f, 1.35f);
            Vector3[] diamond = { new(0, radius, 0), new(radius * 0.65f, 0, 0), new(0, -radius, 0), new(-radius * 0.65f, 0, 0), new(0, radius, 0) };
            AddLine("Diamond", diamond, material, radius * 0.085f);
            AddLine("Stem", new[] { new Vector3(0, radius * 0.5f, 0), new Vector3(0, -radius * 0.5f, 0) }, material, radius * 0.1f);
            AddLine("Broken branch", new[] { new Vector3(-radius * 0.32f, radius * 0.25f, 0), Vector3.zero, new Vector3(radius * 0.32f, radius * 0.25f, 0) }, material, radius * 0.085f);
        }

        private void AddLine(string name, Vector3[] points, Material material, float width)
        {
            GameObject child = new(name);
            child.transform.SetParent(marker, false);
            LineRenderer line = child.AddComponent<LineRenderer>();
            line.sharedMaterial = material;
            line.useWorldSpace = false;
            line.positionCount = points.Length;
            line.SetPositions(points);
            line.startWidth = line.endWidth = width;
            line.startColor = line.endColor = new Color(0.78f, 0.34f, 1f, 0.95f);
        }

        private void LateUpdate()
        {
            if (!character || !character.GetSEMan().HaveStatusEffect("SE_PraetorisWithered".GetStableHashCode()))
            {
                Destroy(this);
                return;
            }
            if (marker && GameCamera.instance)
            {
                Bounds current = BodyBounds(character);
                marker.position = new Vector3(current.center.x, current.max.y + Mathf.Clamp(current.size.y * 0.22f, 1.3f, 3f), current.center.z);
                marker.rotation = GameCamera.instance.transform.rotation;
                marker.localScale = Vector3.one * (1f + 0.07f * Mathf.Sin(Time.time * 2f));
            }
        }

        private void OnDestroy()
        {
            foreach ((Renderer renderer, Material[] materials) in originals)
                if (renderer) renderer.sharedMaterials = materials;
            foreach (UnityEngine.Object item in owned) if (item) Destroy(item);
        }
    }
}
