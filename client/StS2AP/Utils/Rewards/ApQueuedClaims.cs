using System.Text.Json;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;
using StS2AP.Models;
using StS2AP.Multiplayer;
using StS2AP.Persistence;

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
        bool waitsForMenuRow = true)
    {
        Noun = noun;
        Apply = apply;
        Describe = describe;
        WaitsForMenuRow = waitsForMenuRow;
        Descriptor = new ApNetActionDescriptor<T>(
            ActionKey: actionKey,
            Serialize: static message => JsonSerializer.SerializeToUtf8Bytes(message),
            Deserialize: DeserializeMessage,
            Execute: context => ApQueuedClaims.Execute(this, context),
            // NonCombat actions are held while a combat runs and block their owner's queue if one
            // reaches the front during combat. A claim that the host orders after a move into a
            // combat room must still execute there, on every peer, and not stall that player.
            ActionType: GameActionType.Any
        );
    }

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
    /// Whether each action belongs to a row of the replicated AP menu and meets it there (see
    /// <see cref="ApQueuedClaims.SelectRow{T}"/>). An action that prepares the menu has no row:
    /// only its owner waits for it (<see cref="ApQueuedClaims.RequestAndWait{T}"/>).
    /// </summary>
    internal bool WaitsForMenuRow { get; }

    internal ApNetActionDescriptor<T> Descriptor { get; }

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
/// Prototype, off by default (<c>ap queuedclaims on</c>): multiplayer AP claims (gold, potions,
/// bonus wax relics, relics, Ancient relics and cards) run as native queued actions, executed on
/// every peer in the host's action order, instead of applying when a replicated reward selection
/// arrives. Relics and Ancient choices are also assigned by one, before the menu opens.
/// </summary>
/// <remarks>
/// A replicated selection is handled whenever it arrives, so one peer can apply a claim before a
/// map move's room-exit checksum and another after it. A queued claim is ordered against the move
/// by construction. Executors validate against action-owned state on
/// <see cref="ApPlayerRunState"/>, so a repeated claim is refused identically everywhere without
/// asking the host first.
///
/// Until every reward kind is a queued claim the AP menu stays replicated: each queued row is
/// still a native reward in it, so the other rows keep their indexes and selections still
/// replicate. Only the effect moves into the action. The owner's row requests the action from
/// inside the player's AP selection order (after earlier AP rows finished), every peer's row waits
/// for this peer to execute it, and the executor waits for the row's turn on this peer before
/// applying. That rendezvous keeps a queued claim at its row's place among the replicated
/// selections, so a relic taken just before a potion sees the same belt everywhere. It is
/// scaffolding: once the menu is owner-local the action queue alone orders claims, and the
/// rendezvous goes.
/// </remarks>
public static class ApQueuedClaims
{
    // How long an executor waits for its row's turn before applying anyway. The claim must apply
    // on every peer; only the order relative to a stuck earlier row is at stake.
    private static readonly TimeSpan RowTimeout = TimeSpan.FromSeconds(10);

    // How long a row waits for its action to start executing on this peer. Only the start is timed:
    // a card claim then pauses for its owner's pick, which may legitimately take much longer.
    private static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(30);

    private readonly record struct AttemptKey(Guid ClaimId, int Attempt);

