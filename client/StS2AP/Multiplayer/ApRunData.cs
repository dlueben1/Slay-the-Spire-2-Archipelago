using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Game.Lobby;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Runs;
using StS2AP.Utils;
using StS2AP.Domain;
using StS2AP.DomainAdapters;
using STS2RitsuLib;
using STS2RitsuLib.Networking.Sidecar;
using STS2RitsuLib.RunData;

namespace StS2AP.Multiplayer;

/// <summary>
/// Owns the AP data embedded in MegaCrit's canonical run snapshot. Lobby methods stage the
/// launch contract; mid-run progress changes are published immediately after local AP mutations,
/// confirmed and relayed by the host, and durable only when the fixed host writes the next native
/// checkpoint. There are no
/// Frozen/Validated booleans: launch readiness must be derived from the current contributions,
/// and the committed run snapshot is the lifecycle boundary that makes the mapping immutable.
/// </summary>
public static class ApRunData
{
    internal const int RunSchemaVersion = 9;
    private const string ProgressSnapshotMessageKey = "player_ap_progress_snapshot_v1";
    private const string ProgressDeltaMessageKey = "player_ap_progress_delta_v1";
    private static RunSavedData<ApRunSharedState> _sharedRun = null!;
    private static PlayerRunSavedData<ApPlayerRunState> _players = null!;
    private static readonly RitsuLibSidecarJsonSerializer<ApProgressSnapshotMessage>
        ProgressSnapshotSerializer = new();
    private static readonly RitsuLibSidecarMessageDescriptor<ApProgressSnapshotMessage>
        ProgressSnapshotDescriptor = new(
            ModEntry.ModId,
            ProgressSnapshotMessageKey,
            ProgressSnapshotSerializer.Serialize,
            ProgressSnapshotSerializer.Deserialize,
            Required: true
        );
    private static readonly RitsuLibSidecarJsonSerializer<ApProgressDeltaMessage>
        ProgressDeltaSerializer = new();
    private static readonly RitsuLibSidecarMessageDescriptor<ApProgressDeltaMessage>
        ProgressDeltaDescriptor = new(
            ModEntry.ModId,
            ProgressDeltaMessageKey,
            ProgressDeltaSerializer.Serialize,
            ProgressDeltaSerializer.Deserialize,
            Required: true
        );
    private static IDisposable? _progressSnapshotSubscription;
    private static IDisposable? _progressDeltaSubscription;
    private static bool _initialized;
    private static long _localProgressRevision;
    private static ApRunProgressState? _lastPublishedLocalProgress;

    public static void Initialize()
    {
        if (_initialized)
            return;

        var runStore = RitsuLibFramework.GetRunSavedDataStore(ModEntry.ModId);
        _sharedRun = runStore.Register(
            key: "ap_run",
            defaultFactory: () => new ApRunSharedState(),
            options: new RunSavedDataOptions
            {
                SchemaVersion = RunSchemaVersion,
                SyncLobbyOnChange = true,
            }
        );
        _players = runStore.RegisterPerPlayer(
            key: "ap_players",
            defaultFactory: () => new ApPlayerRunState(),
            options: new RunSavedDataOptions
            {
                SchemaVersion = RunSchemaVersion,
                SyncLobbyOnChange = true,
            }
        );

        _initialized = true;
        _progressSnapshotSubscription = RitsuLibSidecarTypedMessageRegistry.Subscribe(
            ProgressSnapshotDescriptor,
            OnProgressSnapshotReceived
        );
        _progressDeltaSubscription = RitsuLibSidecarTypedMessageRegistry.Subscribe(
            ProgressDeltaDescriptor,
            OnProgressDeltaReceived
        );
        RitsuLibFramework.SubscribeLifecycle<RunSavedDataPreparingEvent>(OnRunDataPreparing);
        RitsuLibFramework.SubscribeLifecycle<RunSavedDataLobbyStagingEvent>(OnLobbyStagingChanged);
    }

