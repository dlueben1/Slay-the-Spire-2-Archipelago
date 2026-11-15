using System.Text.Json;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Ascension;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Runs;
using StS2AP.Models;
using StS2AP.Multiplayer;
using StS2AP.Persistence;
using StS2AP.Utils.RunGraph;

namespace StS2AP.Utils;

/// <summary>
/// Prototype, off by default (<c>ap queuedgold on</c>): a multiplayer AP gold claim runs as one
/// native queued action on every peer instead of as a replicated reward selection.
/// </summary>
/// <remarks>
/// The menu row stays where it is: the owner's gold row is still a native reward in the
/// replicated AP menu, so the other rows keep their indexes. Selecting it changes no wallet. The
/// owner's row requests this action, and every peer's row waits for this peer to execute it.
/// The action applies the gold, so it lands on the same side of a map move, and of the room-exit
/// checksum taken inside that move, on every peer. Its claim is validated against a cursor only
/// this action advances, so a repeated claim is refused identically everywhere without asking
/// the host first.
///
/// While other AP rewards are still replicated selections, the action also waits for the gold
/// row's turn in the player's AP selection order on each peer, so a relic taken just before the
/// gold sees the same gold everywhere. Once every claim is an action, that wait goes away: the
/// action queue alone orders them.
/// </remarks>
public static class ApQueuedGoldClaims
{
    private const string ActionKey = "gold_claim";

    // How long the action waits for its row's turn before applying the gold anyway. The wallet
    // must change on every peer; only the order relative to a stuck earlier row is at stake.
    private static readonly TimeSpan RowTimeout = TimeSpan.FromSeconds(10);

    // How long a gold row waits for this peer to execute its action.
    private static readonly TimeSpan ExecutionTimeout = TimeSpan.FromSeconds(30);

    private static readonly ApNetActionDescriptor<ApGoldClaimActionMessage> ActionDescriptor = new(
        ActionKey: ActionKey,
        Serialize: static message => JsonSerializer.SerializeToUtf8Bytes(message),
        Deserialize: DeserializeMessage,
        Execute: ExecuteAction,
        // NonCombat actions are held while a combat runs and block their owner's queue if one
        // reaches the front during combat. A claim that the host orders after a move into a
        // combat room must still execute there, on every peer, and not stall that player.
        ActionType: GameActionType.Any
    );

