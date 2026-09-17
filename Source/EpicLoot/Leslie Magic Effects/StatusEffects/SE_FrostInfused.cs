using UnityEngine;
using EpicLootLeslieAlphaTest.src.StatusEffects.VFX;
using EpicLootLeslieAlphaTest.src.Utilities;
using EpicLootAPI;

namespace EpicLootLeslieAlphaTest.src.StatusEffects
{
    public class SE_FrostInfused : SE_Stats
    {
        public const string EffectName = "SE_FrostInfused";
        private GameObject m_vfxInstance;
        private GameObject m_vfxInstance2;
        public float Efficacy {get; set;}
        private ItemDrop.ItemData weapon;
        private float infusedFrostDamage = 0f;

        public override void Setup(Character character)
        {
            base.Setup(character);
            if (Player.m_localPlayer == null) return;
            Efficacy = MagicEffectss.Infusion.Efficacy;
            m_icon = ObjectDB.instance.GetItemPrefab("SwordMistwalker")?.GetComponent<ItemDrop>()?.m_itemData.m_shared.m_icons[0];
            m_name = "Frost Infused";
            m_ttl = 120f;

            // damage component

            weapon = Player.m_localPlayer?.GetCurrentWeapon();
            if (weapon == null || WA.IsRangedWeapon(weapon)) return;
            float waeponBaseDmg = weapon.GetDamage().GetTotalDamage();
            float realMeleeMultiplier = WA.IsHeavyWeapon(weapon) ? 2f : 1f;
            Jotunn.Logger.LogWarning($"[FrostInfused] weapon base damage:{waeponBaseDmg} efficacy:{Efficacy} weapon multiplier:{realMeleeMultiplier} added:{waeponBaseDmg * Player.m_localPlayer.GetTotalActiveMagicEffectValue("Infusion", .01f)} + {Player.m_localPlayer.GetTotalActiveMagicEffectValue("Infusion", .01f) * Efficacy} * {realMeleeMultiplier}");
            infusedFrostDamage += ((waeponBaseDmg * Player.m_localPlayer.GetTotalActiveMagicEffectValue("Infusion", .01f)) + (Efficacy * Player.m_localPlayer.GetTotalActiveMagicEffectValue("Infusion", .1f)) * realMeleeMultiplier);
            weapon.m_shared.m_damages.m_frost += infusedFrostDamage;

            Jotunn.Logger.LogWarning($"Added {infusedFrostDamage} frost damage");
        }

        public override void Stop()
        {
            base.Stop();
            if (weapon != null)
            {
                weapon.m_shared.m_damages.m_frost-= infusedFrostDamage;
            }
            if (m_vfxInstance != null)
            {
                UnityEngine.Object.Destroy(m_vfxInstance);
            }
            if (m_vfxInstance2 != null)
            {
                UnityEngine.Object.Destroy(m_vfxInstance2);
            }

        }

        public override void UpdateStatusEffect(float dt)
        {
            base.UpdateStatusEffect(dt);
            if (Player.m_localPlayer == null) return;

            if (Player.m_localPlayer.GetCurrentWeapon() == weapon && m_vfxInstance == null)
            {
                CreateVFX();
            }
            else if (Player.m_localPlayer.GetCurrentWeapon() != weapon && m_vfxInstance != null)
            {
                DestroyVFX();
            }
        }

        private void CreateVFX()
        {
            var vis = Player.m_localPlayer.m_visEquipment;
            if (!WA.IsTwoHanded(weapon))
            {
                m_vfxInstance = Object.Instantiate(InfusionVFX.FrostInfusionVFX, vis.m_rightHand);
                m_vfxInstance.SetActive(true);
            }
            else
            {
                m_vfxInstance = Object.Instantiate(InfusionVFX.FrostInfusionVFX, vis.m_rightHand);
                m_vfxInstance2 = Object.Instantiate(InfusionVFX.FrostInfusionVFX, vis.m_leftHand);
                m_vfxInstance.SetActive(true);
                m_vfxInstance2.SetActive(true);
            }
        }

        private void DestroyVFX()
        {
            if (m_vfxInstance != null) { Object.Destroy(m_vfxInstance); m_vfxInstance = null; }
            if (m_vfxInstance2 != null) { Object.Destroy(m_vfxInstance2); m_vfxInstance2 = null; }
        }
    }
}