    /// <summary>Stages this process's guest/AP identity under its native MegaCrit Net ID.</summary>
    public static void StageLocalPlayer(StartRunLobby lobby)
    {
        if (!_initialized)
            return;

        ulong localNetId = lobby.NetService.NetId;
        ApParticipationKind participation = MultiplayerSupport.PendingParticipation;
        _players.Lobby.TryGet(lobby, localNetId, out ApPlayerRunState? existing);
        ApSlotIdentity? preparedSlot = MultiplayerSupport.PreparedSlotIdentity;
        ArchipelagoProgress progress = ArchipelagoClient.Progress;
        var state = new ApPlayerRunState
        {
            Participation = participation,
            ApRoomSeed = participation == ApParticipationKind.OwnApSlot
                ? preparedSlot?.RoomSeed
                : null,
            ApTeamId = participation == ApParticipationKind.OwnApSlot
                ? preparedSlot?.ApTeamId
                : null,
            ApSlotId = participation == ApParticipationKind.OwnApSlot
                ? preparedSlot?.ApSlotId
                : null,
            SlotSettings = participation == ApParticipationKind.OwnApSlot
                ? MultiplayerSupport.CreateEffectiveHostSettingsSnapshot()
                : null,
            InitialRelicReceiptIndexesByCharacter = participation ==
                    ApParticipationKind.VanillaGuest
                ? new Dictionary<long, List<int>>()
                : progress.GetRelicReceiptIndexSnapshot(),
            InitialProgressiveAncientsByCharacter = participation ==
                    ApParticipationKind.VanillaGuest
                ? new Dictionary<long, int>()
                : new Dictionary<long, int>(progress.ProgressiveAncients),
            ReceiptSourceReady = participation switch
            {
                ApParticipationKind.OwnApSlot => MultiplayerSupport.InitialItemsLoaded,
                _ => true,
            },
            Progress = existing?.Progress ?? new ApRunProgressState(),
            Construction = existing?.Construction ?? new ApReplicaConstructionState(),
            ProgressRevision = existing?.ProgressRevision ?? 0,
            ProgressiveStarters = existing?.ProgressiveStarters
                ?? new ApProgressiveStarterPlayerState(),
        };
        // SyncLobbyOnChange makes this a contribution to the authoritative host staging
        // session. On a client RitsuLib pushes the local PlayerRunSavedData payload with a
        // vanilla character-change message and flushes it again with SetReady(true); the host
        // merges it under sender Net ID before handling that Ready message. On the host the
        // same call merges locally. This is not a host-to-client or mid-run synchronization
        // mechanism.
        if (existing == null || !HasSameLobbyContribution(existing, state))
        {
            _players.Lobby.Set(lobby, localNetId, state);
        }

        // This stages transport and storage only. The host Ready UI and final launch guard both
        // call TryValidateHostLobbyContributions against the latest active player list.
        if (lobby.NetService.Type == NetGameType.Host)
        {
            EnsureLobbyRunId(lobby);
            StageHostSettings(lobby, participation);
        }
    }

    public static bool TryGetLocalPlayerState(
        RunState runState,
        ulong localNetId,
        out ApPlayerRunState state)
    {
        if (_initialized)
            return _players.TryGet(runState, localNetId, out state);

        state = null!;
        return false;
    }

    public static ApRunSharedState GetSharedState(RunState runState) => _sharedRun.Get(runState);

    public static void ModifyRelicReceipts(RunState runState, Action<ApRelicReceiptState> update) =>
        _sharedRun.Modify(runState, state => update(state.RelicReceipts));

    public static bool TryGetSharedState(RunState runState, out ApRunSharedState state)
    {
        if (_initialized)
            return _sharedRun.TryGet(runState, out state);
        state = null!;
        return false;
    }

    public static bool TryGetPlayerState(
        RunState runState,
        ulong netId,
        out ApPlayerRunState state)
    {
        if (_initialized)
            return _players.TryGet(runState, netId, out state);
        state = null!;
        return false;
    }

    /// <summary>
    /// Commits a managed action's per-player starter recipe to canonical run data. Managed
    /// actions execute on every replica, so this mutation must be made identically everywhere.
    /// </summary>
    public static bool SetProgressiveStarterState(
        RunState runState,
        ulong netId,
        ApProgressiveStarterPlayerState progressiveStarters)
    {
        if (!_initialized || !_players.TryGet(runState, netId, out ApPlayerRunState state))
            return false;

        state.ProgressiveStarters = progressiveStarters;
        _players.Set(runState, netId, state);
        return true;
    }

