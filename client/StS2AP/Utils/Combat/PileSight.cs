using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using StS2AP.Data;

namespace StS2AP.Utils.Combat;

/// <summary>
/// The Hidden Piles challenge: the combat pile buttons stay hidden until the slot receives any
/// Pile Sight.
/// </summary>
internal static class PileSight
{
    internal static bool IsHidden =>
        ChallengeSightItems.IsHidden(
            ArchipelagoClient.Settings?.HiddenPiles == true,
            ItemTable.APItem.PileSight);

    /// <summary>Applies a newly received Pile Sight to the pile buttons already on screen.</summary>
    internal static void RefreshVisiblePiles()
    {
        // The pile buttons are children of the combat UI, so they are ready once it is.
        if (NCombatRoom.Instance?.Ui is not { } ui || !ui.IsNodeReady())
            return;
        foreach (NCombatCardPile pile in new NCombatCardPile[] { ui.DrawPile, ui.DiscardPile, ui.ExhaustPile })
            pile._countLabel.SetTextAutoSize(pile._currentCount.ToString());
    }
}
