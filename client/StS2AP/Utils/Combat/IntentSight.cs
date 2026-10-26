using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using StS2AP.Data;
using StS2AP.Models;

namespace StS2AP.Utils;

/// <summary>How much of an enemy's intent the local player may see.</summary>
internal enum IntentDetail
{
    // No intent icons, intent tooltips or Intent Graph overlay.
    None = 0,
    // Intent icons and tooltip titles, without damage or other numbers.
    Types = 1,
    // Everything the game shows normally.
    Full = 2,
}

/// <summary>
/// The Hidden Intents challenge: intents stay hidden until the slot receives Progressive Intent
/// Sight items, one step per copy. Display-only, so each client follows its own slot and
/// multiplayer peers never need to agree.
/// </summary>
internal static class IntentSight
{
    private static ArchipelagoProgress? _countedProgress;
    private static long _countedRevision = -1;
    private static int _receivedCount;

    internal static IntentDetail Detail
    {
        get
        {
            // Vanilla guests have no slot of their own, so they always see intents.
            if (ArchipelagoClient.Settings?.HiddenIntents != true || MultiplayerSupport.IsLocalGuest)
                return IntentDetail.Full;
            if (!MultiplayerSupport.IsRealMultiplayerRun && GameUtility.CurrentConfig is null)
                return IntentDetail.Full;
            return (IntentDetail)Math.Min(ReceivedCount, (int)IntentDetail.Full);
        }
    }

    // Intent visuals refresh on every combat state change, so count receipts only when the
    // ledger changes.
    private static int ReceivedCount
    {
        get
        {
            ArchipelagoProgress progress = ArchipelagoClient.Progress;
            long revision = progress.Items.Revision;
            if (!ReferenceEquals(progress, _countedProgress) || revision != _countedRevision)
            {
                _receivedCount = progress.AllReceivedItems.Count(receipt =>
                    receipt.Item.ItemId == (long)ItemTable.APItem.ProgressiveIntentSight);
                _countedProgress = progress;
                _countedRevision = revision;
            }
            return _receivedCount;
        }
    }

    /// <summary>Applies a newly received Intent Sight to the intents already on screen.</summary>
    internal static void RefreshVisibleIntents()
    {
        if (NCombatRoom.Instance is not { } room)
            return;
        foreach (NCreature creature in room.CreatureNodes)
        {
            foreach (NIntent intent in creature.IntentContainer.GetChildren().OfType<NIntent>())
            {
                if (intent._intent != null)
                    intent.UpdateVisuals();
            }
        }
    }
}
