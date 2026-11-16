using System.Text.Json;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;
using StS2AP.Models;
using StS2AP.Multiplayer;
using StS2AP.Persistence;
using STS2RitsuLib.Networking.ManagedActions;

namespace StS2AP.Utils;

/// <summary>What a queued claim did on this peer, for its menu row.</summary>
/// <param name="ClaimedModel">
/// The potion or relic the action obtained, so the owner's reward screen can animate it from the
/// row into the inventory as it does for a vanilla reward.
/// </param>
internal readonly record struct ApQueuedClaimResult(bool Applied, AbstractModel? ClaimedModel = null)
{
    internal static ApQueuedClaimResult NotApplied => new(false);
}

/// <summary>
/// A queued claim on its way through an executor: the owner's message, the replicated run state it
/// was validated against, and its action's choice context for a claim that asks its owner to choose.
/// </summary>
internal sealed record ApQueuedClaim<T>(
    T Message,
    Player Player,
    RunState RunState,
    ApRunSharedState Shared,
    ApPlayerRunState OwnerState,
    PlayerChoiceContext ChoiceContext
) where T : ApQueuedClaimMessage
{
    internal bool IsLocalOwner => LocalContext.IsMe(Player);
}

/// <summary>One kind of queued claim: its native action and how every peer applies it.</summary>
internal sealed class ApQueuedClaimKind<T> where T : ApQueuedClaimMessage, new()
{
    internal ApQueuedClaimKind(
        string actionKey,
        string noun,
        Func<ApQueuedClaim<T>, Task<ApQueuedClaimResult>> apply,
        Func<T, string> describe,
        bool notifiesOwnerOfRefusal = true,
        GameActionType actionType = GameActionType.Any)
    {
        Noun = noun;
        Apply = apply;
        Describe = describe;
        NotifiesOwnerOfRefusal = notifiesOwnerOfRefusal;
        Descriptor = new RitsuLibManagedNetActionDescriptor<T>(
            ModuleId: ModEntry.ModId,
            ActionKey: actionKey,
            Serialize: static message => JsonSerializer.SerializeToUtf8Bytes(message),
            Deserialize: DeserializeMessage,
            Execute: context => ApQueuedClaims.Execute(this, context),
            // Any by default: NonCombat actions are held while a combat runs and block their
            // owner's queue if one reaches the front during combat. A claim that the host orders
            // after a move into a combat room must still execute there, on every peer, and not
            // stall that player. A kind requested during combat is CombatPlayPhaseOnly instead
            // (ApReceiptLedger.CombatKind).
            ActionType: actionType
        );
    }

    /// <summary>
    /// Whether the game can drop this kind's action before it runs. An Any action always runs; a
    /// CombatPlayPhaseOnly one is cancelled on every peer when the turn or the combat ends first.
    /// </summary>
    internal bool CanBeCancelled => Descriptor.ActionType != GameActionType.Any;

    /// <summary>What the claim is called in logs and notices, such as "AP potion".</summary>
    internal string Noun { get; }

    /// <summary>
    /// Applies one validated claim on every peer. It may read only replicated state that queued
    /// actions change, never local switches such as disabled claims: the other peers apply the
    /// claim regardless.
    /// </summary>
    internal Func<ApQueuedClaim<T>, Task<ApQueuedClaimResult>> Apply { get; }

    /// <summary>The kind's own payload, for logs that compare two players' claims.</summary>
    internal Func<T, string> Describe { get; }

    /// <summary>
    /// Whether a refusal shows the owner a notice. A menu row's claim does; the action that
    /// prepares a menu doesn't, because the menu reports that it could not open.
    /// </summary>
    internal bool NotifiesOwnerOfRefusal { get; }

    internal RitsuLibManagedNetActionDescriptor<T> Descriptor { get; }

    /// <summary>The claim id in one of this kind's action payloads; empty if it doesn't read.</summary>
    internal Guid ReadClaimId(byte[] payload) => DeserializeMessage(payload).ClaimId;

    private T DeserializeMessage(ReadOnlySpan<byte> bytes)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(bytes) ?? new T();
        }
        catch (JsonException ex)
        {
            // An empty message has no claim id, so every peer refuses it.
            LogUtility.Warn($"Could not deserialize a queued {Noun} claim: {ex.Message}");
            return new T();
        }
    }
}