    /// <summary>One selection's meeting point between its menu row and its action, on this peer.</summary>
    private sealed class Meeting
    {
        public TaskCompletionSource RowReached { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<ApQueuedClaimResult> Executed { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private static readonly Dictionary<AttemptKey, Meeting> Meetings = new();

    // Selections of each queued row on this peer. Every peer replays the same row selections in
    // the same order, so the count matches the owner's attempt number for the same selection.
    private static readonly Dictionary<Guid, int> RowSelections = new();

    private static bool _initialized;

    /// <summary>
    /// Whether this process's new AP menus offer their rewards as queued claims. Only the claim owner's switch matters: the menu tells every peer
    /// how each row works.
    /// </summary>
    public static bool Enabled { get; set; }

    /// <summary>
    /// Whether a menu opened now uses queued claims. A menu reads it once when it starts opening,
    /// so all of its rows, and the relic assignments it waits for, go the same way.
    /// </summary>
    internal static bool IsActive => Enabled && MultiplayerSupport.IsRealMultiplayerRun;

    public static void Initialize()
    {
        if (_initialized)
            return;
        ApNetActions.Register(ApQueuedGoldClaims.Kind.Descriptor);
        ApNetActions.Register(ApQueuedReceiptClaims.PotionKind.Descriptor);
        ApNetActions.Register(ApQueuedReceiptClaims.BonusRelicKind.Descriptor);
        ApNetActions.Register(ApQueuedRelicClaims.AssignmentKind.Descriptor);
        ApNetActions.Register(ApQueuedRelicClaims.ClaimKind.Descriptor);
        ApNetActions.Register(ApQueuedRelicClaims.AncientClaimKind.Descriptor);
        ApNetActions.Register(ApQueuedCardClaims.ClaimKind.Descriptor);
        _initialized = true;
    }

    public static void EndRun()
    {
        foreach (Meeting meeting in Meetings.Values)
        {
            meeting.RowReached.TrySetResult();
            meeting.Started.TrySetResult();
            meeting.Executed.TrySetResult(ApQueuedClaimResult.NotApplied);
        }
        Meetings.Clear();
        RowSelections.Clear();
    }

    /// <summary>
    /// Runs one queued row's selection on any peer, at its place in that player's AP selection
    /// order. The owner requests the claim here, after its earlier AP rows finished, so every peer
    /// has received those rows' selections before the action.
    /// </summary>
    /// <param name="createMessage">The kind's payload; the shared header is filled in here.</param>
    internal static async Task<ApQueuedClaimResult> SelectRow<T>(
        ApQueuedClaimKind<T> kind,
        Guid claimId,
        Player player,
        Func<T> createMessage) where T : ApQueuedClaimMessage, new()
    {
        int attempt = RowSelections.GetValueOrDefault(claimId) + 1;
        RowSelections[claimId] = attempt;
        Meeting meeting = GetOrAdd(new AttemptKey(claimId, attempt));
        meeting.RowReached.TrySetResult();

        if (LocalContext.IsMe(player) && !TryRequest(kind, claimId, attempt, player, createMessage))
        {
            // The row's selection already reached the other peers, which now wait for an action
            // that will not come. Their timeout disables claims there; do the same here.
            MultiplayerSupport.InvalidateRunClaims($"queued {kind.Noun} claim {claimId} could not be requested");
            NotificationUtility.ShowRawText($"Could not claim that {kind.Noun}. Try again after reloading the run.");
            return ApQueuedClaimResult.NotApplied;
        }

        if (!await WaitForStart(meeting, kind, claimId, player))
        {
            MultiplayerSupport.InvalidateRunClaims($"queued {kind.Noun} claim {claimId} did not execute");
            return ApQueuedClaimResult.NotApplied;
        }
        return await meeting.Executed.Task;
    }

    private static async Task<bool> WaitForStart<T>(
        Meeting meeting,
        ApQueuedClaimKind<T> kind,
        Guid claimId,
        Player player) where T : ApQueuedClaimMessage, new()
    {
        try
        {
            await meeting.Started.Task.WaitAsync(StartTimeout);
            return true;
        }
        catch (TimeoutException)
        {
            LogUtility.Error(
                $"Queued {kind.Noun} {claimId} for player {player.NetId} did not start executing within "
                    + $"{StartTimeout.TotalSeconds:0} seconds."
            );
            return false;
        }
    }

    /// <summary>
    /// Owner only: requests a row-less action, such as the relic assignments a menu needs before it
    /// opens, and waits until this peer has executed it. A timeout disables nothing: the action
    /// still executes alike on every peer whenever it comes, and a retry finds its effect there.
    /// </summary>
    internal static async Task<ApQueuedClaimResult> RequestAndWait<T>(
        ApQueuedClaimKind<T> kind,
        Player owner,
        Func<T> createMessage) where T : ApQueuedClaimMessage, new()
    {
        var key = new AttemptKey(Guid.NewGuid(), 1);
        Meeting meeting = GetOrAdd(key);
        if (!TryRequest(kind, key.ClaimId, key.Attempt, owner, createMessage))
            return ApQueuedClaimResult.NotApplied;
        if (!await WaitForStart(meeting, kind, key.ClaimId, owner))
            return ApQueuedClaimResult.NotApplied;
        return await meeting.Executed.Task;
    }

    private static bool TryRequest<T>(
        ApQueuedClaimKind<T> kind,
        Guid claimId,
        int attempt,
        Player owner,
        Func<T> createMessage) where T : ApQueuedClaimMessage, new()
    {
        if (owner.RunState is not RunState runState
            || !ApRunData.TryGetSharedState(runState, out ApRunSharedState shared)
            || shared.RunId == Guid.Empty)
        {
            return false;
        }

        T message = createMessage();
        message.RunId = shared.RunId;
        message.ClaimId = claimId;
        message.Attempt = attempt;
        message.OwnerNetId = owner.NetId;
        message.ApSlotId = MultiplayerSupport.PreparedApSlotId ?? 0;
        bool requested = ApNetActions.Request(kind.Descriptor, message, owner.NetId);
        LogUtility.Info(
            $"{(requested ? "Requested" : "Could not request")} queued {kind.Noun} claim {claimId} "
                + $"attempt {attempt}: {kind.Describe(message)}"
        );
        return requested;
    }

    internal static async Task Execute<T>(ApQueuedClaimKind<T> kind, ApNetActionContext<T> context)
        where T : ApQueuedClaimMessage, new()
    {
        T message = context.Message;
        if (message.ClaimId == Guid.Empty || message.Attempt < 1)
        {
            LogUtility.Error(
                $"Refused a queued {kind.Noun} claim from player {context.Player.NetId} without a claim id or attempt."
            );
            return;
        }

        Meeting meeting = GetOrAdd(new AttemptKey(message.ClaimId, message.Attempt));
        meeting.Started.TrySetResult();
        ApQueuedClaimResult result = ApQueuedClaimResult.NotApplied;
        try
        {
            try
            {
                if (kind.WaitsForMenuRow)
                    await meeting.RowReached.Task.WaitAsync(RowTimeout);
            }
            catch (TimeoutException)
            {
                LogUtility.Warn(
                    $"Queued {kind.Noun} claim {message.ClaimId} attempt {message.Attempt} did not reach "
                        + $"its menu row on this peer within {RowTimeout.TotalSeconds:0} seconds; applying it anyway."
                );
            }

            // A menu row tells its owner why nothing happened; an owner waiting on a row-less
            // action reports the failure itself.
            if (!TryGetClaim(message, context.Player, context.PlayerChoiceContext, out ApQueuedClaim<T> claim, out string reason))
            {
                result = Refuse(kind, message, context.Player, reason,
                    kind.WaitsForMenuRow ? $"Could not claim that {kind.Noun}." : null);
            }
            else
                result = await kind.Apply(claim);
        }
        finally
        {
            meeting.Executed.TrySetResult(result);
        }
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
    /// Logs what a claim did on this peer, in the same form on every peer (claim id, attempt,
    /// player, the kind's payload, then the outcome), so two players' logs line up per claim.
    /// </summary>
    internal static void LogApplied<T>(ApQueuedClaimKind<T> kind, T message, Player player, string outcome)
        where T : ApQueuedClaimMessage, new() =>
        LogUtility.Info(
            $"Applied queued {kind.Noun} claim {message.ClaimId} attempt {message.Attempt} for player "
                + $"{player.NetId} ({kind.Describe(message)}): {outcome}"
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
            $"Refused queued {kind.Noun} claim {message.ClaimId} attempt {message.Attempt} for player "
                + $"{player.NetId} ({kind.Describe(message)}): {reason}."
        );
        if (ownerNotice != null && LocalContext.IsMe(player))
            NotificationUtility.ShowRawText(ownerNotice);
        return ApQueuedClaimResult.NotApplied;
    }

    private static Meeting GetOrAdd(AttemptKey key)
    {
        if (!Meetings.TryGetValue(key, out Meeting? meeting))
        {
            meeting = new Meeting();
            Meetings[key] = meeting;
        }
        return meeting;
    }
}
