using Archipelago.MultiClient.Net.Models;
using Godot;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Runs;
using StS2AP.Data;
using StS2AP.Extensions;
using STS2RitsuLib.Networking.ManagedActions;
using static StS2AP.Data.ItemTable;

namespace StS2AP.Utils;

/// <summary>
/// Every peer's record of each player's AP receipts (<see cref="ApPlayerRunState.ReceiptLedger"/>),
/// written by a queued "receipts arrived" action in the host's action order. Anything that needs
/// another player's AP items (Progressive counts, waiting relics, claim validation) reads this
/// ledger, so every peer sees the same receipts at the same point in the run.
/// </summary>
/// <remarks>
/// The owner requests the action when its run starts or its AP connection comes back, with every
/// receipt past the ledger's last index, and again for each live receipt. AP replays the whole
/// history on every connection, so peers append by index and ignore repeats. A claim the owner
/// requests after a receipt arrives is queued behind that receipt's action, so the receipt is in
/// the ledger by the time the claim executes.
/// </remarks>
public static class ApReceiptLedger
{
    // Keeps one action under RitsuLibManagedNetActions.MaxPayloadBytes (64 KB): a full batch is
    // about 32 KB with large real ids, and 55 KB with every number at its widest.
    private const int MaxReceiptsPerAction = 1000;

    internal static readonly ApQueuedClaimKind<ApReceiptsArrivedActionMessage> Kind = new(
        actionKey: "receipts_arrived",
        noun: "AP receipts",
        apply: Apply,
        describe: Describe,
        notifiesOwnerOfRefusal: false
    );

    // The highest receipt index this process has asked to record in the current run, so a live
    // receipt sends only what earlier requests did not. Run start and reconnect go back to the
    // ledger's own last index, which resends anything a lost request left out.
    private static int _requestedThrough;
    // Campfire checks this process has asked to record in the current run, for the same reason.
    private static readonly HashSet<long> RequestedCampfires = new();

    internal static void EndRun()
    {
        _requestedThrough = 0;
        RequestedCampfires.Clear();
    }

    /// <summary>Owner: records every receipt the ledger lacks, and the run's anytime relics.</summary>
    public static void BeginRun(Player localPlayer)
    {
        EndRun();
        RequestNewReceipts(localPlayer, "run start");
    }

    /// <summary>Owner: the AP connection came back; record what arrived while it was down.</summary>
    public static void QueueReconnectSync()
    {
        Callable.From(() =>
        {
            if (GameUtility.CurrentPlayer is not Player localPlayer)
                return;
            EndRun();
            RequestNewReceipts(localPlayer, "reconnect");
        }).CallDeferred();
    }

    /// <summary>Owner: one receipt arrived live. Receipts a request already carried are skipped.</summary>
    public static void ReceiveLiveReceipt(int receivedItemIndex)
    {
        if (receivedItemIndex <= _requestedThrough || GameUtility.CurrentPlayer is not Player localPlayer)
            return;
        RequestNewReceipts(localPlayer, $"receipt {receivedItemIndex}");
    }

    /// <summary>Owner: the AP server reported checked locations, which may include campfires.</summary>
    public static void ReceiveCheckedLocations()
    {
        if (GameUtility.CurrentPlayer is Player localPlayer)
            RequestNewReceipts(localPlayer, "checked locations");
    }

    private static void RequestNewReceipts(Player owner, string reason)
    {
        try
        {
            TryRequestNewReceipts(owner, reason);
        }
        catch (Exception ex)
        {
            // Item processing calls this; a failure here must not stop the item itself.
            LogUtility.Error($"Could not record new AP receipts ({reason}) for the other players: {ex}");
        }
    }

