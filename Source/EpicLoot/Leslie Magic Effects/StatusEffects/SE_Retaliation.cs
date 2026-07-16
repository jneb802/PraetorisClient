using System.Collections.Generic;
using UnityEngine;
using EpicLootLeslieAlphaTest.src.Utilities;
using EpicLootLeslieAlphaTest.src.MagicEffectss;
using EpicLootAPI;

namespace EpicLootLeslieAlphaTest.src.StatusEffects
{
    public class SE_Retaliation : SE_Stats
    {
        public const string EffectName = "SE_Retaliation";
        public int m_stacks = 0;
        public int m_maxStacks = 1; // make ME value
        private const float m_expire = 5f;
        private bool addedRetaliationSpeed = false;
        private bool amAttacking = false;
        private static readonly Dictionary<string, float> _animSpeeds = new()
        {
            { "atgeir_attack", 1.2f },
            { "atgeir_secondary", 2.0f },
            { "knife_stab", 1.2f },
            { "knife_secondary", 1.1f },
            { "battleaxe_attack", 1.6f },
            { "swing_axe", 1.2f },
            { "swing_longsword", 1.2f },
            { "mace_secondary", 1.35f },
            { "spear_poke", 1.2f },
            { "sword_secondary", 0.4f },
            { "dualaxes", 1.1f },
            { "dualaxes_secondary", 1.1f },
            { "dual_knives", 1.1f },
            { "dual_knives_secondary", 1.2f },
            { "greatsword", 1.2f },
            { "greatsword_secondary", 1.2f },
            { "swing_sledge", 1.4f },
            { "unarmed_attack", 2.2f },
        };

        public override void Setup(Character character)
        {
            base.Setup(character);
            if (character.m_animator != null)
            {
                m_maxStacks = Mathf.RoundToInt(Player.m_localPlayer.GetTotalActiveMagicEffectValue("Retaliation", 1f));
                character.m_animator.speed = (Retaliation.AttackType != null && _animSpeeds.TryGetValue(Retaliation.AttackType, out float s)) ? s : 1.2f;
                m_icon = ObjectDB.instance.GetItemPrefab("ShieldBanded")?.GetComponent<ItemDrop>()?.m_itemData.m_shared.m_icons[3];
                m_name = "Retaliation";
                m_flashIcon = true;
            }
        }

        public void AddStack()
        {
            m_ttl = m_expire;
            if (m_stacks < m_maxStacks)
            {
                ++m_stacks;
                m_time = 0f;
            }
        }

        public void ConsumeStack()
        {
            if (m_stacks <= 0) return;
            --m_stacks;
            m_time = 0f;
        }

        public override void UpdateStatusEffect(float dt)
        {
            base.UpdateStatusEffect(dt);
            ItemDrop.ItemData weapon = Player.m_localPlayer?.GetCurrentWeapon();
            if (WA.IsRangedWeapon(weapon)) return;

            if (!m_character.InAttack() && amAttacking)
            {
                ConsumeStack();
            }
            amAttacking = m_character.InAttack();
            //Jotunn.Logger.LogInfo($"animator speed: {m_character.m_animator.speed} character attack{ m_character.m_attack} string attack type{Retaliation.AttackType}");
            if (m_character.m_animator != null && m_character.m_animator.speed >= 5f && addedRetaliationSpeed == false) //&& m_stacks > 0)
            {
                m_character.m_animator.speed += (Retaliation.AttackType != null && _animSpeeds.TryGetValue(Retaliation.AttackType, out float s)) ? s : 1.2f;
                addedRetaliationSpeed = true;
            }
            if (m_character.m_animator != null && m_character.m_animator.speed < 5f)
            {
                m_character.m_animator.speed = (Retaliation.AttackType != null && _animSpeeds.TryGetValue(Retaliation.AttackType, out float s)) ? s : 1.2f;
            }
            if (m_stacks > 0 && !m_character.InAttack())
            {
                m_character.m_animator.speed = 1f;
            }
            if (m_stacks <= 0 && !m_character.InAttack())
            {
                m_character.GetSEMan().RemoveStatusEffect(this);
                return;
            }
        }

        public override void Stop()
        {
            if (m_character?.m_animator != null)
            {
                m_character.m_animator.speed = 1f;
            }
            base.Stop();
        }

        public override string GetIconText() // show stacks instead of timer
        {
            return $"{m_stacks}";
        }
    }
}