/// <summary>
/// Multiplayer AP claims run as native queued actions, executed on every peer in the host's action
/// order. Relics and Ancient choices are also assigned by one, before the owner's menu opens.
/// </summary>
/// <remarks>
/// The AP menu is the owner's alone: an unsynchronized reward set no other peer builds. A row's
/// selection requests its claim action and waits until this peer has executed it, then shows its
/// verdict. Every peer applies the claim when the action reaches its turn, so it lands on the same
/// side of a map move, and of the room-exit checksum taken inside that move, everywhere. Executors
/// validate against action-owned state on <see cref="ApPlayerRunState"/>, so a repeated claim is
/// refused identically everywhere without asking the host first.
/// </remarks>
public static class ApQueuedClaims
{
    // This peer's own requested claims, by claim id, until they execute here.
    private static readonly Dictionary<Guid, TaskCompletionSource<ApQueuedClaimResult>> Pending = new();

    // This peer's own requested actions that the game may cancel before they run, by claim id,
    // with what to do if it does.
    private static readonly Dictionary<Guid, Action<string>> CancellationHandlers = new();

    // How to read the claim id from the payload of each cancellable kind's action, by opcode.
    private static readonly Dictionary<ulong, Func<byte[], Guid>> CancellableKinds = new();

    // The run's action queues, watched for this peer's cancellable actions as they are enqueued.
    private static ActionQueueSet? _watchedQueues;

    private static bool _initialized;

    /// <summary>
    /// Whether this player has a queued AP claim requested that has not executed here yet. Until
    /// it has, its choices (a card pick, a relic that asks for a card) reserve choice ids on other
    /// peers at an unknown point, so native choices of this player must wait.
    /// </summary>
    internal static bool HasPendingOwnerClaims => Pending.Count > 0;

    public static void Initialize()
    {
        if (_initialized)
            return;
        Register(ApQueuedGoldClaims.Kind);
        Register(ApQueuedReceiptClaims.PotionKind);
        Register(ApQueuedReceiptClaims.BonusRelicKind);
        Register(ApQueuedRelicClaims.AssignmentKind);
        Register(ApQueuedRelicClaims.ClaimKind);
        Register(ApQueuedRelicClaims.AncientClaimKind);
        Register(ApQueuedCardClaims.ClaimKind);
        Register(ApReceiptLedger.Kind);
        Register(ApReceiptLedger.CombatKind);
        CombatManager.Instance.CombatEnded += _ => CancelActionsLeftAtCombatEnd();
        _initialized = true;
    }

    private static void Register<T>(ApQueuedClaimKind<T> kind) where T : ApQueuedClaimMessage, new()
    {
        ulong opcode = RitsuLibManagedNetActions.Register(kind.Descriptor);
        if (kind.CanBeCancelled)
            CancellableKinds[opcode] = kind.ReadClaimId;
    }

    public static void EndRun()
    {
        foreach (TaskCompletionSource<ApQueuedClaimResult> pending in Pending.Values)
            pending.TrySetResult(ApQueuedClaimResult.NotApplied);
        Pending.Clear();
        CancellationHandlers.Clear();
        if (_watchedQueues != null)
            _watchedQueues.ActionEnqueued -= OnActionEnqueued;
        _watchedQueues = null;
        ApReceiptLedger.EndRun();
    }

    /// <summary>
    /// Owner, just before requesting an action of a cancellable kind: runs <paramref name="onCancelled"/>
    /// once if the game cancels it before it runs here. A cancelled action runs on no peer: every
    /// peer cancels the same waiting actions at turn and combat end, in the host's order.
    /// </summary>
    internal static void OnCancelled(Guid claimId, Action<string> onCancelled)
    {
        WatchForCancellations();
        CancellationHandlers[claimId] = onCancelled;
    }

    /// <summary>Owner: the request was not made after all.</summary>
    internal static void StopWatching(Guid claimId) => CancellationHandlers.Remove(claimId);

