using System.Text.Json;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Game.Lobby;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Runs;
using StS2AP.Data;
using StS2AP.Utils;
using StS2AP.Domain;
using StS2AP.DomainAdapters;
using STS2RitsuLib;
using STS2RitsuLib.RunData;

namespace StS2AP.Multiplayer;

/// <summary>
/// Owns the AP data embedded in MegaCrit's canonical run snapshot. Lobby methods stage the
/// launch contract. Mid-run, action-owned state (receipt ledger, queued claims, natural relic
/// decisions, construction counters) changes identically on every replica; each owner's own AP
/// progress goes to the host only, and is durable only when the fixed host writes the next
/// native checkpoint. There are no
/// Frozen/Validated booleans: launch readiness must be derived from the current contributions,
/// and the committed run snapshot is the lifecycle boundary that makes the mapping immutable.
/// </summary>
public static class ApRunData
{
    internal const int RunSchemaVersion = 9;
    private static RunSavedData<ApRunSharedState> _sharedRun = null!;
    private static PlayerRunSavedData<ApPlayerRunState> _players = null!;
    // Each owner's AP progress, client -> host, kept in the run's save as that owner's checkpoint.
    private static readonly ApNetMessageDescriptor<ApProgressCheckpointMessage> ProgressCheckpoint = new(
        "progress_checkpoint",
        static message => JsonSerializer.SerializeToUtf8Bytes(message),
        static payload => JsonSerializer.Deserialize<ApProgressCheckpointMessage>(payload)
            ?? new ApProgressCheckpointMessage(),
        OnProgressCheckpointReceived
    );
    private static bool _initialized;
    // The progress this process last sent (or restored), so an unchanged view is not resent.
    private static byte[]? _lastPublishedLocalProgress;

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
        ApNetMessages.Register(ProgressCheckpoint);
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
            CombatsSinceLastWaxMelt = existing?.CombatsSinceLastWaxMelt ?? 0,
            Construction = existing?.Construction ?? new ApReplicaConstructionState(),
            ProgressiveStarters = existing?.ProgressiveStarters
                ?? new ApProgressiveStarterPlayerState(),
            LastConsumedBuffIndex = existing?.LastConsumedBuffIndex ?? -1,
            CombatBuffLimit = existing?.CombatBuffLimit ?? new CombatEffectLimit(),
            LastConsumedTrapIndex = existing?.LastConsumedTrapIndex ?? -1,
            CombatTrapLimit = existing?.CombatTrapLimit ?? new CombatEffectLimit(),
            QueuedGoldClaimCursor = existing?.QueuedGoldClaimCursor,
            QueuedClaimedReceiptIndexes = existing?.QueuedClaimedReceiptIndexes ?? new List<int>(),
            QueuedCardOffers = existing?.QueuedCardOffers ?? new Dictionary<int, ApQueuedCardOfferState>(),
            QueuedAncientChoices = existing?.QueuedAncientChoices ?? new Dictionary<int, List<string>>(),
            ReceiptLedger = existing?.ReceiptLedger ?? new List<ApLedgerReceipt>(),
            RelicRewardsAvailableAnytime = existing?.RelicRewardsAvailableAnytime,
            CheckedCampfireLocationIds = existing?.CheckedCampfireLocationIds ?? new HashSet<long>(),
            RelicRewardsAttempted = existing?.RelicRewardsAttempted,
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

    internal static void SetWaxCombatCount(RunState runState, ulong netId, int count)
    {
        if (!_initialized || !_players.TryGet(runState, netId, out ApPlayerRunState state))
            return;
        state.CombatsSinceLastWaxMelt = count;
        _players.Set(runState, netId, state);
    }

    // Singleplayer does not contribute through a multiplayer lobby, so create its run state lazily.
    internal static ApPlayerRunState GetSingleplayerBuffState(RunState runState, ulong netId) =>
        _players.Get(runState, netId);