    /// <summary>One claim's meeting point between its menu row and its action, on this peer.</summary>
    private sealed class ClaimState
    {
        public TaskCompletionSource RowReached { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<bool> Executed { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private static readonly Dictionary<Guid, ClaimState> Claims = new();
    private static bool _initialized;

    /// <summary>
    /// Whether this process's new AP menus offer gold as a queued claim. Only the claim owner's
    /// switch matters: the menu tells every peer which way its gold row works.
    /// </summary>
    public static bool Enabled { get; set; }

    internal static bool ShouldQueueNewClaims => Enabled && MultiplayerSupport.IsRealMultiplayerRun;

    public static void Initialize()
    {
        if (_initialized)
            return;
        ApNetActions.Register(ActionDescriptor);
        _initialized = true;
    }

    public static void EndRun()
    {
        foreach (ClaimState claim in Claims.Values)
        {
            claim.RowReached.TrySetResult();
            claim.Executed.TrySetResult(false);
        }
        Claims.Clear();
    }

    /// <summary>
    /// Runs one queued gold row's selection on any peer, at its place in that player's AP
    /// selection order. The owner requests the claim here, after its earlier AP rows finished, so
    /// every peer has received those rows' selections before the action.
    /// </summary>
    internal static async Task<bool> SelectRow(Guid claimId, ApGoldClaim claim, Player player)
    {
        ClaimState state = GetOrAdd(claimId);
        state.RowReached.TrySetResult();

        if (LocalContext.IsMe(player) && !TryRequest(claimId, claim, player))
        {
            // The row's selection already reached the other peers, which now wait for an action
            // that will not come. Their timeout disables claims there; do the same here.
            MultiplayerSupport.InvalidateRunClaims($"queued AP gold claim {claimId} could not be requested");
            NotificationUtility.ShowRawText("Could not claim AP gold. Try again after reloading the run.");
            return false;
        }

        try
        {
            return await state.Executed.Task.WaitAsync(ExecutionTimeout);
        }
        catch (TimeoutException)
        {
            LogUtility.Error(
                $"Queued AP gold claim {claimId} for player {player.NetId} did not execute within "
                    + $"{ExecutionTimeout.TotalSeconds:0} seconds."
            );
            MultiplayerSupport.InvalidateRunClaims($"queued AP gold claim {claimId} did not execute");
            return false;
        }
    }

    private static bool TryRequest(Guid claimId, ApGoldClaim claim, Player owner)
    {
        if (owner.RunState is not RunState runState
            || !ApRunData.TryGetSharedState(runState, out ApRunSharedState shared)
            || shared.RunId == Guid.Empty)
        {
            return false;
        }

        var message = new ApGoldClaimActionMessage
        {
            RunId = shared.RunId,
            ClaimId = claimId,
            OwnerNetId = owner.NetId,
            ApSlotId = MultiplayerSupport.PreparedApSlotId ?? 0,
            CursorBefore = claim.RedeemedRawAfter - claim.SourceAmount,
            SourceAmount = claim.SourceAmount,
        };
        bool requested = ApNetActions.Request(ActionDescriptor, message, owner.NetId);
        LogUtility.Info(
            $"{(requested ? "Requested" : "Could not request")} queued AP gold claim {claimId}: "
                + $"cursor {message.CursorBefore} + {message.SourceAmount} raw gold"
        );
        return requested;
    }

    private static async Task ExecuteAction(ApNetActionContext<ApGoldClaimActionMessage> context)
    {
        ApGoldClaimActionMessage message = context.Message;
        if (message.ClaimId == Guid.Empty)
        {
            LogUtility.Error($"Refused a queued AP gold claim from player {context.Player.NetId} without a claim id.");
            return;
        }

        ClaimState state = GetOrAdd(message.ClaimId);
        bool applied = false;
        try
        {
            try
            {
                await state.RowReached.Task.WaitAsync(RowTimeout);
            }
            catch (TimeoutException)
            {
                LogUtility.Warn(
                    $"Queued AP gold claim {message.ClaimId} did not reach its menu row on this peer "
                        + $"within {RowTimeout.TotalSeconds:0} seconds; applying it anyway."
                );
            }
            applied = await Apply(message, context.Player);
        }
        finally
        {
            state.Executed.TrySetResult(applied);
        }
    }

    /// <summary>
    /// Validates and applies one claim. Everything that decides the outcome is replicated run
    /// state that only queued actions change, so every peer reaches the same result. Local
    /// conditions such as this peer's disabled claims must not refuse it: the others apply it.
    /// </summary>
    private static async Task<bool> Apply(ApGoldClaimActionMessage message, Player player)
    {
        if (!TryGetClaimContext(
                message,
                player,
                out RunState runState,
                out ApRunSharedState shared,
                out ApPlayerRunState ownerState,
                out string invalidReason))
        {
            Refuse(message, player, invalidReason);
            return false;
        }

        bool poverty = shared.AscensionStateInitialized
            && shared.CurrentAscensions.Contains((int)AscensionLevel.Poverty);
        QueuedGoldClaimRule.Decision decision = QueuedGoldClaimRule.Evaluate(
            ownerState.QueuedGoldClaimCursor,
            message.CursorBefore,
            message.SourceAmount,
            poverty
        );
        if (!decision.Accepted)
        {
            Refuse(message, player, decision.RefusalReason ?? "the claim was refused");
            return false;
        }

        // Advance the cursor before awaiting the wallet change, so nothing can apply it twice.
        ApRunData.RecordQueuedGoldClaim(runState, player.NetId, decision.CursorAfter);
        if (decision.GrantedAmount > 0)
            await PlayerCmd.GainGold(decision.GrantedAmount, player);
        LogUtility.Info(
            $"Applied queued AP gold claim {message.ClaimId} for player {player.NetId}: "
                + $"{decision.GrantedAmount} gold, raw cursor {message.CursorBefore} -> "
                + $"{decision.CursorAfter}, poverty={poverty}"
        );

        if (LocalContext.IsMe(player))
            CommitOwnerBookkeeping(message, decision, player);
        return true;
    }

    private static bool TryGetClaimContext(
        ApGoldClaimActionMessage message,
        Player player,
        out RunState runState,
        out ApRunSharedState shared,
        out ApPlayerRunState ownerState,
        out string reason)
    {
        runState = null!;
        shared = null!;
        ownerState = null!;
        reason = string.Empty;

        if (message.OwnerNetId != player.NetId)
            reason = $"it names owner {message.OwnerNetId} but was sent by {player.NetId}";
        else if (RunManager.Instance.DebugOnlyGetState() is not RunState current
                 || !ReferenceEquals(player.RunState, current))
            reason = "its player is not in the active run";
        else if (!ApRunData.TryGetSharedState(current, out shared) || shared.RunId != message.RunId)
            reason = "it belongs to another run";
        else if (!ApRunData.TryGetPlayerState(current, player.NetId, out ownerState)
                 || ownerState.Participation == ApParticipationKind.VanillaGuest)
            reason = "its player has no AP slot in this run";
        else if (ownerState.ApSlotId != message.ApSlotId)
            reason = $"it names AP slot {message.ApSlotId}, not the player's slot {ownerState.ApSlotId}";
        else
        {
            runState = current;
            return true;
        }
        return false;
    }

    private static void Refuse(ApGoldClaimActionMessage message, Player player, string reason)
    {
        LogUtility.Warn($"Refused queued AP gold claim {message.ClaimId} for player {player.NetId}: {reason}.");
        if (LocalContext.IsMe(player))
            NotificationUtility.ShowRawText("That AP gold was already claimed.");
    }

    /// <summary>
    /// Owner-only: advances the AP source cursor that later menus offer gold from. Every peer
    /// has already applied the same wallet change, so a mismatch here stops further claims
    /// rather than undoing anything.
    /// </summary>
    private static void CommitOwnerBookkeeping(
        ApGoldClaimActionMessage message,
        QueuedGoldClaimRule.Decision decision,
        Player player)
    {
        ArchipelagoProgress progress = ArchipelagoClient.Progress;
        if (progress.GoldRedeemed != message.CursorBefore)
        {
            LogUtility.Error(
                $"Queued AP gold claim {message.ClaimId} started at raw cursor {message.CursorBefore}, "
                    + $"but this client's cursor is {progress.GoldRedeemed}."
            );
            MultiplayerSupport.InvalidateRunClaims("a queued AP gold claim did not match the local gold cursor");
        }
        progress.GoldRedeemed = Math.Max(progress.GoldRedeemed, decision.CursorAfter);
        ApRunGraphRecorder.RecordGoldClaim(decision.GrantedAmount);
        if (!ApRunData.PublishLocalProgress(player))
        {
            MultiplayerSupport.InvalidateRunClaims(
                "the AP gold cursor could not be published after a queued gold claim"
            );
        }
    }

    private static ClaimState GetOrAdd(Guid claimId)
    {
        if (!Claims.TryGetValue(claimId, out ClaimState? state))
        {
            state = new ClaimState();
            Claims[claimId] = state;
        }
        return state;
    }

    private static ApGoldClaimActionMessage DeserializeMessage(ReadOnlySpan<byte> bytes)
    {
        try
        {
            return JsonSerializer.Deserialize<ApGoldClaimActionMessage>(bytes) ?? new();
        }
        catch (JsonException ex)
        {
            LogUtility.Warn($"Could not deserialize a queued AP gold claim: {ex.Message}");
            return new ApGoldClaimActionMessage();
        }
    }
}