    /// <summary>
    /// Owner only: requests one claim and waits until this peer has executed it. There is no
    /// timeout: a card claim pauses for its owner's pick, and an action that never comes means
    /// the run is ending, which completes the wait.
    /// </summary>
    /// <param name="createMessage">The kind's payload; the shared header is filled in here.</param>
    internal static async Task<ApQueuedClaimResult> RequestAndWait<T>(
        ApQueuedClaimKind<T> kind,
        Player owner,
        Func<T> createMessage) where T : ApQueuedClaimMessage, new()
    {
        Guid claimId = Guid.NewGuid();
        var pending = new TaskCompletionSource<ApQueuedClaimResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        Pending[claimId] = pending;
        if (!TryRequest(kind, claimId, owner, createMessage))
        {
            Pending.Remove(claimId);
            if (kind.NotifiesOwnerOfRefusal)
                NotificationUtility.ShowRawText($"Could not claim that {kind.Noun}.");
            return ApQueuedClaimResult.NotApplied;
        }
        return await pending.Task;
    }

    private static bool TryRequest<T>(
        ApQueuedClaimKind<T> kind,
        Guid claimId,
        Player owner,
        Func<T> createMessage) where T : ApQueuedClaimMessage, new()
    {
        T message = createMessage();
        if (!TryAddress(message, claimId, owner))
            return false;
        bool requested = RitsuLibManagedNetActions.Request(
            RunManager.Instance, kind.Descriptor, message, owner.NetId);
        LogRequested(kind, message, requested);
        return requested;
    }

    /// <summary>
    /// Fills in the header every claim shares: this run, a claim id, and the owner's player and
    /// AP slot, which every peer checks before applying the claim.
    /// </summary>
    internal static bool TryAddress<T>(T message, Guid claimId, Player owner) where T : ApQueuedClaimMessage
    {
        if (owner.RunState is not RunState runState
            || !ApRunData.TryGetSharedState(runState, out ApRunSharedState shared)
            || shared.RunId == Guid.Empty)
        {
            return false;
        }

        message.RunId = shared.RunId;
        message.ClaimId = claimId;
        message.OwnerNetId = owner.NetId;
        message.ApSlotId = MultiplayerSupport.PreparedApSlotId ?? 0;
        return true;
    }

    internal static void LogRequested<T>(ApQueuedClaimKind<T> kind, T message, bool requested)
        where T : ApQueuedClaimMessage, new() =>
        LogUtility.Info(
            $"{(requested ? "Requested" : "Could not request")} queued {kind.Noun} claim {message.ClaimId}: "
                + kind.Describe(message)
        );

    internal static async Task Execute<T>(
        ApQueuedClaimKind<T> kind,
        RitsuLibManagedNetActionContext<T> context)
        where T : ApQueuedClaimMessage, new()
    {
        T message = context.Message;
        ApQueuedClaimResult result = ApQueuedClaimResult.NotApplied;
        try
        {
            if (message.ClaimId == Guid.Empty)
            {
                LogUtility.Error($"Refused a queued {kind.Noun} claim from player {context.Player.NetId} without a claim id.");
                return;
            }

            if (!TryGetClaim(message, context.Player, context.PlayerChoiceContext, out ApQueuedClaim<T> claim, out string reason))
            {
                result = Refuse(kind, message, context.Player, reason,
                    kind.NotifiesOwnerOfRefusal ? $"Could not claim that {kind.Noun}." : null);
            }
            else
            {
                result = await kind.Apply(claim);
            }
        }
        finally
        {
            // Only the owner waits for its claims, or watches for their cancellation; other peers
            // have no entry.
            CancellationHandlers.Remove(message.ClaimId);
            if (Pending.Remove(message.ClaimId, out TaskCompletionSource<ApQueuedClaimResult>? pending))
                pending.TrySetResult(result);
        }
    }

    private static void WatchForCancellations()
    {
        ActionQueueSet queues = RunManager.Instance.ActionQueueSet;
        if (ReferenceEquals(queues, _watchedQueues))
            return;
        if (_watchedQueues != null)
            _watchedQueues.ActionEnqueued -= OnActionEnqueued;
        queues.ActionEnqueued += OnActionEnqueued;
        _watchedQueues = queues;
    }

    /// <summary>
    /// The owner's own cancellable action reaching its queue: the host's request, or the host's
    /// announcement on a client. The game raises this before it may cancel an action that arrives
    /// too late, so the cancellation is seen either way.
    /// </summary>
    private static void OnActionEnqueued(GameAction action)
    {
        if (action is not RitsuLibManagedGameAction managed
            || !CancellableKinds.TryGetValue(managed.DescriptorOpcode, out Func<byte[], Guid>? readClaimId)
            || !LocalContext.IsMe(managed.Player))
        {
            return;
        }
        Guid claimId = readClaimId(managed.Payload);
        if (CancellationHandlers.ContainsKey(claimId))
            managed.BeforeCancelled += _ => Cancelled(claimId, "the game cancelled its action as the turn or the combat ended");
    }

