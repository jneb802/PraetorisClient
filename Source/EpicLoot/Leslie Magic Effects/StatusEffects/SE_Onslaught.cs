using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using EpicLootLeslieAlphaTest.src.Utilities;
using EpicLootAPI;

namespace EpicLootLeslieAlphaTest.src.StatusEffects
{
    public class SE_Onslaught : SE_Stats
    {
        public const string EffectName = "SE_Onslaught";
        public int m_stacks = 0;
        public int m_maxStacks = 9999;
        private float m_onslaughtHits = 0f;
        private const float m_expire = 2f;
        
        public override void Setup(Character character)
        {
            base.Setup(character);
            if (character.m_animator != null)
            {
                m_icon = ObjectDB.instance.GetItemPrefab("MaceIron")?.GetComponent<ItemDrop>()?.m_itemData.m_shared.m_icons[0];
                m_name = "Onslaught Hits";
                m_flashIcon = true;
                ++m_stacks;
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
            if (m_stacks <= 0 && !m_character.InAttack())
            {
                m_character.GetSEMan().RemoveStatusEffect(this);
            }
            if (!Player.m_localPlayer.InAttack() && m_time > 1.5f)
            {
                m_onslaughtHits = 0;
            }
        }

        public override void ModifyAttack(Skills.SkillType skill, ref HitData hitData)
        {
            base.ModifyAttack(skill, ref hitData);
            Jotunn.Logger.LogWarning($"stacks{m_stacks}");
            int? chain = (m_character as Humanoid)?.m_currentAttack?.m_currentAttackCainLevel;
            //Jotunn.Logger.LogWarning($" # of stacks {m_stacks} {chain < 2}");
            if (m_stacks <= 0 || chain < 2) return;
            {
                //Jotunn.Logger.LogWarning($"stack consumed");
                ++m_onslaughtHits;
                Jotunn.Logger.LogWarning($"{m_onslaughtHits}");
                float onslaughtBonus = 1f + (m_onslaughtHits * (Player.m_localPlayer.GetTotalActiveMagicEffectValue("Onslaught", .01f)));
                hitData.ApplyModifier(onslaughtBonus);
            }
        }

        public override string GetIconText() // show stacks instead of timer
        {
            return $"{m_onslaughtHits}";
        }
    }
}
