using HarmonyLib;
using UnityEngine;

namespace PraetorisClient.EpicLootFeature
{
    // Snapshot the equipment and launch position. Changing weapons or moving after firing
    // must not change the damage of a projectile already in flight.
    internal sealed class PointBlankProjectile : MonoBehaviour
    {
        internal Vector3 LaunchPosition;
        internal float CloseBonus;

        internal float Multiplier(Vector3 hitPoint)
        {
            float distance = Vector3.Distance(LaunchPosition, hitPoint);
            float falloff = Mathf.InverseLerp(2f, 20f, distance);
            return 1f + Mathf.Lerp(CloseBonus, -25f, falloff) * 0.01f;
        }
    }

    [HarmonyPatch(typeof(Projectile), nameof(Projectile.Setup))]
    internal static class PointBlankSetupPatch
    {
        private static void Postfix(Projectile __instance, Character owner, ItemDrop.ItemData item)
        {
            if (owner is not Player player || item == null ||
                (item.m_shared.m_skillType != Skills.SkillType.Bows &&
                 item.m_shared.m_skillType != Skills.SkillType.Crossbows))
            {
                return;
            }

            float bonus = EpicLootApiBridge.GetTotalActiveMagicEffectValueForWeapon(
                player, item, PraetorisMagicEffects.PointBlank, 1f);
            if (bonus <= 0f)
            {
                return;
            }

            PointBlankProjectile effect = __instance.gameObject.AddComponent<PointBlankProjectile>();
            effect.LaunchPosition = __instance.transform.position;
            effect.CloseBonus = bonus;
        }
    }

    [HarmonyPatch(typeof(Projectile), nameof(Projectile.OnHit))]
    internal static class PointBlankHitPatch
    {
        // Piercing Shot can deal damage in its prefix and skip vanilla OnHit.
        [HarmonyPriority(Priority.First)]
        private static void Prefix(Projectile __instance, Vector3 hitPoint, out HitData.DamageTypes? __state)
        {
            __state = null;
            if (__instance.TryGetComponent(out PointBlankProjectile effect))
            {
                __state = __instance.m_damage;
                __instance.m_damage.Modify(effect.Multiplier(hitPoint));
            }
        }

        private static void Finalizer(Projectile __instance, HitData.DamageTypes? __state)
        {
            // Restore even when another prefix skips the hit. Each pierced target gets
            // its own distance multiplier, calculated from the original damage.
            if (__state.HasValue)
            {
                __instance.m_damage = __state.Value;
            }
        }
    }
}