    /// <summary>
    /// At this peer's combat end the game has cancelled every in-combat action not run here yet,
    /// including requests it still held for the players' turn, which no peer ever enqueued. Every
    /// peer runs actions in the same order, so an action that has not run here by now runs nowhere.
    /// </summary>
    private static void CancelActionsLeftAtCombatEnd()
    {
        foreach (Guid claimId in CancellationHandlers.Keys.ToList())
            Cancelled(claimId, "combat ended before its action ran");
    }

    private static void Cancelled(Guid claimId, string reason)
    {
        if (CancellationHandlers.Remove(claimId, out Action<string>? onCancelled))
            onCancelled(reason);
    }

    /// <summary>
    /// The checks every claim kind shares. Everything here is replicated run state that local
    /// conditions cannot change, so every peer reaches the same verdict.
    /// </summary>
    private static bool TryGetClaim<T>(
        T message,
        Player player,
        PlayerChoiceContext choiceContext,
        out ApQueuedClaim<T> claim,
        out string reason) where T : ApQueuedClaimMessage
    {
        claim = null!;
        reason = string.Empty;

        if (message.OwnerNetId != player.NetId)
            reason = $"it names owner {message.OwnerNetId} but was sent by {player.NetId}";
        else if (RunManager.Instance.DebugOnlyGetState() is not RunState runState
                 || !ReferenceEquals(player.RunState, runState))
            reason = "its player is not in the active run";
        else if (!ApRunData.TryGetSharedState(runState, out ApRunSharedState shared) || shared.RunId != message.RunId)
            reason = "it belongs to another run";
        else if (!ApRunData.TryGetPlayerState(runState, player.NetId, out ApPlayerRunState ownerState)
                 || ownerState.Participation == ApParticipationKind.VanillaGuest)
            reason = "its player has no AP slot in this run";
        else if (ownerState.ApSlotId != message.ApSlotId)
            reason = $"it names AP slot {message.ApSlotId}, not the player's slot {ownerState.ApSlotId}";
        else
        {
            claim = new ApQueuedClaim<T>(message, player, runState, shared, ownerState, choiceContext);
            return true;
        }
        return false;
    }

    /// <summary>
    /// Whether a queued claim already used up this receipt. Receipt claim kinds check it before
    /// applying anything and call <see cref="MarkReceiptClaimed"/> once they use the receipt up.
    /// </summary>
    internal static bool IsReceiptClaimed<T>(ApQueuedClaim<T> claim, int receivedItemIndex)
        where T : ApQueuedClaimMessage =>
        claim.OwnerState.QueuedClaimedReceiptIndexes.BinarySearch(receivedItemIndex) >= 0;

    internal static void MarkReceiptClaimed<T>(ApQueuedClaim<T> claim, int receivedItemIndex)
        where T : ApQueuedClaimMessage =>
        ApRunData.RecordQueuedReceiptClaim(claim.RunState, claim.Player.NetId, receivedItemIndex);

    /// <summary>
    /// Logs what a claim did on this peer, in the same form on every peer (claim id, player, the
    /// kind's payload, then the outcome), so two players' logs line up per claim.
    /// </summary>
    internal static void LogApplied<T>(ApQueuedClaimKind<T> kind, T message, Player player, string outcome)
        where T : ApQueuedClaimMessage, new() =>
        LogUtility.Info(
            $"Applied queued {kind.Noun} claim {message.ClaimId} for player {player.NetId} "
                + $"({kind.Describe(message)}): {outcome}"
        );

    /// <summary>A refused claim is a no-op on every peer, plus a notice for its owner.</summary>
    internal static ApQueuedClaimResult Refuse<T>(
        ApQueuedClaimKind<T> kind,
        T message,
        Player player,
        string reason,
        string? ownerNotice) where T : ApQueuedClaimMessage, new()
    {
        LogUtility.Warn(
            $"Refused queued {kind.Noun} claim {message.ClaimId} for player {player.NetId} "
                + $"({kind.Describe(message)}): {reason}."
        );
        if (ownerNotice != null && LocalContext.IsMe(player))
            NotificationUtility.ShowRawText(ownerNotice);
        return ApQueuedClaimResult.NotApplied;
    }
}