    /// <summary>Records successful combat effects identically on every replica, including guests.</summary>
    internal static bool RecordConsumedCombatEffect(RunState runState, ulong netId, int itemIndex,
        CombatEffectKey combat, bool trap)
    {
        if (!_initialized || !_players.TryGet(runState, netId, out ApPlayerRunState state))
            return false;
        if (trap)
        {
            state.CombatTrapLimit.Complete(combat);
            state.LastConsumedTrapIndex = Math.Max(state.LastConsumedTrapIndex, itemIndex);
        }
        else
        {
            state.CombatBuffLimit.Complete(combat);
            state.LastConsumedBuffIndex = Math.Max(state.LastConsumedBuffIndex, itemIndex);
        }
        _players.Set(runState, netId, state);
        return true;
    }

    /// <summary>
    /// Advances a player's queued gold claim cursor. The queued gold action calls this
    /// identically on every replica; owner progress deltas never touch it.
    /// </summary>
    internal static bool RecordQueuedGoldClaim(RunState runState, ulong netId, int cursorAfter)
    {
        if (!_initialized || !_players.TryGet(runState, netId, out ApPlayerRunState state))
            return false;
        state.QueuedGoldClaimCursor = Math.Max(state.QueuedGoldClaimCursor ?? 0, cursorAfter);
        _players.Set(runState, netId, state);
        return true;
    }

    /// <summary>
    /// Marks an AP receipt used up by a queued claim. Claim actions call this identically on every
    /// replica; owner progress deltas never touch it.
    /// </summary>
    internal static bool RecordQueuedReceiptClaim(RunState runState, ulong netId, int receivedItemIndex)
    {
        if (!_initialized || !_players.TryGet(runState, netId, out ApPlayerRunState state))
            return false;
        int position = state.QueuedClaimedReceiptIndexes.BinarySearch(receivedItemIndex);
        if (position < 0)
            state.QueuedClaimedReceiptIndexes.Insert(~position, receivedItemIndex);
        _players.Set(runState, netId, state);
        return true;
    }

    /// <summary>
    /// Records or clears a card offer revealed by a queued card claim. The claim action calls
    /// this identically on every replica; owner progress deltas never touch it.
    /// </summary>
    internal static bool RecordQueuedCardOffer(
        RunState runState,
        ulong netId,
        int receivedItemIndex,
        ApQueuedCardOfferState? offer)
    {
        if (!_initialized || !_players.TryGet(runState, netId, out ApPlayerRunState state))
            return false;
        if (offer == null)
            state.QueuedCardOffers.Remove(receivedItemIndex);
        else
            state.QueuedCardOffers[receivedItemIndex] = offer;
        _players.Set(runState, netId, state);
        return true;
    }

    /// <summary>
    /// Records the Ancient choices the queued relic assignment action drew, identically on every
    /// replica; owner progress deltas never touch them.
    /// </summary>
    internal static bool RecordQueuedAncientChoices(
        RunState runState,
        ulong netId,
        int receivedItemIndex,
        List<string> serializedChoices)
    {
        if (!_initialized || !_players.TryGet(runState, netId, out ApPlayerRunState state))
            return false;
        state.QueuedAncientChoices[receivedItemIndex] = serializedChoices;
        _players.Set(runState, netId, state);
        return true;
    }

    /// <summary>
    /// Appends the receipts this replica's ledger lacks, keeping it in index order, and records
    /// the anytime relic count once. The receipts action calls this identically on every replica.
    /// A receipt whose index is already recorded keeps its first item id.
    /// </summary>
    internal static int RecordLedgerReceipts(
        RunState runState,
        ulong netId,
        IEnumerable<ApLedgerReceipt> receipts,
        int? relicRewardsAvailableAnytime)
    {
        if (!_initialized || !_players.TryGet(runState, netId, out ApPlayerRunState state))
            return 0;
        int appended = 0;
        foreach (ApLedgerReceipt receipt in receipts)
        {
            if (receipt.Index <= 0 || ArchipelagoIdCodec.IsArchipelagoReservedId(receipt.ItemId))
                continue;
            List<ApLedgerReceipt> ledger = state.ReceiptLedger;
            if (ledger.Count == 0 || ledger[^1].Index < receipt.Index)
            {
                ledger.Add(new ApLedgerReceipt { Index = receipt.Index, ItemId = receipt.ItemId });
                appended++;
                continue;
            }
            int position = ledger.BinarySearch(receipt, LedgerOrder);
            if (position >= 0)
                continue;
            ledger.Insert(~position, new ApLedgerReceipt { Index = receipt.Index, ItemId = receipt.ItemId });
            appended++;
        }
        state.RelicRewardsAvailableAnytime ??= relicRewardsAvailableAnytime;
        _players.Set(runState, netId, state);
        return appended;
    }

