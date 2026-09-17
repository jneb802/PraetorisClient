using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EpicLootLeslieAlphaTest.src.Utilities
{
    public static class WA
    {
        public static bool IsHeavyWeapon(ItemDrop.ItemData item)
        {
            var state = item.m_shared.m_animationState;
            return state == ItemDrop.ItemData.AnimationState.TwoHandedAxe ||
                    state == ItemDrop.ItemData.AnimationState.TwoHandedClub ||
                    state == ItemDrop.ItemData.AnimationState.Atgeir ||
                    state == ItemDrop.ItemData.AnimationState.Greatsword;
        }

        public static bool IsRangedWeapon(ItemDrop.ItemData item)
        {
            var state = item.m_shared.m_animationState;
            return state == ItemDrop.ItemData.AnimationState.Bow ||
                    state == ItemDrop.ItemData.AnimationState.Crossbow ||
                    state == ItemDrop.ItemData.AnimationState.Staves ||
                    state == ItemDrop.ItemData.AnimationState.MagicItem;
        }

        public static bool IsTwoHanded(ItemDrop.ItemData item)
        {
            var state = item.m_shared.m_animationState;
            return state == ItemDrop.ItemData.AnimationState.TwoHandedAxe ||
                    state == ItemDrop.ItemData.AnimationState.TwoHandedClub ||
                    state == ItemDrop.ItemData.AnimationState.Atgeir ||
                    state == ItemDrop.ItemData.AnimationState.Greatsword ||
                    state == ItemDrop.ItemData.AnimationState.DualAxes ||
                    state == ItemDrop.ItemData.AnimationState.Knives ||
                    state == ItemDrop.ItemData.AnimationState.Unarmed;
        }
    }
}

