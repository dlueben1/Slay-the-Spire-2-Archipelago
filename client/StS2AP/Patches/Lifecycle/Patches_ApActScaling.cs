using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;

namespace StS2AP.Patches
{
    /// <summary>
    /// Applies the Act Scaling Percent run modifier to enemy HP. CreateCreature rolls a monster's
    /// HP and scales it for multiplayer before any health bar exists; it builds both encounter
    /// monsters and summons.
    /// </summary>
    [HarmonyPatch(typeof(CombatState), nameof(CombatState.CreateCreature))]
    public static class Patches_ApActScaling
    {
        [HarmonyPostfix]
        public static void Postfix(CombatState __instance, CombatSide side, Creature __result)
        {
            if (side != CombatSide.Enemy)
                return;
            __instance.RunState.Modifiers.OfType<ApActScaling>().FirstOrDefault()?.ScaleNewEnemy(__result);
        }
    }
}
