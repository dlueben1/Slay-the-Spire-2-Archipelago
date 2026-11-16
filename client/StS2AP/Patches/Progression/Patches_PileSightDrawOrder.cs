using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Nodes.Screens.CardLibrary;
using StS2AP.Utils.Combat;

namespace StS2AP.Patches;

/// <summary>
/// The Pile Sight bonus. The draw pile screen sorts its cards by rarity so it does not give away
/// the draw order; with the bonus it keeps the pile's own order, whose first card is drawn next.
/// The grid fills rows left to right, so the next draw is at the top left.
/// </summary>
[HarmonyPatch(typeof(NCardPileScreen), nameof(NCardPileScreen.OnPileContentsChanged))]
public static class Patches_PileSightDrawOrder
{
    [HarmonyPrefix]
    public static bool Prefix(NCardPileScreen __instance)
    {
        if (__instance.Pile.Type != PileType.Draw || !PileSight.ShowsDrawOrder)
            return true;
        // Ascending is the grid's "keep the given order" setting, as the game passes it here.
        __instance._grid.SetCards(
            __instance.Pile.Cards.ToList(),
            __instance.Pile.Type,
            [SortingOrders.Ascending]);
        return false;
    }
}
