using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Runs;
using StS2AP.Utils;
using STS2RitsuLib.Networking.Sidecar;

namespace StS2AP.Multiplayer;

/// <summary>
/// Arbitrates chest funding and AP-menu assignments before native reward construction. Sidecar
/// messages do not enter the native action queue: room entry may already be executing there.
/// </summary>
public static class RelicReceiptMultiplayer
{
    private sealed class Request
    {
        public Request() { }
        public Guid RunId { get; set; }
        public Guid RequestId { get; set; }
        public ulong OwnerNetId { get; set; }
        public string? RoomKey { get; set; }
        public List<int> MenuIndexes { get; set; } = new();
    }

    private sealed class Reply
    {
        public Reply() { }
        public Guid RunId { get; set; }
        public Guid RequestId { get; set; }
        public ulong OwnerNetId { get; set; }
        public List<int> MenuIndexes { get; set; } = new();
        public ApRelicReceiptState.ChestDecision? Chest { get; set; }
        public List<int> BankedMenuIndexes { get; set; } = new();
    }

    private sealed class TreasureProceedReady
    {
        public TreasureProceedReady() { }
        public Guid RunId { get; set; }
        public string RoomKey { get; set; } = string.Empty;
        public ulong PlayerNetId { get; set; }
        public bool AllPlayersReady { get; set; }
    }

    private static readonly RitsuLibSidecarJsonSerializer<Request> RequestSerializer = new();
    private static readonly RitsuLibSidecarJsonSerializer<Reply> ReplySerializer = new();
    private static readonly RitsuLibSidecarMessageDescriptor<Request> RequestDescriptor = new(
        ModEntry.ModId, "relic_receipt_request_v1", RequestSerializer.Serialize,
        RequestSerializer.Deserialize, Required: true);
    private static readonly RitsuLibSidecarMessageDescriptor<Reply> ReplyDescriptor = new(
        ModEntry.ModId, "relic_receipt_decision_v1", ReplySerializer.Serialize,
        ReplySerializer.Deserialize, Required: true);
    private static readonly RitsuLibSidecarJsonSerializer<TreasureProceedReady>
        TreasureProceedReadySerializer = new();
    private static readonly RitsuLibSidecarMessageDescriptor<TreasureProceedReady>
        TreasureProceedReadyRequestDescriptor = new(
            ModEntry.ModId,
            "treasure_proceed_ready_request_v1",
            TreasureProceedReadySerializer.Serialize,
            TreasureProceedReadySerializer.Deserialize,
            Required: true
        );
    private static readonly RitsuLibSidecarMessageDescriptor<TreasureProceedReady>
        TreasureProceedReadyDecisionDescriptor = new(
            ModEntry.ModId,
            "treasure_proceed_ready_decision_v1",
            TreasureProceedReadySerializer.Serialize,
            TreasureProceedReadySerializer.Deserialize,
            Required: true
        );
    private static readonly Dictionary<Guid, TaskCompletionSource<Reply>> PendingMenus = new();
    private static readonly Dictionary<string, TaskCompletionSource> PendingChests = new();
    private static readonly HashSet<string> ReadyPickers = new();
    private static readonly HashSet<(string, ulong)> OpenedChests = new();
    private static readonly Dictionary<string, HashSet<ulong>> ProceedReadyPlayers = new();
    private static readonly HashSet<string> ProceedReadyRooms = new();
    private static TaskCompletionSource _decisionsChanged = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static IDisposable? _requestSubscription;
    private static IDisposable? _replySubscription;
    private static IDisposable? _treasureProceedReadyRequestSubscription;
    private static IDisposable? _treasureProceedReadyDecisionSubscription;
    private static int _runGeneration;

