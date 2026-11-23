using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Enchantments;

namespace StS2AP.Patches;

/// <summary>
/// The game only puts Inky on Shivs (Blade of Ink), so Inky never limits which cards it fits. Its
/// OnPlay applies Weak to the card's chosen target, and on a card with none (a Self skill, a power,
/// a random-target attack) that target is null and the play throws. AP bonus items, Ember Enchanting
/// and the deck pickers all ask CanEnchant, so limiting it here keeps Inky off those cards.
/// </summary>
public static class Patches_InkyTargets
{
    /// <summary>Cards whose play has an enemy target, or hits every enemy.</summary>
    public static bool TargetsEnemies(CardModel card) =>
        card.TargetType is TargetType.AnyEnemy or TargetType.AllEnemies;

    // Inky does not override CanEnchant, so its calls run the base method patched here.
    [HarmonyPatch(typeof(EnchantmentModel), nameof(EnchantmentModel.CanEnchant))]
    public static class CanEnchant
    {
        [HarmonyPostfix]
        public static void Postfix(EnchantmentModel __instance, CardModel card, ref bool __result)
        {
            if (__result && __instance is Inky && !TargetsEnemies(card))
                __result = false;
        }
    }
}