    /// <summary>
    /// Atomically records one managed Ascension Down receipt and removes its level from the
    /// canonical host-authored set. Managed actions call this identically on every replica.
    /// </summary>
    public static bool TryApplyAscensionDown(
        RunState runState,
        int receivedItemIndex,
        MegaCrit.Sts2.Core.Entities.Ascension.AscensionLevel level,
        out bool alreadyHandled,
        out bool removed)
    {
        alreadyHandled = false;
        removed = false;
        if (!_initialized
            || !_sharedRun.TryGet(runState, out ApRunSharedState shared)
            || !shared.AscensionStateInitialized)
        {
            return false;
        }

        alreadyHandled = shared.HandledAscensionDownReceiptIndexes.Contains(receivedItemIndex);
        if (alreadyHandled)
            return true;

        removed = shared.CurrentAscensions.Contains((int)level);
        _sharedRun.Modify(runState, state =>
        {
            state.CurrentAscensions = state.CurrentAscensions
                .Where(value => value != (int)level)
                .Distinct()
                .Order()
                .ToList();
            state.HandledAscensionDownReceiptIndexes = state
                .HandledAscensionDownReceiptIndexes
                .Append(receivedItemIndex)
                .Distinct()
                .Order()
                .ToList();
        });
        return true;
    }

    public static bool TryGetLobbySharedState(
        StartRunLobby lobby,
        out ApRunSharedState state)
    {
        if (_initialized)
            return _sharedRun.Lobby.TryGet(lobby, out state);
        state = null!;
        return false;
    }

    public static bool TryGetLobbyPlayerState(
        StartRunLobby lobby,
        ulong netId,
        out ApPlayerRunState state)
    {
        if (_initialized)
            return _players.Lobby.TryGet(lobby, netId, out state);
        state = null!;
        return false;
    }