    private static void TryRequestNewReceipts(Player owner, string reason)
    {
        if (!MultiplayerSupport.IsRealMultiplayerRun
            || !MultiplayerSupport.IsLocalOwnApSlot
            || owner.RunState is not RunState runState
            || !ApRunData.TryGetPlayerState(runState, owner.NetId, out ApPlayerRunState state)
            || state.Participation != ApParticipationKind.OwnApSlot)
        {
            return;
        }

        int recordedThrough = state.ReceiptLedger.Count > 0 ? state.ReceiptLedger[^1].Index : 0;
        int floor = Math.Max(recordedThrough, _requestedThrough);
        IReadOnlyList<ItemInfo> history = MultiplayerSupport.GetCurrentOwnSlotReceivedItems().ToArray();
        var receipts = new List<ApLedgerReceipt>();
        for (int i = floor; i < history.Count; i++)
        {
            long itemId = history[i].ItemId;
            if (!ArchipelagoIdCodec.IsArchipelagoReservedId(itemId))
                receipts.Add(new ApLedgerReceipt { Index = i + 1, ItemId = itemId });
        }
        int? anytime = state.RelicRewardsAvailableAnytime == null
            ? Math.Clamp(
                ArchipelagoClient.Progress.RelicRewardsAvailableAnytimeForRun,
                0,
                ArchipelagoProgress._maxRelicRewards
            )
            : null;
        List<long> campfires = ArchipelagoClient.CheckedLocations
            .Concat(ArchipelagoClient.Progress.PendingLocationChecks)
            .Where(LocationData.IsCampfireLocationId)
            .Where(id => !state.CheckedCampfireLocationIds.Contains(id) && !RequestedCampfires.Contains(id))
            .Distinct()
            .Order()
            .ToList();
        if (receipts.Count == 0 && anytime == null && campfires.Count == 0)
            return;

        int batches = Math.Max(1, (receipts.Count + MaxReceiptsPerAction - 1) / MaxReceiptsPerAction);
        for (int batch = 0; batch < batches; batch++)
        {
            var message = new ApReceiptsArrivedActionMessage
            {
                Receipts = receipts.Skip(batch * MaxReceiptsPerAction).Take(MaxReceiptsPerAction).ToList(),
                RelicRewardsAvailableAnytime = batch == 0 ? anytime : null,
                CheckedCampfireLocationIds = batch == 0 ? campfires : new List<long>(),
            };
            // Later batches must not overtake one that could not be sent.
            if (!Request(owner, message, reason))
                return;
            if (message.Receipts.Count > 0)
                _requestedThrough = Math.Max(_requestedThrough, message.Receipts[^1].Index);
            RequestedCampfires.UnionWith(message.CheckedCampfireLocationIds);
        }
    }

    private static bool Request(Player owner, ApReceiptsArrivedActionMessage message, string reason)
    {
        Guid claimId = Guid.NewGuid();
        if (!ApQueuedClaims.TryAddress(message, claimId, owner))
        {
            LogUtility.Error($"Could not record new AP receipts ({reason}): the run has no AP run id.");
            return false;
        }
        Guid runId = message.RunId;
        ManagedActionRequestScheduler.RequestOrDefer(
            claimId,
            $"AP receipts ({reason})",
            () => RitsuLibManagedNetActions.Request(
                RunManager.Instance, Kind.Descriptor, message, owner.NetId),
            () => MultiplayerSupport.IsRealMultiplayerRun
                && RunManager.Instance.DebugOnlyGetState() is RunState runState
                && ApRunData.TryGetSharedState(runState, out ApRunSharedState shared)
                && shared.RunId == runId,
            () => ApQueuedClaims.LogRequested(Kind, message, requested: true),
            failure =>
            {
                // The next live receipt or reconnect sends these again.
                if (message.Receipts.Count > 0)
                    _requestedThrough = Math.Min(_requestedThrough, message.Receipts[0].Index - 1);
                RequestedCampfires.ExceptWith(message.CheckedCampfireLocationIds);
                LogUtility.Error($"Could not record new AP receipts for the other players: {failure}");
            }
        );
        return true;
    }