    public static void Initialize()
    {
        _requestSubscription ??= RitsuLibSidecarTypedMessageRegistry.Subscribe(
            RequestDescriptor, context => Post(() => HandleRequest(context.SenderNetId, context.Message)));
        _replySubscription ??= RitsuLibSidecarTypedMessageRegistry.Subscribe(
            ReplyDescriptor, context => Post(() =>
            {
                if (BetaMainCompatibility.TryGetHostNetId(RunManager.Instance.NetService, out ulong host)
                    && context.SenderNetId == host)
                    ApplyReply(context.Message);
            }));
        _treasureProceedReadyRequestSubscription ??= RitsuLibSidecarTypedMessageRegistry.Subscribe(
            TreasureProceedReadyRequestDescriptor,
            context => Post(() => HandleTreasureProceedReady(
                context.SenderNetId,
                context.Message
            ))
        );
        _treasureProceedReadyDecisionSubscription ??= RitsuLibSidecarTypedMessageRegistry.Subscribe(
            TreasureProceedReadyDecisionDescriptor,
            context => Post(() =>
            {
                if (BetaMainCompatibility.TryGetHostNetId(
                        RunManager.Instance.NetService,
                        out ulong host)
                    && context.SenderNetId == host)
                {
                    ApplyTreasureProceedReady(context.Message);
                }
            })
        );
    }

