using StS2AP.Data;
using StS2AP.Models;

namespace StS2AP.Utils;

// Maybe in the future this would be suited to F# given the different states represented
/// <summary>
/// Shared rules for the challenge options that hide information until progressive Sight items
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
}
