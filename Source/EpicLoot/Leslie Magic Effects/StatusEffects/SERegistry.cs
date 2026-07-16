using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using Jotunn.Managers;
using Jotunn.Entities;

namespace EpicLootLeslieAlphaTest.src.StatusEffects
{
    public static class SERegistry
    {
        public static void RegisterStatusEffects()
        {
            Register<SE_Retaliation>(SE_Retaliation.EffectName);
            Register<SE_Onslaught>(SE_Onslaught.EffectName);
            Register<SE_FireInfused>(SE_FireInfused.EffectName);
            Register<SE_FrostInfused>(SE_FrostInfused.EffectName);
            Register<SE_LightningInfused>(SE_LightningInfused.EffectName);
            Register<SE_PoisonInfused>(SE_PoisonInfused.EffectName);
            Register<SE_WindInfused>(SE_WindInfused.EffectName);

        }

        private static void Register<T>(string effectName) where T : StatusEffect
        {
            var se = ScriptableObject.CreateInstance<T>();
            se.name = effectName;
            se.m_name = effectName;
            ItemManager.Instance.AddStatusEffect(new CustomStatusEffect(se, fixReference: false));
        }
    }
}