    /// <summary>
    /// Records campfire checks a player's rest sites no longer offer. Rest-site options and the
    /// receipts action call this identically on every replica.
    /// </summary>
    internal static int RecordCampfireChecks(RunState runState, ulong netId, IEnumerable<long> locationIds)
    {
        if (!_initialized || !_players.TryGet(runState, netId, out ApPlayerRunState state))
            return 0;
        int added = locationIds.Count(state.CheckedCampfireLocationIds.Add);
        if (added > 0)
            _players.Set(runState, netId, state);
        return added;
    }

    /// <summary>
    /// Raises a player's natural relic reward count. Natural relic decisions call this
    /// identically on every replica (RelicReceiptMultiplayer).
    /// </summary>
    internal static void RecordRelicRewardsAttempted(RunState runState, ulong netId, int attempted)
    {
        if (!_initialized || !_players.TryGet(runState, netId, out ApPlayerRunState state))
            return;
        state.RelicRewardsAttempted = Math.Max(state.RelicRewardsAttempted ?? 0, attempted);
        _players.Set(runState, netId, state);
    }

    private static readonly IComparer<ApLedgerReceipt> LedgerOrder =
        Comparer<ApLedgerReceipt>.Create((left, right) => left.Index.CompareTo(right.Index));

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
        foreach (ulong netId in lobby.Players.Select(player => player.id).ToArray())
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
    /// Replaces the process-local AP view with the fixed host's checkpoint for this player. When
    /// no initialized progress exists, the caller initializes a fresh view.
    /// </summary>
    public static bool RestoreLocalProgress(Player player)
    {
        _lastPublishedLocalProgress = null;
        if (!_initialized)
            return false;
        if (player.RunState is not RunState runState
            || !_players.TryGet(runState, player.NetId, out ApPlayerRunState state)
            || !state.Progress.Initialized)
        {
            return false;
        }

        ArchipelagoClient.Progress = ArchipelagoProgress.FromRunProgressState(state.Progress, player);
        _lastPublishedLocalProgress = SerializeProgress(ArchipelagoClient.Progress.ToRunProgressState());
        return true;
    }

    /// <summary>
    /// Sends the local owner's complete AP progress to the host whenever it changed, as one native
    /// message; the host keeps it in the run's save, the checkpoint that restores this owner's AP
    /// view on continue or rejoin (the rejoin response carries it back). No other peer reads it:
    /// they build this player's AP state from action-owned run state (the receipt ledger, queued
    /// claims, natural relic decisions). A late-run snapshot is a few kilobytes, so there is no
    /// delta chain; messages arrive in order and the newest wins.
    /// </summary>
    public static bool PublishLocalProgress(Player player)
    {
        if (!_initialized || !MultiplayerSupport.IsRealMultiplayerRun)
            return true;
        if (player.RunState is not RunState runState
            || !_players.TryGet(runState, player.NetId, out ApPlayerRunState state)
            || state.Participation == ApParticipationKind.VanillaGuest
            || !_sharedRun.TryGet(runState, out ApRunSharedState shared))
        {
            return false;
        }

        ApRunProgressState snapshot = ArchipelagoClient.Progress.ToRunProgressState();
        byte[] serialized = SerializeProgress(snapshot);
        if (_lastPublishedLocalProgress != null && serialized.AsSpan().SequenceEqual(_lastPublishedLocalProgress))
            return true;

        if (RunManager.Instance.NetService.Type != NetGameType.Host
            && !ApNetMessages.SendToHost(ProgressCheckpoint, new ApProgressCheckpointMessage
            {
                RunId = shared.RunId,
                OwnerNetId = player.NetId,
                Progress = snapshot,
            }))
        {
            return false;
        }

        state.Progress = snapshot;
        _players.Set(runState, player.NetId, state);
        _lastPublishedLocalProgress = serialized;
        return true;
    }

