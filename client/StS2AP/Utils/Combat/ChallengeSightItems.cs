using StS2AP.Data;
using StS2AP.Models;

namespace StS2AP.Utils;

// Maybe in the future this would be suited to F# given the different states represented
/// <summary>
/// Shared rules for the challenge options that hide information until Sight items
/// arrive. They are display-only, so each client follows its own slot and multiplayer peers
/// never need to agree.
/// </summary>
internal static class ChallengeSightItems
{
    private static ArchipelagoProgress? _countedProgress;
    private static long _countedRevision = -1;
    private static readonly Dictionary<ItemTable.APItem, int> Counts = new();

    /// <summary>Whether a slot's challenge option hides anything for the local player.</summary>
    internal static bool Applies(bool optionEnabled)
    {
        // Vanilla guests have no slot of their own, so nothing is hidden from them.
        if (!optionEnabled || MultiplayerSupport.IsLocalGuest)
            return false;
        return MultiplayerSupport.IsRealMultiplayerRun || GameUtility.CurrentConfig is not null;
    }

    /// <summary>
    /// Whether a challenge with no partial steps still hides everything: its option is on and no
    /// copy of its Sight item has arrived.
    /// </summary>
    internal static bool IsHidden(bool optionEnabled, ItemTable.APItem sightItem) =>
        Applies(optionEnabled) && Received(sightItem) == 0;

    /// <summary>
    /// Copies of a Sight item received by this slot. Combat visuals ask on every state change,
    /// so receipts are only counted again when the ledger changes.
    /// </summary>
    internal static int Received(ItemTable.APItem item)
    {
        ArchipelagoProgress progress = ArchipelagoClient.Progress;
        long revision = progress.Items.Revision;
        if (!ReferenceEquals(progress, _countedProgress) || revision != _countedRevision)
        {
            Counts.Clear();
            _countedProgress = progress;
            _countedRevision = revision;
        }
        if (!Counts.TryGetValue(item, out int count))
        {
            count = progress.AllReceivedItems.Count(receipt => receipt.Item.ItemId == (long)item);
            Counts[item] = count;
        }
        return count;
    }

    /// <summary>
    /// Every Sight Bonuses name the APWorld's <c>sight_bonuses</c> option accepts, which is also
    /// what slots from older APWorlds without that option get.
    /// </summary>
    internal static readonly string[] AllBonusNames =
        ["IntentSight", "HealthSight", "PileSight", "MapSight", "CardSight", "RelicSight"];

    /// <summary>Copies of a Sight item it takes to add its Sight Bonus.</summary>
    private const int BonusCopies = 2;

    /// <summary>
    /// Whether a Sight item's bonus is unlocked: its challenge applies, the slot's Sight Bonuses
    /// list names it, and a second copy has arrived. Use this for bonuses that only change what the
    /// local player sees.
    /// </summary>
    internal static bool HasBonus(bool optionEnabled, ItemTable.APItem sightItem, string bonusName) =>
        Applies(optionEnabled)
        && ArchipelagoClient.Settings?.SightBonuses.Contains(bonusName) == true
        && Received(sightItem) >= BonusCopies;

    /// <summary>
    /// <see cref="HasBonus"/> for bonuses that change the fight or its rewards. Those stay out of
    /// multiplayer: every peer simulates the run, and they have no way to agree on one slot's
    /// Sight receipts.
    /// </summary>
    internal static bool HasGameplayBonus(bool optionEnabled, ItemTable.APItem sightItem, string bonusName) =>
        !MultiplayerSupport.IsMultiplayerScope && HasBonus(optionEnabled, sightItem, bonusName);

    /// <summary>
    /// Sight steps the slot has unlocked, from 0 (everything hidden) to <paramref name="fullSteps"/>
    /// (everything shown). Any copy shows everything, so a second copy in the pool is a spare. The
    /// partial steps stay in the Sight levels for a return to one progressive step per copy.
    /// </summary>
    internal static int UnlockedSteps(ItemTable.APItem item, int fullSteps)
    {
        int received = Received(item);
        // One step per copy, for progressive Sight items:
        // return Math.Min(received, fullSteps);
        return received > 0 ? fullSteps : 0;
    }
}
