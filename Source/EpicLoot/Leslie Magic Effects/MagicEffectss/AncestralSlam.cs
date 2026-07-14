using EpicLoot;
using EpicLootAPI;
using EpicLootLeslieAlphaTest.src.Utilities;
using HarmonyLib;
using PlayFab.EconomyModels;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.UIElements;
using static Attack;

namespace EpicLootLeslieAlphaTest.src.MagicEffectss;

public partial class AncestralSlam
{
    private const float damageModifier = 1f;
    private static int _lastAttackCainLevel = 0;
    private static string _lastAttackAnim = "";
    private static Attack _lastAttack = null;
    private static float _lastAttackRange = 2f;
    public static readonly HashSet<GameObject> ancestralClonesHASH = new HashSet<GameObject>();

    [HarmonyPatch(typeof(Attack), nameof(Attack.OnAttackTrigger))]
    public class Attack_Trigger
    {
        public static void Prefix(Attack __instance)
        {
            if (__instance.m_character is Player)
            {
                _lastAttackCainLevel = __instance.m_currentAttackCainLevel;
                _lastAttackAnim = __instance.m_attackAnimation;
                _lastAttackRange = __instance.m_attackRange;
                _lastAttack = __instance;
            }
        }
    }

    [HarmonyPatch(typeof(Character), nameof(Character.Damage))]
    public class Character_Damage_SpawnClone_Patch
    {
        public static void Postfix(Character __instance, HitData hit)
        {
            if (Player.m_localPlayer.GetCurrentWeapon() == null) return;
            ItemDrop.ItemData weapon = Player.m_localPlayer.GetCurrentWeapon();
            if (weapon == null) return;
            if (!weapon.HasMagicEffect("AncestralSlam")) return;
            Character attacker = hit.GetAttacker();
            if (attacker == null || attacker != Player.m_localPlayer) return;
            if (ancestralClonesHASH.Contains(attacker.gameObject)) return;

            Vector3 enemeyPos = __instance.transform.position;
            Vector3 dirTowardsEnemey = (enemeyPos - Player.m_localPlayer.transform.position).normalized;
            Quaternion rotation = dirTowardsEnemey != Vector3.zero ? Quaternion.LookRotation(dirTowardsEnemey) : Player.m_localPlayer.transform.rotation;

            SpawnAncestralSlam(enemeyPos - dirTowardsEnemey * 1.5f, rotation, weapon);
        }
    }

    [HarmonyPatch(typeof(Character), nameof(Character.Damage))]
    public class Character_Clone_Damage_Patch
    {
        public static void Prefix(Character __instance, HitData hit)
        {
            if (Player.m_localPlayer == null) return; 
            ItemDrop.ItemData weapon = Player.m_localPlayer.GetCurrentWeapon();
            if (weapon == null) return;
            if (!weapon.HasMagicEffect("AncestralSlam")) return;
            Character attacker = hit.GetAttacker();
            if (attacker == null || !ancestralClonesHASH.Contains(attacker.gameObject)) return;

            float skillFactor = Player.m_localPlayer.GetSkillFactor(weapon.m_shared.m_skillType);
            float randomVarience = Random.Range(.96f, 1.04f);
            HitData.DamageTypes dmg = weapon.m_shared.m_damages.Clone();
            if (_lastAttackCainLevel >= 2)
            {
                dmg.Modify(((skillFactor * .75f + .25f) * 2f) * randomVarience);
            }
            else
            {
                dmg.Modify((skillFactor * .75f + .25f) * randomVarience);
            }
            hit.m_damage = dmg;
        }
    }

    [HarmonyPatch(typeof(EnemyHud), nameof(EnemyHud.ShowHud))] // stop clone health bar from spawning
    internal class EnemyHud_ShowHud_Patch
    {
        static bool Prefix(Character c)
        {
            return !ancestralClonesHASH.Contains(c.gameObject);
        }
    }

    internal static void SpawnAncestralSlam(Vector3 position, Quaternion rotation, ItemDrop.ItemData weapon)
    {
        if (HumanoidFactory.playerAncestor == null) return;

        GameObject ancestral_CloneFinal = Object.Instantiate(HumanoidFactory.playerAncestor, position, rotation);
        ancestralClonesHASH.Add(ancestral_CloneFinal);
        ancestral_CloneFinal.SetActive(true);

        bool isSecondary = weapon.m_shared.m_secondaryAttack.m_attackAnimation == _lastAttackAnim;
        Humanoid ancestral_CloneFinalFinalForSure = ancestral_CloneFinal.GetComponent<Humanoid>();

        ancestral_CloneFinalFinalForSure.m_rightItem = weapon.Clone();
        //mickey mouse player skill level weapon damage humanoid no skills.

        Animator ancestral_CloneAnimationTime = ancestral_CloneFinal.GetComponent<Animator>();
        ancestral_CloneFinalFinalForSure.m_lookDir = rotation * Vector3.forward;

        ancestral_CloneFinalFinalForSure.StartAttack(null, isSecondary);  // spawn clone to attack

        if (ancestral_CloneFinalFinalForSure.m_currentAttack != null)
        {
            ancestral_CloneFinalFinalForSure.m_currentAttack.m_attackRange *= 1.2f;
            ancestral_CloneFinalFinalForSure.m_currentAttack.m_currentAttackCainLevel = _lastAttackCainLevel;
        }
        ancestral_CloneFinalFinalForSure.StartCoroutine(DelayedDestruction(ancestral_CloneFinal.GetComponent<ZNetView>(), ancestral_CloneFinal, 2.5f));
    }
    

    private static IEnumerator DelayedDestruction(ZNetView zview, GameObject go, float attackTime)
    {
        yield return new WaitForSeconds(attackTime + .5f);
        Character FF = go.GetComponent<Character>();
        if (FF != null)
        {
            EnemyHud.instance?.RemoveCharacterHud(FF);
            Character.Instances.Remove(FF);
        }
        AncestralSlam.ancestralClonesHASH.Remove(go);
        if (zview != null && zview.IsOwner())
        {
            ZNetScene.instance.Destroy(zview.gameObject);
        }
        yield break;
    }
}