    private static Task<ApQueuedClaimResult> Apply(ApQueuedClaim<ApReceiptsArrivedActionMessage> claim)
    {
        ApReceiptsArrivedActionMessage message = claim.Message;
        int appended = ApRunData.RecordLedgerReceipts(
            claim.RunState,
            claim.Player.NetId,
            message.Receipts,
            message.RelicRewardsAvailableAnytime
        );
        int campfires = ApRunData.RecordCampfireChecks(
            claim.RunState,
            claim.Player.NetId,
            message.CheckedCampfireLocationIds
        );
        List<ApLedgerReceipt> ledger = claim.OwnerState.ReceiptLedger;
        ApQueuedClaims.LogApplied(
            Kind,
            message,
            claim.Player,
            $"appended {appended}; ledger holds {ledger.Count} through index "
                + $"{(ledger.Count > 0 ? ledger[^1].Index : 0)}, anytime relics "
                + $"{claim.OwnerState.RelicRewardsAvailableAnytime?.ToString() ?? "unset"}, "
                + $"campfire checks +{campfires} = {claim.OwnerState.CheckedCampfireLocationIds.Count}"
        );
        return Task.FromResult(new ApQueuedClaimResult(appended + campfires > 0));
    }

    private static string Describe(ApReceiptsArrivedActionMessage message)
    {
        string receipts = message.Receipts.Count == 0
            ? "no receipts"
            : $"{message.Receipts.Count} receipt(s) {message.Receipts[0].Index}..{message.Receipts[^1].Index}";
        if (message.RelicRewardsAvailableAnytime is int anytime)
            receipts += $", anytime relics {anytime}";
        if (message.CheckedCampfireLocationIds.Count > 0)
            receipts += $", campfire checks [{string.Join(",", message.CheckedCampfireLocationIds)}]";
        return receipts;
    }

    // Readers. Each reads only the ledger, which the receipts action alone writes, so a reader at
    // an ordered point in the run (room entry, an action) answers alike on every peer.

    internal static bool TryGetState(Player player, out ApPlayerRunState state)
    {
        state = null!;
        return player.RunState is RunState runState
            && ApRunData.TryGetPlayerState(runState, player.NetId, out state)
            && state.Participation == ApParticipationKind.OwnApSlot;
    }

    /// <summary>The recorded receipt at this index, if any.</summary>
    internal static ApLedgerReceipt? Find(ApPlayerRunState state, int receivedItemIndex)
    {
        List<ApLedgerReceipt> ledger = state.ReceiptLedger;
        int low = 0, high = ledger.Count - 1;
        while (low <= high)
        {
            int middle = low + (high - low) / 2;
            int index = ledger[middle].Index;
            if (index == receivedItemIndex)
                return ledger[middle];
            if (index < receivedItemIndex)
                low = middle + 1;
            else
                high = middle - 1;
        }
        return null;
    }

    /// <summary>How many receipts of one character item type this character has.</summary>
    internal static int CountCharacterReceipts(ApPlayerRunState state, APItem type, long characterOffset) =>
        state.ReceiptLedger.Count(receipt => receipt.IsCharacterItem(type, characterOffset));

    /// <summary>The player's receipts of one character item type for their run's character.</summary>
    internal static IEnumerable<ApLedgerReceipt> CharacterReceipts(
        Player player,
        ApPlayerRunState state,
        APItem type)
    {
        if (player.GetAPCharacterNumber() is not long characterOffset)
            return Array.Empty<ApLedgerReceipt>();
        return state.ReceiptLedger.Where(receipt => receipt.IsCharacterItem(type, characterOffset));
    }

    /// <summary>
    /// Whether the recorded receipt at this index is a character item of this type for the
    /// player's character, or (<paramref name="universalType"/>) that universal item.
    /// </summary>
    internal static bool HasReceipt(
        Player player,
        ApPlayerRunState state,
        int receivedItemIndex,
        APItem type,
        bool universalType = false)
    {
        if (Find(state, receivedItemIndex) is not ApLedgerReceipt receipt)
            return false;
        if (universalType)
            return receipt.IsUniversalItem(type);
        return player.GetAPCharacterNumber() is long characterOffset
            && receipt.IsCharacterItem(type, characterOffset);
    }
}