    private static byte[] SerializeProgress(ApRunProgressState progress) =>
        JsonSerializer.SerializeToUtf8Bytes(progress);

    /// <summary>Forgets the local publication baseline; host-carried run data is untouched.</summary>
    public static void EndRun() => _lastPublishedLocalProgress = null;

    public static void CaptureLocalHostProgressBeforeSave()
    {
        if (RunManager.Instance.NetService.Type != NetGameType.Host)
            return;
        RunState? runState = RunManager.Instance.DebugOnlyGetState();
        Player? localPlayer = runState?.GetPlayer(RunManager.Instance.NetService.NetId);
        if (localPlayer != null)
            PublishLocalProgress(localPlayer);
    }

    /// <summary>
    /// Host: stores a client's AP progress as that client's checkpoint. Only the owner may send
    /// its own; native messages keep their order, so the newest snapshot wins.
    /// </summary>
    private static void OnProgressCheckpointReceived(ApNetMessageContext<ApProgressCheckpointMessage> context)
    {
        if (RunManager.Instance.NetService.Type != NetGameType.Host)
            return;
        ApProgressCheckpointMessage message = context.Message;
        if (context.SenderNetId != message.OwnerNetId)
        {
            LogUtility.Error($"Rejected AP progress for {message.OwnerNetId} sent by {context.SenderNetId}.");
            return;
        }
        if (!message.Progress.Initialized
            || !TryGetProgressOwner(message.RunId, message.OwnerNetId, out RunState runState, out ApPlayerRunState state))
        {
            return;
        }

        state.Progress = message.Progress;
        _players.Set(runState, message.OwnerNetId, state);
    }

    /// <summary>
    /// Every peer, at a run's launch: starts each AP player's reward-construction counters from
    /// the run's saved state, the same on every peer: the saved counters, else the progress the
    /// host saved for the player, else zero for a new run. From then on only construction on
    /// this replica advances them, so no peer needs the owner's published progress.
    /// </summary>
    public static void BindConstruction(RunState runState)
    {
        if (!_initialized)
            return;
        foreach (Player player in runState.Players)
        {
            if (!_players.TryGet(runState, player.NetId, out ApPlayerRunState state)
                || state.Participation == ApParticipationKind.VanillaGuest)
            {
                continue;
            }
            EnsureConstructionInitialized(
                state,
                state.Progress.Initialized ? state.Progress : new ApRunProgressState()
            );
            _players.Set(runState, player.NetId, state);
        }
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
            && (existing.HostSettings?.RunModifiers ?? []).SequenceEqual(
                hostSettings?.RunModifiers ?? [])
            && existing.HostSettings?.EnemyHpScaling == hostSettings?.EnemyHpScaling
            && existing.HostSettings?.EnemyDamageScaling == hostSettings?.EnemyDamageScaling
            && existing.HostSettings?.DreadMode == hostSettings?.DreadMode
            && existing.HostSettings?.BlitzMerchantChance == hostSettings?.BlitzMerchantChance
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
        && CountMapsEqual(
            left.InitialProgressiveAncientsByCharacter,
            right.InitialProgressiveAncientsByCharacter)
        && left.ReceiptSourceReady == right.ReceiptSourceReady;

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

        // This event fires before the base game initializes the run's modifiers. Multiplayer
        // peers all read the host's frozen settings so they build the same run.
        ArchipelagoSettings? modifierSettings = ArchipelagoClient.Settings;
        if (evt.IsMultiplayer)
        {
            modifierSettings = _sharedRun.TryGet(evt.RunState, out ApRunSharedState shared)
                ? shared.HostSettings
                : null;
        }
        // The lobby is a standard one, so the host's Dread Mode is applied here, the first point
        // where guests know the host's settings.
        if (evt.IsMultiplayer)
            DreadRuns.ApplyHostSetting(evt.RunState, modifierSettings);
        ApRunModifiers.AddToNewRun(evt.RunState, modifierSettings);

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