    /// <summary>
    /// Recomputes launch-contribution readiness from the host's current lobby staging. This
    /// deliberately returns no persistent validation token: callers must evaluate the latest
    /// player list and contributions at the actual host launch boundary.
    /// </summary>
    public static bool TryValidateHostLobbyContributions(
        StartRunLobby lobby,
        out string reason)
    {
        if (lobby.NetService.Type != NetGameType.Host)
        {
            reason = "Only the host has the authoritative merged lobby contributions.";
            return false;
        }

        ulong fixedHostNetId = lobby.NetService.NetId;
        ParticipantIdentity? hostIdentity = null;
        foreach (ulong netId in BetaMainCompatibility.GetLobbyPlayerNetIds(lobby))
        {
            bool contributed = TryGetLobbyPlayerState(lobby, netId, out ApPlayerRunState state);
            ContributionReadiness readiness = ParticipantAdapter.Evaluate(contributed ? state : null);
            string? blocker = ParticipantAdapter.Blocker(readiness);
            if (blocker != null)
            {
                reason = contributed
                    ? $"Player {netId}: {blocker}."
                    : $"Player {netId} has not contributed AP lobby state.";
                return false;
            }
            if (netId == fixedHostNetId)
                hostIdentity = readiness.Match<ParticipantIdentity?>(ready => ready, _ => null, _ => null);
        }

        if (hostIdentity?.Kind != ParticipantKind.OwnApSlot)
        {
            reason = "The fixed STS host must have a prepared AP slot.";
            return false;
        }

        if (!TryGetLobbySharedState(lobby, out ApRunSharedState hostShared)
            || hostShared.SchemaVersion != RunSchemaVersion
            || hostShared.HostSettings == null
            || !hostShared.AscensionStateInitialized
            || !hostShared.HostCharacterOffset.HasValue)
        {
            reason = "The host's AP settings and ascensions have not been frozen into lobby run data.";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    /// <summary>
    /// Replaces the process-local AP view with the fixed host's checkpoint for this player. The
    /// restored snapshot is also the baseline for later deltas; when no initialized progress
    /// exists, the caller initializes a fresh view and its first publication is a full snapshot.
    /// </summary>
    public static bool RestoreLocalProgress(Player player)
    {
        _localProgressRevision = 0;
        _lastPublishedLocalProgress = null;
        if (!_initialized)
            return false;
        if (player.RunState is not RunState runState
            || !_players.TryGet(runState, player.NetId, out ApPlayerRunState state))
            return false;

        _localProgressRevision = state.ProgressRevision;
        if (!state.Progress.Initialized)
            return false;

        EnsureConstructionInitialized(state, state.Progress);
        RelicReceiptMultiplayer.ReconcileProgress(runState, player, state.Progress);
        ArchipelagoClient.Progress = ArchipelagoProgress.FromRunProgressState(state.Progress, player);
        _lastPublishedLocalProgress = ArchipelagoClient.Progress.ToRunProgressState();
        return true;
    }

    /// <summary>
    /// Publishes the local owner's complete progress once, then only the fields changed since the
    /// preceding revision. The host relays accepted revisions so every replica can deterministically
    /// construct owner-specific native state; concrete gameplay effects still travel through
    /// MegaCrit's synchronizers.
    /// </summary>
    public static bool PublishLocalProgress(Player player)
    {
        if (!_initialized || !MultiplayerSupport.IsRealMultiplayerRun)
            return true;
        if (player.RunState is not RunState runState
            || !_players.TryGet(runState, player.NetId, out ApPlayerRunState state)
            || state.Participation == ApParticipationKind.VanillaGuest)
        {
            return false;
        }

        ApRunProgressState snapshot = ArchipelagoClient.Progress.ToRunProgressState();
        EnsureConstructionInitialized(state, snapshot);
        ApProgressDelta? delta = _lastPublishedLocalProgress == null
            ? null
            : ApProgressDelta.Between(_lastPublishedLocalProgress, snapshot);
        if (delta is { HasChanges: false })
            return true;

        if (_lastPublishedLocalProgress != null
            && state.ProgressRevision != _localProgressRevision)
        {
            LogUtility.Error(
                $"Cannot publish AP progress for {player.NetId}: local revision "
                    + $"{_localProgressRevision} does not match run data {state.ProgressRevision}"
            );
            return false;
        }

        long baseRevision = state.ProgressRevision;
        long revision = baseRevision + 1;
        if (!_sharedRun.TryGet(runState, out ApRunSharedState shared))
            return false;

        var snapshotMessage = new ApProgressSnapshotMessage
        {
            RunId = shared.RunId,
            OwnerNetId = player.NetId,
            Revision = revision,
            Progress = snapshot,
        };
        var deltaMessage = delta == null
            ? null
            : new ApProgressDeltaMessage
            {
                RunId = shared.RunId,
                OwnerNetId = player.NetId,
                BaseRevision = baseRevision,
                Revision = revision,
                Delta = delta,
            };
        bool sent = true;
        if (RunManager.Instance.NetService.Type != NetGameType.Host)
        {
            // The first publication establishes a complete baseline. Every later message is a
            // small ordered patch, so saved reward assignments are not resent after each check.
            sent = delta == null
                ? RitsuLibSidecarTypedMessageRegistry.SendToHost(
                    RunManager.Instance,
                    ProgressSnapshotDescriptor,
                    snapshotMessage
                )
                : RitsuLibSidecarTypedMessageRegistry.SendToHost(
                    RunManager.Instance,
                    ProgressDeltaDescriptor,
                    deltaMessage!
                );
        }

        if (!sent)
            return false;

        state.Progress = snapshot;
        state.ProgressRevision = revision;
        _players.Set(runState, player.NetId, state);
        _localProgressRevision = revision;
        _lastPublishedLocalProgress = snapshot;
        StandardRelicPool.ReserveAssignedChoices(player, snapshot);
        if (RunManager.Instance.NetService.Type == NetGameType.Host)
        {
            if (!BroadcastHostConfirmedProgress(snapshotMessage, deltaMessage))
                return false;
            AncientMultiplayer.ConfirmProgress(runState, player.NetId, revision, snapshot);
        }
        return true;
    }

    public static bool IsReceiptUsed(RunState runState, ulong netId, int receivedItemIndex) =>
        _players.TryGet(runState, netId, out ApPlayerRunState state)
        && state.Progress.UsedItems.Contains(receivedItemIndex);

    /// <summary>Forgets the local publication baseline; host-carried run data is untouched.</summary>
    public static void EndRun()
    {
        _localProgressRevision = 0;
        _lastPublishedLocalProgress = null;
    }

    public static void CaptureLocalHostProgressBeforeSave()
    {
        if (RunManager.Instance.NetService.Type != NetGameType.Host)
            return;
        RunState? runState = RunManager.Instance.DebugOnlyGetState();
        Player? localPlayer = runState?.GetPlayer(RunManager.Instance.NetService.NetId);
        if (localPlayer != null)
            PublishLocalProgress(localPlayer);
        if (runState != null)
            foreach (Player player in runState.Players)
                if (_players.TryGet(runState, player.NetId, out ApPlayerRunState state) && state.Progress.Initialized)
                {
                    RelicReceiptMultiplayer.ReconcileProgress(runState, player, state.Progress);
                    _players.Set(runState, player.NetId, state);
                }
    }

    /// <summary>
    /// Accepts a client's initial full view. Full snapshots are uncommon recovery/binding
    /// boundaries; normal mutations arrive through <see cref="OnProgressDeltaReceived"/>.
    /// </summary>
    private static void OnProgressSnapshotReceived(
        RitsuLibSidecarTypedDispatchContext<ApProgressSnapshotMessage> context)
    {
        bool isHost = RunManager.Instance.NetService.Type == NetGameType.Host;
        if (isHost && context.SenderNetId != context.Message.OwnerNetId)
        {
            LogUtility.Error("Rejected incorrectly owned AP progress snapshot.");
            return;
        }
        if (!isHost && !IsMessageFromHost(context.SenderNetId))
        {
            LogUtility.Error("Rejected AP progress snapshot from a non-host peer.");
            return;
        }

        bool posted = RitsuLibSidecarGodotMainLoopScheduling.TryPostToMainLoop(() =>
        {
            if (!TryGetProgressOwner(
                    context.Message.RunId,
                    context.Message.OwnerNetId,
                    out RunState runState,
                    out ApPlayerRunState state)
                || !context.Message.Progress.Initialized)
            {
                return;
            }

            if (context.Message.Revision <= state.ProgressRevision)
            {
                if (!isHost && context.Message.Revision == state.ProgressRevision)
                {
                    if (runState.GetPlayer(context.Message.OwnerNetId) is Player existingOwner)
                        StandardRelicPool.ReserveAssignedChoices(existingOwner, state.Progress);
                    AncientMultiplayer.ConfirmProgress(
                        runState,
                        context.Message.OwnerNetId,
                        context.Message.Revision,
                        context.Message.Progress
                    );
                }
                return;
            }

            EnsureConstructionInitialized(state, context.Message.Progress);
            state.Progress = context.Message.Progress;
            if (runState.GetPlayer(context.Message.OwnerNetId) is Player snapshotOwner)
                RelicReceiptMultiplayer.ReconcileProgress(runState, snapshotOwner, state.Progress);
            state.ProgressRevision = context.Message.Revision;
            _players.Set(runState, context.Message.OwnerNetId, state);
            if (runState.GetPlayer(context.Message.OwnerNetId) is Player confirmedOwner)
                StandardRelicPool.ReserveAssignedChoices(confirmedOwner, state.Progress);
            if (isHost)
            {
                if (!BroadcastHostConfirmedProgress(context.Message, deltaMessage: null))
                    return;
            }
            AncientMultiplayer.ConfirmProgress(
                runState,
                context.Message.OwnerNetId,
                context.Message.Revision,
                state.Progress
            );
        });
        if (!posted)
            LogUtility.Error("Could not schedule the AP progress snapshot on the game main loop.");
    }

    /// <summary>
    /// Applies one client mutation only to the exact baseline it was created from. Sidecar's
    /// stable ordered delivery should make revisions contiguous; a gap is rejected because
    /// applying a delta to a different snapshot could resurrect or duplicate a reward.
    /// </summary>
    private static void OnProgressDeltaReceived(
        RitsuLibSidecarTypedDispatchContext<ApProgressDeltaMessage> context)
    {
        bool isHost = RunManager.Instance.NetService.Type == NetGameType.Host;
        if (isHost && context.SenderNetId != context.Message.OwnerNetId)
        {
            LogUtility.Error("Rejected incorrectly owned AP progress delta.");
            return;
        }
        if (!isHost && !IsMessageFromHost(context.SenderNetId))
        {
            LogUtility.Error("Rejected AP progress delta from a non-host peer.");
            return;
        }

        bool posted = RitsuLibSidecarGodotMainLoopScheduling.TryPostToMainLoop(() =>
        {
            if (!TryGetProgressOwner(
                    context.Message.RunId,
                    context.Message.OwnerNetId,
                    out RunState runState,
                    out ApPlayerRunState state))
            {
                return;
            }
            if (context.Message.Revision <= state.ProgressRevision)
            {
                if (!isHost && context.Message.Revision == state.ProgressRevision)
                {
                    if (runState.GetPlayer(context.Message.OwnerNetId) is Player existingOwner)
                        StandardRelicPool.ReserveAssignedChoices(existingOwner, state.Progress);
                    AncientMultiplayer.ConfirmProgress(
                        runState,
                        context.Message.OwnerNetId,
                        context.Message.Revision,
                        state.Progress
                    );
                }
                return;
            }
            
            // only can do 1 base revision difference, maybe we could repair a missing baseline 
            // i.e. support differences of more than 1 revision in the future?
            if (!state.Progress.Initialized
                || !context.Message.Delta.HasChanges
                || context.Message.BaseRevision != state.ProgressRevision
                || context.Message.Revision != context.Message.BaseRevision + 1)
            {
                LogUtility.Error(
                    $"Rejected AP progress delta for {context.Message.OwnerNetId}: "
                        + $"hostRevision={state.ProgressRevision}, "
                        + $"baseRevision={context.Message.BaseRevision}, "
                        + $"revision={context.Message.Revision}"
                );
                return;
            }

            EnsureConstructionInitialized(state, state.Progress);
            ApRunProgressState updatedProgress = context.Message.Delta.ApplyToCopy(state.Progress);
            state.Progress = updatedProgress;
            if (runState.GetPlayer(context.Message.OwnerNetId) is Player deltaOwner)
                RelicReceiptMultiplayer.ReconcileProgress(runState, deltaOwner, state.Progress);
            state.ProgressRevision = context.Message.Revision;
            _players.Set(runState, context.Message.OwnerNetId, state);
            if (runState.GetPlayer(context.Message.OwnerNetId) is Player confirmedOwner)
                StandardRelicPool.ReserveAssignedChoices(confirmedOwner, state.Progress);
            if (isHost)
            {
                if (!BroadcastHostConfirmedProgress(
                        snapshotMessage: null,
                        deltaMessage: context.Message
                    ))
                {
                    return;
                }
            }
            AncientMultiplayer.ConfirmProgress(
                runState,
                context.Message.OwnerNetId,
                context.Message.Revision,
                state.Progress
            );
        });
        if (!posted)
            LogUtility.Error("Could not schedule the AP progress delta on the game main loop.");
    }

    internal static ApReplicaConstructionState EnsureConstructionInitialized(
        ApPlayerRunState state,
        ApRunProgressState baseline)
    {
        state.Construction ??= new ApReplicaConstructionState();
        state.Construction.EnsureInitialized(
            baseline.CardRewardsAttempted,
            baseline.RareCardRewardsAttempted,
            baseline.GoldRewardsAttempted,
            baseline.PotionRewardsAttempted,
            baseline.MultiplayerBossCompensatedActs
        );
        return state.Construction;
    }

    private static bool TryGetProgressOwner(
        Guid runId,
        ulong ownerNetId,
        out RunState runState,
        out ApPlayerRunState state)
    {
        runState = null!;
        state = null!;
        if (RunManager.Instance.DebugOnlyGetState() is not RunState currentRun
            || !_sharedRun.TryGet(currentRun, out ApRunSharedState shared)
            || shared.RunId != runId
            || !_players.TryGet(currentRun, ownerNetId, out state)
            || state.Participation == ApParticipationKind.VanillaGuest)
        {
            return false;
        }

        runState = currentRun;
        return true;
    }

    private static bool IsMessageFromHost(ulong senderNetId) =>
        BetaMainCompatibility.TryGetHostNetId(
            RunManager.Instance.NetService,
            out ulong hostNetId
        ) && senderNetId == hostNetId;

    // maybe these can be in a shared union (if only unions existed in C#)
    private static bool BroadcastHostConfirmedProgress(
        ApProgressSnapshotMessage? snapshotMessage,
        ApProgressDeltaMessage? deltaMessage)
    {
        bool sent = snapshotMessage != null
            ? RitsuLibSidecarTypedMessageRegistry.Broadcast(
                RunManager.Instance.NetService,
                ProgressSnapshotDescriptor,
                snapshotMessage
            )
            : deltaMessage != null
                && RitsuLibSidecarTypedMessageRegistry.Broadcast(
                    RunManager.Instance.NetService,
                    ProgressDeltaDescriptor,
                    deltaMessage
                );
        if (!sent)
        {
            LogUtility.Error(
                "Could not broadcast host-confirmed AP progress to multiplayer peers."
            );
        }
        return sent;
    }

    private static void EnsureLobbyRunId(StartRunLobby lobby)
    {
        if (_sharedRun.Lobby.TryGet(lobby, out ApRunSharedState existing)
            && existing.RunId != Guid.Empty)
        {
            return;
        }

        _sharedRun.Lobby.Modify(lobby, state =>
        {
            if (state.RunId == Guid.Empty)
                state.RunId = Guid.NewGuid();
        });
    }

    private static void StageHostSettings(
        StartRunLobby lobby,
        ApParticipationKind hostParticipation)
    {
        bool shouldStageHostSettings =
            hostParticipation == ApParticipationKind.OwnApSlot;

        ArchipelagoSettings? hostSettings = shouldStageHostSettings
            ? MultiplayerSupport.CreateEffectiveHostSettingsSnapshot()
            : null;
        long hostCharacterOffset = 0;
        var configuredAscensions = new List<int>();
        var currentAscensions = new List<int>();
        var handledReceiptIndexes = new List<int>();
        string ascensionError = "the fixed host does not have AP settings";
        bool ascensionStateInitialized = hostSettings != null
            && AscensionMultiplayer.TryBuildLobbyState(
                lobby,
                hostSettings,
                MultiplayerSupport.GetCurrentOwnSlotReceivedItems(),
                out hostCharacterOffset,
                out configuredAscensions,
                out currentAscensions,
                out handledReceiptIndexes,
                out ascensionError);
        if (!ascensionStateInitialized)
        {
            hostCharacterOffset = 0;
            configuredAscensions = new List<int>();
            currentAscensions = new List<int>();
            handledReceiptIndexes = new List<int>();
            if (shouldStageHostSettings)
                LogUtility.Warn($"Could not stage host ascensions: {ascensionError}");
        }

        // Lobby writes emit RunSavedDataLobbyStagingEvent. That event asks the host UI to
        // refresh, and the refresh stages this same contract again. Only changed construction
        // settings should be written; otherwise the host creates an endless write -> refresh -> write loop
        // that starves the native lobby network update.
        if (_sharedRun.Lobby.TryGet(lobby, out ApRunSharedState existing)
            && existing.SchemaVersion == RunSchemaVersion
            && (shouldStageHostSettings
                ? existing.HostSettings != null
                : existing.HostSettings == null)
            && existing.HostSettings?.AncientRelicLocation == hostSettings?.AncientRelicLocation
            && existing.HostSettings?.AncientRelicPool == hostSettings?.AncientRelicPool
            && existing.AscensionStateInitialized == ascensionStateInitialized
            && existing.HostCharacterOffset == (ascensionStateInitialized
                ? hostCharacterOffset
                : null)
            && existing.ConfiguredAscensions.SequenceEqual(configuredAscensions)
            && existing.CurrentAscensions.SequenceEqual(currentAscensions)
            && existing.HandledAscensionDownReceiptIndexes.SequenceEqual(
                handledReceiptIndexes))
        {
            return;
        }

        _sharedRun.Lobby.Modify(lobby, state =>
        {
            state.SchemaVersion = RunSchemaVersion;
            state.HostSettings = hostSettings;
            state.AscensionStateInitialized = ascensionStateInitialized;
            state.HostCharacterOffset = ascensionStateInitialized
                ? hostCharacterOffset
                : null;
            state.ConfiguredAscensions = configuredAscensions;
            state.CurrentAscensions = currentAscensions;
            state.HandledAscensionDownReceiptIndexes = handledReceiptIndexes;
        });
    }

    private static bool HasSameLobbyContribution(
        ApPlayerRunState left,
        ApPlayerRunState right) =>
        left.SchemaVersion == right.SchemaVersion
        && left.Participation == right.Participation
        && string.Equals(left.ApRoomSeed, right.ApRoomSeed, StringComparison.Ordinal)
        && left.ApTeamId == right.ApTeamId
        && left.ApSlotId == right.ApSlotId
        && (left.SlotSettings == null) == (right.SlotSettings == null)
        && left.SlotSettings?.AncientRelicLocation == right.SlotSettings?.AncientRelicLocation
        && left.SlotSettings?.AncientRelicPool == right.SlotSettings?.AncientRelicPool
        && left.SlotSettings?.PlayerNumber == right.SlotSettings?.PlayerNumber
        && left.SlotSettings?.PlayerCount == right.SlotSettings?.PlayerCount
        && RelicReceiptMapsEqual(
            left.InitialRelicReceiptIndexesByCharacter,
            right.InitialRelicReceiptIndexesByCharacter)
        && CountMapsEqual(
            left.InitialProgressiveAncientsByCharacter,
            right.InitialProgressiveAncientsByCharacter)
        && left.ReceiptSourceReady == right.ReceiptSourceReady;

    private static bool RelicReceiptMapsEqual(
        IReadOnlyDictionary<long, List<int>> left,
        IReadOnlyDictionary<long, List<int>> right) =>
        left.Count == right.Count
        && left.All(pair =>
            right.TryGetValue(pair.Key, out List<int>? values)
            && pair.Value.SequenceEqual(values));

    private static bool CountMapsEqual(
        IReadOnlyDictionary<long, int> left,
        IReadOnlyDictionary<long, int> right) =>
        left.Count == right.Count
        && left.All(pair => right.TryGetValue(pair.Key, out int value) && value == pair.Value);

    private static void OnLobbyStagingChanged(RunSavedDataLobbyStagingEvent evt)
    {
        if (!evt.IsMultiplayer || !evt.IsHost)
            return;

        if (evt.Reason == RunSavedDataLobbyStagingReason.Committing)
        {
            if (!AscensionMultiplayer.PrepareRunConstruction(
                    evt.Lobby,
                    out int nativeAscensionLevel,
                    out string reason))
            {
                LogUtility.Error($"Could not capture host ascensions at lobby commit: {reason}");
                return;
            }

            // Match single-player before the begin-run message is sent. The native value keeps
            // base presentation/save topology coherent; individual checks use the frozen set.
            evt.Lobby.SyncAscensionChange(nativeAscensionLevel);
            return;
        }

        MultiplayerSupport.RequestHostLobbyRefresh(evt.Lobby);
    }

    private static void OnRunDataPreparing(RunSavedDataPreparingEvent evt)
    {
        bool isAuthoritative = !evt.IsMultiplayer
            || RunManager.Instance.NetService.Type == NetGameType.Host;
        if (!isAuthoritative)
        {
            if (!_sharedRun.TryGet(evt.RunState, out ApRunSharedState clientState)
                || clientState.RunId == Guid.Empty)
            {
                LogUtility.Error("AP multiplayer run snapshot arrived without a host RunId");
            }
        }
        else
        {
            _sharedRun.Modify(evt.RunState, state =>
            {
                if (state.RunId == Guid.Empty)
                    state.RunId = Guid.NewGuid();
            });
        }

        if (evt.IsMultiplayer
            && !AscensionMultiplayer.PrepareRunConstruction(
                evt.RunState,
                out string ascensionReason))
        {
            LogUtility.Error(
                $"Could not prepare multiplayer ascensions from the run payload: "
                    + ascensionReason
            );
            MultiplayerSupport.InvalidateRunClaims(ascensionReason);
        }
    }
}
