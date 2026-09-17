using UnityEngine;
using HarmonyLib;

namespace EpicLootLeslieAlphaTest.src.StatusEffects.VFX
{
    public static class InfusionVFX
    {
        public static GameObject FireInfusionVFX;
        public static GameObject FrostInfusionVFX;
        public static GameObject LightningInfusionVFX;
        public static GameObject PoisonInfusionVFX;
        public static GameObject WindInfusionVFX;

        public static void Init()
        {
            FireInfusionVFX = BuildVFX(ZNetScene.instance.GetPrefab("Torch"), .1f);
            FrostInfusionVFX = BuildVFX(ZNetScene.instance.GetPrefab("vfx_Freezing"), .3f);
            LightningInfusionVFX = BuildVFX(ZNetScene.instance.GetPrefab("fx_Lightning"), .2f);
            PoisonInfusionVFX = BuildVFX(ZNetScene.instance.GetPrefab("vfx_Poison"), .2f);
            WindInfusionVFX = BuildVFX(ZNetScene.instance.GetPrefab("vfx_MeadSwimmer"), .08f);
        }

        private static GameObject BuildVFX(GameObject source, float scale)
        {
            if (source == null) return null;
            var template = new GameObject($"InfusionVFX_{source.name}");
            template.SetActive(false);
            Object.DontDestroyOnLoad(template);

            if (template.GetComponent<TimedDestruction>() != null) { Object.Destroy(template.GetComponent<TimedDestruction>()); };

            foreach (var particles in source.GetComponentsInChildren<ParticleSystem>(true))
            {
                if (particles.transform == source.transform) continue;
                var clone = Object.Instantiate(particles.gameObject, template.transform);
                clone.transform.localPosition = Vector3.zero;
                clone.transform.localRotation = Quaternion.identity;
                clone.transform.localScale *= scale;
            }
            return template;
        }
    }
}
