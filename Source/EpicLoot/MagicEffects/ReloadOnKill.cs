using System.Reflection;
using HarmonyLib;

namespace PraetorisClient
{
    internal static partial class PraetorisMagicEffects
    {
        private const string ReloadOnKillDefinitionJson = @"{
  ""Type"": ""ReloadOnKill"",
  ""DisplayText"": ""Reload on Kill [Passive]: Killing an enemy with a crossbow instantly reloads your current crossbow."",
  ""Requirements"": {
    ""AllowedItemTypes"": [ ""Bow"" ],
    ""AllowedSkillTypes"": [ ""Crossbows"" ]
  }
}";

        [HarmonyPatch(typeof(Character), "OnDeath")]
        private static class ReloadOnKill_Character_OnDeath_Patch
        {
            private static readonly AccessTools.FieldRef<Character, HitData> LastHit =
                AccessTools.FieldRefAccess<Character, HitData>("m_lastHit");

            private static readonly MethodInfo SetWeaponLoadedMethod =
                AccessTools.Method(typeof(Player), "SetWeaponLoaded");

            private static void Postfix(Character __instance)
            {
                Player player = Player.m_localPlayer;
                if (__instance == null || __instance.IsPlayer() || player == null)
                {
                    return;
                }

                HitData lastHit = LastHit(__instance);
                if (lastHit == null || !lastHit.m_ranged || lastHit.GetAttacker() != player)
                {
                    return;
                }

                ItemDrop.ItemData currentWeapon = player.GetCurrentWeapon();
                if (currentWeapon == null ||
                    currentWeapon.m_shared.m_itemType != ItemDrop.ItemData.ItemType.Bow ||
                    !currentWeapon.m_shared.m_attack.m_requiresReload ||
                    currentWeapon.m_shared.m_skillType != lastHit.m_skill)
                {
                    return;
                }

                if (!PlayerHasEffect(player, ReloadOnKill, out _))
                {
                    return;
                }

                SetWeaponLoadedMethod?.Invoke(player, new object[] { currentWeapon });
            }
        }
    }
}
