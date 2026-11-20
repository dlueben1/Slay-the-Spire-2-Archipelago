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

/// <summary>
/// The draw pile screen's footer says its cards are sorted by rarity. With the Pile Sight bonus
/// they are in draw order, so the footer says that instead. It is also the only sign of the bonus
/// in multiplayer, where the Frozen Eye relic is not granted.
/// </summary>
[HarmonyPatch(typeof(NCardPileScreen), nameof(NCardPileScreen._Ready))]
public static class Patches_PileSightDrawOrderFooter
{
    [HarmonyPostfix]
    public static void Postfix(NCardPileScreen __instance)
    {
        if (__instance.Pile.Type != PileType.Draw || !PileSight.ShowsDrawOrder)
            return;
        // The game's own footer, with its second line changed.
        __instance._bottomLabel.Text =
            "[center]Cards are drawn from here at the start of each turn.\n"
            + "[gold](Frozen Eye: cards shown are in draw order)[/gold]";
    }
}
