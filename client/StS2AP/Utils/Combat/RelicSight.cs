using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Relics;
using StS2AP.Data;

namespace StS2AP.Utils.Combat;

/// <summary>
/// The Hidden Relics challenge: relics are unknown, with a locked icon and no name or text, and the
/// local player's relic counters show "?", until the slot receives any Relic Sight. Flashes and the
/// other feedback stay, so relics can be worked out by watching them.
/// </summary>
internal static class RelicSight
{
    internal static bool IsHidden =>
        ChallengeSightItems.IsHidden(
            ArchipelagoClient.Settings?.HiddenRelics == true,
            ItemTable.APItem.RelicSight);

    /// <summary>
    /// The Relic Sight bonus: relic rewards offer 2 relics to choose from. It changes rewards, so
    /// it stays out of multiplayer.
    /// </summary>
    internal static bool HasChoiceBonus =>
        ChallengeSightItems.HasGameplayBonus(
            ArchipelagoClient.Settings?.HiddenRelics == true,
            ItemTable.APItem.RelicSight,
            "RelicSight");

    /// <summary>Relics a Relic Sight bonus reward offers.</summary>
    internal const int ChoiceCount = 2;

    /// <summary>
    /// Whether this relic's counter is hidden. Only the local player's relics are counted here. The
    /// owner field is read directly because the Owner getter asserts on canonical models.
    /// </summary>
    internal static bool Hides(RelicModel relic) => IsHidden && LocalContext.IsMe(relic._owner);

    /// <summary>Applies a newly received Relic Sight to the relics already in the top bar.</summary>
    internal static void RefreshVisibleRelics()
    {
        if (NRun.Instance?.GlobalUi?.RelicInventory is not { } inventory || !inventory.IsNodeReady())
            return;
        foreach (NRelicInventoryHolder holder in inventory.RelicNodes)
        {
            // Reload redraws the icon, which Hidden Relics replaced with the unknown one.
            holder.Relic.Reload();
            holder.RefreshAmount();
            holder.RefreshStatus();
        }
    }
}
