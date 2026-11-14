using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.Cards;
using StS2AP.Utils.Combat;

namespace StS2AP.Patches;

/// <summary>
/// The Hidden Cards challenge reuses the game's own look for cards the player has never seen in
/// the Compendium. Every card node draws its art in Reload and its title, text and costs in
/// UpdateVisuals, and both read the card's visibility first. Card holders also skip hover tips
/// for a card that is not visible. Known leaks left on purpose: the card type plaque, the frame
/// and banner colours (character and rarity), the energy spent when a card is played, and the
/// card's effects once it is played.
/// </summary>
public static class Patches_HiddenCards
{
    /// <summary>A card loads its model, or a pooled card is reused for another model.</summary>
    [HarmonyPatch(typeof(NCard), nameof(NCard.Reload))]
    public static class HideOnReload
    {
        [HarmonyPrefix]
        public static void Prefix(NCard __instance) => CardSight.HideIfNeeded(__instance);
    }

    /// <summary>
    /// Covers a card that was loaded before the slot's settings arrived. Only Reload blurs the
    /// art, so a card switched here is reloaded before it is drawn.
    /// </summary>
    [HarmonyPatch(typeof(NCard), nameof(NCard.UpdateVisuals))]
    public static class HideOnUpdateVisuals
    {
        [HarmonyPrefix]
        public static void Prefix(NCard __instance)
        {
            if (CardSight.HideIfNeeded(__instance))
                __instance.Reload();
        }
    }
}