    public static void EndRun()
    {
        Interlocked.Increment(ref _runGeneration);
        foreach (var pending in PendingMenus.Values) pending.TrySetCanceled();
        foreach (var pending in PendingChests.Values) pending.TrySetCanceled();
        PendingMenus.Clear();
        PendingChests.Clear();
        ReadyPickers.Clear();
        OpenedChests.Clear();
        ProceedReadyPlayers.Clear();
        ProceedReadyRooms.Clear();
        _decisionsChanged.TrySetCanceled();
        _decisionsChanged = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private static void Post(Action action)
    {
        int generation = Volatile.Read(ref _runGeneration);
        if (!RitsuLibSidecarGodotMainLoopScheduling.TryPostToMainLoop(() =>
        {
            if (generation != Volatile.Read(ref _runGeneration)) return;
            try { action(); }
            catch (Exception ex) { Fail(ex); }
        }))
            LogUtility.Error("Could not dispatch a relic receipt decision on the Godot main loop.");
    }

    private static void Fail(Exception ex)
    {
        LogUtility.Error($"AP relic receipt agreement failed: {ex}");
        MultiplayerSupport.InvalidateRunClaims("Relic receipt agreement failed; reload the campaign.");
    }

    public static string RoomKey(RunState run) => $"chest:{run.CurrentActIndex}:{run.CurrentMapCoord}";
    private static string WaitKey(Guid runId, string roomKey) => $"{runId}:{roomKey}";
    private static string WaitKey(RunState run) =>
        WaitKey(ApRunData.GetSharedState(run).RunId, RoomKey(run));
    public static ApRelicReceiptState State(RunState run) => ApRunData.GetSharedState(run).RelicReceipts;

    public static bool IsReserved(Player player, int index) =>
        MultiplayerSupport.IsRealMultiplayerRun && player.RunState is RunState run
        && State(run).Find(player.NetId, index) != null;

    public static bool CanUseMenu(Player player, int index) =>
        !MultiplayerSupport.IsRealMultiplayerRun || player.RunState is RunState run
        && State(run).CanUseMenu(player.NetId, index);

    /// <summary>Only an acknowledged subset may be materialized into stable menu assignments.</summary>
    public static async Task<IReadOnlySet<int>?> ApproveMenu(Player player, IReadOnlyList<int> indexes)
    {
        if (!MultiplayerSupport.IsRealMultiplayerRun) return null;
        if (indexes.Count == 0) return new HashSet<int>();
        if (!ApRunData.PublishLocalProgress(player))
            throw new InvalidOperationException("Could not publish AP progress before relic menu approval.");
        var run = (RunState)player.RunState;
        var request = new Request
        {
            RunId = ApRunData.GetSharedState(run).RunId,
            RequestId = Guid.NewGuid(), OwnerNetId = player.NetId,
            MenuIndexes = indexes.Distinct().ToList(),
        };
        var pending = new TaskCompletionSource<Reply>(TaskCreationOptions.RunContinuationsAsynchronously);
        PendingMenus.Add(request.RequestId, pending);
        try
        {
            if (RunManager.Instance.NetService.Type == NetGameType.Host)
                HandleRequest(player.NetId, request);
            else if (!RitsuLibSidecarTypedMessageRegistry.SendToHost(
                RunManager.Instance.NetService, RequestDescriptor, request))
                throw new InvalidOperationException("Could not request relic menu reservations.");
            var reply = await pending.Task.WaitAsync(TimeSpan.FromSeconds(15));
            if (RunManager.Instance.DebugOnlyGetState() != run)
                throw new OperationCanceledException("Run changed during relic menu approval.");
            return reply.MenuIndexes.ToHashSet();
        }
        finally { PendingMenus.Remove(request.RequestId); }
    }

    public static async Task FreezeChest(RunState run)
    {
        string roomKey = RoomKey(run);
        string waitKey = WaitKey(run);
        if (RunManager.Instance.NetService.Type == NetGameType.Host)
        {
            if (!State(run).Chests.TryGetValue(roomKey, out var decision))
            {
                decision = new() { RoomKey = roomKey };
                foreach (Player player in run.Players)
                {
                    int number = MultiplayerLocationChecks.GetRelicRewardsAttempted(player) + 1;
                    bool generates = Hook.ShouldGenerateTreasure(run, player);
                    bool gated = generates && MultiplayerLocationChecks.TryGetCheckSettings(player, out _)
                        && number <= ArchipelagoProgress._maxRelicRewards;
                    decision.Candidates.Add(new()
                    {
                        PlayerNetId = player.NetId, GeneratesRelic = generates, ApGated = gated,
                        RewardNumber = number,
                        ReceiptIndex = gated ? RelicRewardUtility.FindWaitingReceiptIndexForNaturalReward(player) : null,
                    });
                }
                ApRunData.ModifyRelicReceipts(run, state => state.AddChest(decision));
                LogUtility.Info($"Treasure AP decision frozen: room={roomKey}, " + string.Join(", ",
                    decision.Candidates.Select(c => $"player={c.PlayerNetId}/keep={c.Keep}/receipt={c.ReceiptIndex}")));
            }
            // The immediately following native BeginRelicPicking supplies the native IDs and
            // publishes the completed decision. Clients wait without blocking the action queue.
            return;
        }

        // An early broadcast can arrive before local room entry. Otherwise ask for the same
        // immutable decision; a host still entering the room will publish it when its hook ends.
        if (State(run).Chests.TryGetValue(roomKey, out var frozen) && frozen.NativeRelicIds != null) return;
        var pending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        PendingChests.Add(waitKey, pending);
        try
        {
            if (!RitsuLibSidecarTypedMessageRegistry.SendToHost(RunManager.Instance.NetService,
                RequestDescriptor, new Request
                {
                    RunId = ApRunData.GetSharedState(run).RunId,
                    OwnerNetId = RunManager.Instance.NetService.NetId, RoomKey = roomKey,
                }))
                throw new InvalidOperationException("Could not request the host chest decision.");
            await pending.Task.WaitAsync(TimeSpan.FromSeconds(30));
        }
        catch (Exception ex)
        {
            if (MultiplayerSupport.IsRealMultiplayerRun && RunManager.Instance.DebugOnlyGetState() == run)
                Fail(ex);
            throw;
        }
        finally { PendingChests.Remove(waitKey); }
    }

    private static void HandleRequest(ulong sender, Request request)
    {
        if (RunManager.Instance.NetService.Type != NetGameType.Host
            || sender != request.OwnerNetId || !TryRun(request.RunId, out var run)
            || run.GetPlayer(sender) == null)
            return;
        if (request.RoomKey != null)
        {
            if (State(run).Chests.TryGetValue(request.RoomKey, out var chest) && chest.NativeRelicIds != null)
                Publish(new Reply { RunId = request.RunId, Chest = chest });
            return;
        }
        if (!ApRunData.TryGetPlayerState(run, sender, out var playerState)
            || playerState.Participation == ApParticipationKind.VanillaGuest)
            return;
        var reply = new Reply { RunId = request.RunId, RequestId = request.RequestId, OwnerNetId = sender };
        var player = run.GetPlayer(sender)!;
        var catalog = MultiplayerLocationChecks.GetReplicatedRelicReceiptIndexes(player, playerState.Progress).ToList();
        ApRunData.ModifyRelicReceipts(run, state =>
        {
            reply.MenuIndexes = state.ApproveMenu(sender, request.MenuIndexes, catalog, playerState.Progress);
            reply.BankedMenuIndexes = reply.MenuIndexes.Where(index => state.Find(sender, index)!.RequiresBank).ToList();
        });
        Publish(reply);
    }

    private static void Publish(Reply reply)
    {
        if (!RitsuLibSidecarTypedMessageRegistry.Broadcast(RunManager.Instance.NetService, ReplyDescriptor, reply))
            throw new InvalidOperationException("Could not publish the host relic receipt decision.");
        ApplyReply(reply);
    }

    private static void ApplyReply(Reply reply)
    {
        if (!TryRun(reply.RunId, out var run)) return;
        ApRunData.ModifyRelicReceipts(run, state =>
        {
            if (reply.Chest != null) state.AddChest(reply.Chest);
            foreach (int index in reply.MenuIndexes)
            {
                var existing = state.Find(reply.OwnerNetId, index);
                if (existing?.Destination == ApRelicReceiptState.MenuDestination) continue;
                if (!state.TryReserve(reply.OwnerNetId, index, ApRelicReceiptState.MenuDestination,
                    requiresBank: reply.BankedMenuIndexes.Contains(index)))
                    throw new InvalidOperationException("Conflicting host relic menu reservation.");
            }
        });
        var changed = _decisionsChanged;
        _decisionsChanged = new(TaskCreationOptions.RunContinuationsAsynchronously);
        changed.TrySetResult();
        if (reply.Chest != null && PendingChests.TryGetValue(
            $"{reply.RunId}:{reply.Chest.RoomKey}", out var chestPending)) chestPending.TrySetResult();
        if (PendingMenus.TryGetValue(reply.RequestId, out var menuPending)) menuPending.TrySetResult(reply);
    }

    private static bool TryRun(Guid id, out RunState run)
    {
        run = RunManager.Instance.DebugOnlyGetState()!;
        return run != null && MultiplayerSupport.IsRealMultiplayerRun
            && ApRunData.TryGetSharedState(run, out var shared) && shared.RunId == id;
    }

    public static ApRelicReceiptState.ChestDecision GetChest(RunState run) =>
        State(run).Chests.TryGetValue(RoomKey(run), out var decision) ? decision
            : throw new InvalidOperationException("Native chest picker started without the host decision.");

    public static void MarkPickerReady(RunState run) => ReadyPickers.Add(WaitKey(run));

    public static void AgreeNativeCandidates(RunState run, List<string> ids)
    {
        var decision = GetChest(run);
        if (RunManager.Instance.NetService.Type == NetGameType.Host && decision.NativeRelicIds == null)
        {
            ApRunData.ModifyRelicReceipts(run, state => state.Chests[RoomKey(run)].NativeRelicIds = ids);
            decision = GetChest(run);
            Publish(new Reply { RunId = ApRunData.GetSharedState(run).RunId, Chest = decision });
        }
        if (decision.NativeRelicIds == null || !decision.NativeRelicIds.SequenceEqual(ids))
        {
            var error = new InvalidOperationException($"Native chest relic IDs differ from the host in {RoomKey(run)}: "
                + $"expected=[{string.Join(",", decision.NativeRelicIds ?? [])}], actual=[{string.Join(",", ids)}].");
            Fail(error);
            throw error;
        }
    }
    public static bool IsPickerReady(RunState run) => ReadyPickers.Contains(WaitKey(run));
    public static bool MarkChestOpened(RunState run, ulong player)
    {
        if (!OpenedChests.Add((WaitKey(run), player))) return false;
        ApRunData.ModifyRelicReceipts(run, state => state.Chests[RoomKey(run)].SettledPlayers.Add(player));
        return true;
    }

    public static void MarkLocalTreasureProceedReady(RunState run)
    {
        var ready = new TreasureProceedReady
        {
            RunId = ApRunData.GetSharedState(run).RunId,
            RoomKey = RoomKey(run),
            PlayerNetId = RunManager.Instance.NetService.NetId,
        };
        if (RunManager.Instance.NetService.Type == NetGameType.Host)
        {
            HandleTreasureProceedReady(ready.PlayerNetId, ready);
            return;
        }

        if (!RitsuLibSidecarTypedMessageRegistry.SendToHost(
                RunManager.Instance.NetService,
                TreasureProceedReadyRequestDescriptor,
                ready))
        {
            throw new InvalidOperationException(
                "Could not report treasure proceed readiness to the host."
            );
        }
    }

    public static bool IsTreasureProceedReadyForApMenu(RunState run) =>
        ProceedReadyRooms.Contains(WaitKey(run));

    private static void HandleTreasureProceedReady(ulong sender, TreasureProceedReady ready)
    {
        if (RunManager.Instance.NetService.Type != NetGameType.Host
            || sender != ready.PlayerNetId
            || ready.AllPlayersReady
            || !TryRun(ready.RunId, out RunState run)
            || run.GetPlayer(sender) == null
            || ready.RoomKey != RoomKey(run))
        {
            return;
        }

        string waitKey = WaitKey(ready.RunId, ready.RoomKey);
        if (!ProceedReadyPlayers.TryGetValue(waitKey, out HashSet<ulong>? players))
        {
            players = new HashSet<ulong>();
            ProceedReadyPlayers.Add(waitKey, players);
        }
        players.Add(sender);
        if (ProceedReadyRooms.Contains(waitKey)
            || run.Players.Any(player => !players.Contains(player.NetId)))
        {
            return;
        }

        var decision = new TreasureProceedReady
        {
            RunId = ready.RunId,
            RoomKey = ready.RoomKey,
            PlayerNetId = RunManager.Instance.NetService.NetId,
            AllPlayersReady = true,
        };
        if (!RitsuLibSidecarTypedMessageRegistry.Broadcast(
                RunManager.Instance.NetService,
                TreasureProceedReadyDecisionDescriptor,
                decision))
        {
            throw new InvalidOperationException(
                "Could not publish treasure proceed readiness to every player."
            );
        }
        ApplyTreasureProceedReady(decision);
    }

    private static void ApplyTreasureProceedReady(TreasureProceedReady ready)
    {
        if (!ready.AllPlayersReady
            || !TryRun(ready.RunId, out RunState run)
            || ready.RoomKey != RoomKey(run))
        {
            return;
        }
        ProceedReadyRooms.Add(WaitKey(ready.RunId, ready.RoomKey));
        LogUtility.Info(
            $"All replicas reached treasure Proceed; AP rewards are safe in {ready.RoomKey}."
        );
    }

    public static void ReconcileProgress(RunState run, Player player, ApRunProgressState progress)
    {
        var gated = MultiplayerLocationChecks.GetReplicatedRelicReceiptIndexes(player, progress)
            .Skip(Math.Clamp(progress.RelicRewardsAvailableAnytimeForRun, 0, 10));
        State(run).ReconcileProgress(player.NetId, progress, gated);
    }

    public static async Task WaitForMenuReservations(Player player, IEnumerable<int> indexes)
    {
        int[] required = indexes.ToArray();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        while (required.Any(index => !CanUseMenu(player, index)))
            await _decisionsChanged.Task.WaitAsync(timeout.Token);
    }

    public static void RecordMenuAssignment(Player player, int index, string serializedRelic)
    {
        if (MultiplayerSupport.IsRealMultiplayerRun && player.RunState is RunState run)
            ApRunData.ModifyRelicReceipts(run, state => state.AssignMenu(player.NetId, index, serializedRelic));
    }

    public static void ConsumeMenu(Player player, int index)
    {
        if (MultiplayerSupport.IsRealMultiplayerRun && player.RunState is RunState run)
            ApRunData.ModifyRelicReceipts(run, state => state.Consume(
                player.NetId, index, ApRelicReceiptState.MenuDestination));
    }

    public static void ConsumeChest(Player player, int index)
    {
        var run = (RunState)player.RunState;
        ApRunData.ModifyRelicReceipts(run, state => state.Consume(player.NetId, index, RoomKey(run)));
    }
}
