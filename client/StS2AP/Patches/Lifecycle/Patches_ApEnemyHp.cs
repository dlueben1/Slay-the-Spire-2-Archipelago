using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;

namespace StS2AP.Patches
{
    /// <summary>
    /// Applies the Enemy Max HP Percent run modifier. CreateCreature rolls a monster's HP and
    /// scales it for multiplayer before any health bar exists; it builds both encounter monsters
    /// and summons.
    /// </summary>
    [HarmonyPatch(typeof(CombatState), nameof(CombatState.CreateCreature))]
    public static class Patches_ApEnemyHp
    {
        [HarmonyPostfix]
        public static void Postfix(CombatState __instance, CombatSide side, Creature __result)
        {
            if (side != CombatSide.Enemy)
                return;
            __instance.RunState.Modifiers.OfType<ApEnemyHp>().FirstOrDefault()?.ScaleNewEnemy(__result);
        }
    }
}
