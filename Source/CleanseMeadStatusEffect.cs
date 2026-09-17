using System.Collections.Generic;
using UnityEngine;

namespace PraetorisClient
{
    internal sealed class CleanseMeadStatusEffect : StatusEffect
    {
        private const string InternalName = "SE_Warp_MeadCleanse";
        private const string Category = "PraetorisCleanseMead";
        private const float DurationSeconds = 120f;

        public static CleanseMeadStatusEffect Create(Sprite icon)
        {
            CleanseMeadStatusEffect effect = ScriptableObject.CreateInstance<CleanseMeadStatusEffect>();
            effect.name = InternalName;
            effect.m_name = "Cleanse Mead";
            effect.m_category = Category;
            effect.m_icon = icon;
            effect.m_cooldownIcon = true;
            effect.m_ttl = DurationSeconds;
            effect.m_tooltip = "Cleanses ALL status effects. Be mindful of your burdens!";
            effect.m_startMessageType = MessageHud.MessageType.Center;
            effect.m_startMessage = "You feel cleansed.";
            return effect;
        }

        public override void Setup(Character character)
        {
            base.Setup(character);

            if (character == null)
            {
                return;
            }

            SEMan seMan = character.GetSEMan();
            if (seMan == null)
            {
                return;
            }

            int cleanseHash = NameHash();
            List<StatusEffect> activeEffects = new List<StatusEffect>(seMan.GetStatusEffects());
            foreach (StatusEffect activeEffect in activeEffects)
            {
                if (!activeEffect || activeEffect.NameHash() == cleanseHash)
                {
                    continue;
                }

                seMan.RemoveStatusEffect(activeEffect.NameHash(), true);
            }
        }
    }
}
