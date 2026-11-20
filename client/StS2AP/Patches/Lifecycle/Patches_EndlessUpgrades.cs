using HarmonyLib;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Saves.Runs;
using StS2AP.Models;

namespace StS2AP.Patches;

/// <summary>
/// Lifts the one-upgrade cap for the Endless Upgrades boon. Every upgrade check in the game
/// (campfires, Armaments, Whetstone, the eggs, the upgrade screen) goes through
/// <see cref="CardModel.IsUpgradable"/>, which compares the level with
/// <see cref="CardModel.MaxUpgradeLevel"/>, and the level setter throws past that cap.
///
/// Only the base getter is patched, so cards that override the cap keep it: curses and statuses
/// stay unupgradable and Dingy Dagger keeps its own. The getter is virtual, so callers cannot
/// inline it, including RitsuLib's earlier replacement of <c>CardModel.UpgradeInternal</c>.
/// Patching the private level setter instead would not be safe for that reason.
/// </summary>
internal static class Patches_EndlessUpgrades
{
    // Cards rebuilt from a save, a multiplayer message or run history are upgraded level by level
    // before they have an owner, so the boon cannot be read from their run yet.
    [ThreadStatic]
    private static int _cardsBeingLoaded;

    [HarmonyPatch(typeof(CardModel), nameof(CardModel.MaxUpgradeLevel), MethodType.Getter)]
    internal static class MaxUpgradeLevel
    {
        [HarmonyPostfix]
        public static void LiftCap(CardModel __instance, ref int __result)
        {
            // A card already past one upgrade came from a run with the boon. Keeping its cap
            // lifted also gives it the "+N" title the game uses for multi-upgrade cards.
            if (_cardsBeingLoaded > 0
                || __instance.CurrentUpgradeLevel > 1
                || ApEndlessUpgrades.AppliesTo(__instance))
            {
                __result = ApEndlessUpgrades.MaxUpgradeLevel;
            }
        }
    }

    [HarmonyPatch(typeof(CardModel), nameof(CardModel.FromSerializable))]
    internal static class FromSerializable
    {
        [HarmonyPrefix]
        public static void BeginLoad() => _cardsBeingLoaded++;

        [HarmonyFinalizer]
        public static void EndLoad() => _cardsBeingLoaded--;
    }
}
