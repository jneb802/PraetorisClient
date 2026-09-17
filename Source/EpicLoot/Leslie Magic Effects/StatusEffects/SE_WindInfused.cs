using EpicLootAPI;
using EpicLootLeslieAlphaTest.src.StatusEffects.VFX;
using EpicLootLeslieAlphaTest.src.Utilities;
using UnityEngine;
using static EpicLootLeslieAlphaTest.src.MagicEffectss.Infusion;

namespace EpicLootLeslieAlphaTest.src.StatusEffects
{
    public class SE_WindInfused : SE_Stats
    {
        public const string EffectName = "SE_WindInfused";
        private GameObject m_vfxInstance;
        private GameObject m_vfxInstance2;
        private ItemDrop.ItemData weapon;
        private float infusedWindValue = 0f;
        private float baseSpeed = 1f;
        public override void Setup(Character character)
        {
            base.Setup(character);
            if (Player.m_localPlayer == null) return;
            weapon = Player.m_localPlayer.GetCurrentWeapon();
            baseSpeed = m_character.m_animator.speed;
            //m_icon = ObjectDB.instance.GetStatusEffect("Warm".GetStableHashCode())?.m_icon;
            m_name = "Wind Infused";
            m_ttl = 120f;
        }

        public override void Stop()
        {
            base.Stop();
            m_character.m_animator.speed = baseSpeed;
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
            
            GameObject infusePlacement = Player.m_localPlayer.m_visEquipment.m_rightItemInstance; // do transforms for all weapon if I have time
            bool isFists = weapon.m_shared.m_animationState == ItemDrop.ItemData.AnimationState.Unarmed;
            Transform rightHand = Player.m_localPlayer.m_visEquipment.m_rightHand;
            Transform leftHand = Player.m_localPlayer.m_visEquipment.m_leftHand;

            if (Player.m_localPlayer.InAttack() && Player.m_localPlayer.GetCurrentWeapon() == weapon && baseSpeed <= (3 * baseSpeed))
            {
                var speed = m_character.m_animator.speed += Player.m_localPlayer.GetTotalActiveMagicEffectValue("Infusion", .01f); // adds per frame while attack I know its jank
                //Jotunn.Logger.LogWarning($" Base Speed = {baseSpeed} Added Speed{speed} current speed {m_character.m_animator.speed}");
            }


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
                m_vfxInstance = Object.Instantiate(InfusionVFX.WindInfusionVFX, vis.m_rightHand);
                m_vfxInstance.SetActive(true);
                PlayAll(m_vfxInstance);
            }
            else
            {
                m_vfxInstance = Object.Instantiate(InfusionVFX.WindInfusionVFX, vis.m_rightHand);
                m_vfxInstance2 = Object.Instantiate(InfusionVFX.WindInfusionVFX, vis.m_leftHand);
                m_vfxInstance.SetActive(true);
                m_vfxInstance2.SetActive(true);
                PlayAll(m_vfxInstance);
                PlayAll(m_vfxInstance2);
            }
        }

        private void PlayAll(GameObject vfx)
        {   
            var light = vfx.GetComponentInChildren<Light>();
            if (light != null) light.gameObject.SetActive(false);

            foreach (var ps in vfx.GetComponentsInChildren<ParticleSystem>())
            {
                if (ps.transform == vfx.transform) { ps.Stop(); continue; }

                var main = ps.main;
                main.loop = true;
                main.duration = 2f;
                main.startColor = new Color(194f / 255f, 1f, 146f / 255f, 1f);
                ps.Play();
            }
        }

        private void DestroyVFX()
        {
            if (m_vfxInstance != null) { Object.Destroy(m_vfxInstance); m_vfxInstance = null; }
            if (m_vfxInstance2 != null) { Object.Destroy(m_vfxInstance2); m_vfxInstance2 = null; }
        }
    }
}
