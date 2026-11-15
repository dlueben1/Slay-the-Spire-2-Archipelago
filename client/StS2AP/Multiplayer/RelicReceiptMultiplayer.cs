using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using StS2AP.Utils;
using StS2AP.Utils.RunGraph;
using static StS2AP.Data.ItemTable;

namespace StS2AP.Multiplayer;

/// <summary>
/// Pairs multiplayer AP Relic receipts with natural relic rewards (chests, Elites, Black Star) on
/// every peer, from action-owned state alone: the receipt ledger, each player's natural relic
/// count (<see cref="ApPlayerRunState.RelicRewardsAttempted"/>) and the reservations in
/// <see cref="ApRelicReceiptState"/>. Each step happens where the host's action order fixes it, so
/// every peer reaches the same result without a message:
/// <list type="bullet">
/// <item>a chest is decided at room entry, inside the move action;</item>
/// <item>an Elite's waiting receipts are reserved as its combat ends, between actions; its
/// rewards screen, built whenever each peer's UI gets there, takes them in order; the next map
/// move settles what it took.</item>
/// </list>
/// AP-menu relics are assigned by the queued relic assignment action (ApQueuedRelicClaims), which
/// spends the banks counted here.
/// </summary>
public static class RelicReceiptMultiplayer
{
    private const int MaxRelicRewards = ArchipelagoProgress._maxRelicRewards;

    public static string RoomKey(RunState run) => $"chest:{run.CurrentActIndex}:{run.CurrentMapCoord}";
    private static string EliteKey(RunState run) => $"elite:{run.CurrentActIndex}:{run.CurrentMapCoord}";
    public static ApRelicReceiptState State(RunState run) => ApRunData.GetSharedState(run).RelicReceipts;

    public static bool IsReserved(Player player, int index) =>
        MultiplayerSupport.IsRealMultiplayerRun && player.RunState is RunState run
        && State(run).Find(player.NetId, index) != null;

    public static bool CanUseMenu(Player player, int index) =>
        !MultiplayerSupport.IsRealMultiplayerRun || player.RunState is RunState run
        && State(run).CanUseMenu(player.NetId, index);

    // Reads of action-owned state, alike on every peer at an ordered point.

    private static bool TryGetApPlayer(Player player, out RunState run, out ApPlayerRunState state)
    {
        run = null!;
        state = null!;
        if (player.RunState is not RunState playerRun || !ApReceiptLedger.TryGetState(player, out state))
            return false;
        run = playerRun;
        return true;
    }

    private static int GetAttempted(ApPlayerRunState state) => state.RelicRewardsAttempted ?? 0;

    private static int GetAnytime(ApPlayerRunState state) =>
        Math.Clamp(state.RelicRewardsAvailableAnytime ?? 0, 0, MaxRelicRewards);

    // The player's Relic receipts for their run's character, in AP order.
    private static List<int> GetRelicReceipts(Player player, ApPlayerRunState state) =>
        ApReceiptLedger.CharacterReceipts(player, state, APItem.Relic).Select(receipt => receipt.Index).ToList();

    /// <summary>Whether this receipt is among the run's first anytime relics, which need no bank.</summary>
    internal static bool IsAnytimeReceipt(Player player, ApPlayerRunState state, int index) =>
        GetRelicReceipts(player, state).Take(GetAnytime(state)).Contains(index);

    /// <summary>Whether the ledger holds this receipt as a Relic for the player's character.</summary>
    internal static bool IsRelicReceipt(Player player, ApPlayerRunState state, int index) =>
        ApReceiptLedger.HasReceipt(player, state, index, APItem.Relic);

    /// <summary>Relic receipts past the anytime relics that nothing has taken yet, in AP order.</summary>
    private static IEnumerable<int> GetWaitingReceipts(RunState run, Player player, ApPlayerRunState state)
    {
        ApRelicReceiptState receipts = State(run);
        return GetRelicReceipts(player, state)
            .Skip(GetAnytime(state))
            .Where(index => receipts.Find(player.NetId, index) == null
                && state.QueuedClaimedReceiptIndexes.BinarySearch(index) < 0);
    }

    internal static int GetAvailableBanks(RunState run, Player player, ApPlayerRunState state) =>
        State(run).AvailableBanks(player.NetId, GetAttempted(state), MaxRelicRewards);

    /// <summary>
    /// The Relic receipts the owner's AP menu can offer: anytime relics and receipts with a menu
    /// assignment, not yet claimed, and waiting receipts up to the player's banked natural relic
    /// rewards. The relic assignment action checks the same rules on every peer.
    /// </summary>
    internal static IReadOnlyList<int> GetMenuCandidates(Player player)
    {
        if (!TryGetApPlayer(player, out RunState run, out ApPlayerRunState state))
            return Array.Empty<int>();
        ApRelicReceiptState receipts = State(run);
        List<int> relics = GetRelicReceipts(player, state);
        bool Unclaimed(int index) =>
            state.QueuedClaimedReceiptIndexes.BinarySearch(index) < 0
            && receipts.Find(player.NetId, index) is null or { Destination: ApRelicReceiptState.MenuDestination, Consumed: false };
        IEnumerable<int> anytime = relics.Take(GetAnytime(state));
        IEnumerable<int> assigned = relics.Where(index => receipts.CanUseMenu(player.NetId, index));
        IEnumerable<int> waiting = GetWaitingReceipts(run, player, state)
            .Take(Math.Max(0, GetAvailableBanks(run, player, state)));
        return anytime.Concat(assigned).Concat(waiting).Where(Unclaimed).Distinct().Order().ToList();
    }

    /// <summary>
    /// Every peer, at a run's launch: saves from before natural relic counts were action-owned
    /// start from the count the host saved in the player's progress.
    /// </summary>
    public static void BindRun(RunState run)
    {
        foreach (Player player in run.Players)
        {
            if (ApReceiptLedger.TryGetState(player, out ApPlayerRunState state) && state.RelicRewardsAttempted == null)
                ApRunData.RecordRelicRewardsAttempted(run, player.NetId, state.Progress.RelicRewardsAttempted);
        }
    }

    /// <summary>
    /// Every peer, at treasure room entry inside the move action: decides each player's chest. A
    /// gated chest (one of the player's first ten natural relic rewards) keeps its native relic
    /// only for a waiting receipt, which it takes right here; the chest counts as a natural relic
    /// reward whether or not it is opened, since the room cannot be left unopened.
    /// </summary>
    public static void DecideChest(RunState run)
    {
        string roomKey = RoomKey(run);
        if (State(run).Chests.ContainsKey(roomKey))
        {
            // Loading a save made in this room: its decision already counted.
            LogUtility.Info($"Treasure AP decision reused: room={roomKey}.");
            return;
        }

        var decision = new ApRelicReceiptState.ChestDecision { RoomKey = roomKey };
        foreach (Player player in run.Players)
        {
            bool apPlayer = TryGetApPlayer(player, out _, out ApPlayerRunState state);
            int number = (apPlayer ? GetAttempted(state) : 0) + 1;
            bool generates = Hook.ShouldGenerateTreasure(run, player);
            bool gated = generates && apPlayer
                && MultiplayerLocationChecks.TryGetCheckSettings(player, out _)
                && number <= MaxRelicRewards;
            decision.Candidates.Add(new ApRelicReceiptState.Candidate
            {
                PlayerNetId = player.NetId, GeneratesRelic = generates, ApGated = gated, RewardNumber = number,
                ReceiptIndex = gated ? GetWaitingReceipts(run, player, state).Select(index => (int?)index).FirstOrDefault() : null,
            });
            if (generates && apPlayer)
                ApRunData.RecordRelicRewardsAttempted(run, player.NetId, number);
        }

        ApRunData.ModifyRelicReceipts(run, receipts =>
        {
            receipts.Chests[roomKey] = decision;
            foreach (ApRelicReceiptState.Candidate candidate in decision.Candidates)
            {
                if (candidate.ReceiptIndex is not int index)
                    continue;
                receipts.TryReserve(candidate.PlayerNetId, index, roomKey, candidate.RewardNumber);
                receipts.Consume(candidate.PlayerNetId, index, roomKey);
            }
        });
        foreach (ApRelicReceiptState.Candidate candidate in decision.Candidates)
        {
            if (candidate.ReceiptIndex is int index)
                ApRunData.RecordQueuedReceiptClaim(run, candidate.PlayerNetId, index);
        }

        LogUtility.Info($"Treasure AP decision: room={roomKey}, " + string.Join(", ", decision.Candidates.Select(c =>
            $"player={c.PlayerNetId}/reward={c.RewardNumber}/keep={c.Keep}/receipt={c.ReceiptIndex?.ToString() ?? "none"}")));
        MirrorOwner(run);
    }

    public static ApRelicReceiptState.ChestDecision GetChest(RunState run) =>
        State(run).Chests.TryGetValue(RoomKey(run), out var decision) ? decision
            : throw new InvalidOperationException("Native chest picker started without an AP chest decision.");

    /// <summary>
    /// Every peer, as an Elite combat ends (between actions): reserves each player's waiting
    /// receipts for the relic rewards the rewards screen will build, one per reward (two with
    /// Black Star) and none past the player's tenth natural relic reward.
    /// </summary>
    public static void ReserveEliteReceipts(RunState run, CombatRoom room)
    {
        string key = EliteKey(run);
        if (room.RoomType != RoomType.Elite || State(run).Elites.ContainsKey(key))
            return;

        var decision = new ApRelicReceiptState.EliteDecision { RoomKey = key };
        foreach (Player player in run.Players)
        {
            if (!TryGetApPlayer(player, out _, out ApPlayerRunState state)
                || !MultiplayerLocationChecks.TryGetCheckSettings(player, out _))
            {
                continue;
            }
            int attempted = GetAttempted(state);
            int rewards = player.Relics.OfType<BlackStar>().Any() ? 2 : 1;
            decision.Players.Add(new ApRelicReceiptState.EliteSources
            {
                PlayerNetId = player.NetId,
                BaseAttempted = attempted,
                Reserved = GetWaitingReceipts(run, player, state)
                    .Take(Math.Max(0, Math.Min(rewards, MaxRelicRewards - attempted)))
                    .ToList(),
            });
        }

        ApRunData.ModifyRelicReceipts(run, receipts =>
        {
            receipts.Elites[key] = decision;
            foreach (ApRelicReceiptState.EliteSources sources in decision.Players)
            {
                for (int i = 0; i < sources.Reserved.Count; i++)
                    receipts.TryReserve(sources.PlayerNetId, sources.Reserved[i], key, sources.BaseAttempted + i + 1);
            }
        });
        LogUtility.Info($"Elite AP relic receipts reserved: room={key}, " + string.Join(", ", decision.Players.Select(p =>
            $"player={p.PlayerNetId}/attempted={p.BaseAttempted}/reserved=[{string.Join(",", p.Reserved)}]")));
    }

    /// <summary>
    /// The rewards screen, on each peer when its UI builds it: numbers this player's next Elite
    /// relic reward and says which reserved receipt keeps its native relic. Returns false past the
    /// tenth natural relic reward, which has no AP check. Its effects wait for the next map move,
    /// so nothing read before then depends on when this runs.
    /// </summary>
    public static bool TryTakeEliteReward(Player player, out int rewardNumber, out int? receiptIndex)
    {
        rewardNumber = 0;
        receiptIndex = null;
        if (!TryGetApPlayer(player, out RunState run, out ApPlayerRunState state))
            return false;

        string key = EliteKey(run);
        int number = 0;
        int? receipt = null;
        ApRunData.ModifyRelicReceipts(run, receipts =>
        {
            // An Elite whose combat end this peer did not see (a loaded save) reserved nothing.
            if (!receipts.Elites.TryGetValue(key, out var decision))
                receipts.Elites[key] = decision = new ApRelicReceiptState.EliteDecision { RoomKey = key };
            ApRelicReceiptState.EliteSources? sources = decision.Players.FirstOrDefault(p => p.PlayerNetId == player.NetId);
            if (sources == null)
            {
                sources = new ApRelicReceiptState.EliteSources { PlayerNetId = player.NetId, BaseAttempted = GetAttempted(state) };
                decision.Players.Add(sources);
            }
            int position = sources.Sources.Count;
            number = sources.BaseAttempted + position + 1;
            receipt = position < sources.Reserved.Count ? sources.Reserved[position] : null;
            sources.Sources.Add(receipt);
        });
        rewardNumber = number;
        receiptIndex = receipt;
        LogUtility.Info($"Elite AP relic reward: room={key}, player={player.NetId}, reward={number}, "
            + $"receipt={receipt?.ToString() ?? "none"}.");
        return number <= MaxRelicRewards;
    }

    /// <summary>
    /// Every peer, as the next map move starts: counts each Elite's relic rewards as natural relic
    /// rewards, consumes the receipts they kept and frees the reservations they did not use.
    /// </summary>
    public static void SettleElites(RunState run)
    {
        ApRelicReceiptState state = State(run);
        if (state.Elites.Count == 0)
            return;

        List<ApRelicReceiptState.EliteDecision> decisions = state.Elites.Values.ToList();
        foreach (ApRelicReceiptState.EliteDecision decision in decisions)
        {
            foreach (ApRelicReceiptState.EliteSources sources in decision.Players)
            {
                ApRunData.RecordRelicRewardsAttempted(run, sources.PlayerNetId, sources.BaseAttempted + sources.Sources.Count);
                HashSet<int> kept = sources.Sources.OfType<int>().ToHashSet();
                ApRunData.ModifyRelicReceipts(run, receipts =>
                {
                    foreach (int index in sources.Reserved)
                    {
                        if (kept.Contains(index))
                            receipts.Consume(sources.PlayerNetId, index, decision.RoomKey);
                        else
                            receipts.Release(sources.PlayerNetId, index);
                    }
                });
                foreach (int index in kept)
                    ApRunData.RecordQueuedReceiptClaim(run, sources.PlayerNetId, index);
            }
            LogUtility.Info($"Elite AP relic rewards settled: room={decision.RoomKey}, " + string.Join(", ",
                decision.Players.Select(p => $"player={p.PlayerNetId}/rewards={p.Sources.Count}"
                    + $"/kept=[{string.Join(",", p.Sources.OfType<int>())}]")));
        }
        ApRunData.ModifyRelicReceipts(run, receipts => receipts.Elites.Clear());
        MirrorOwner(run);
    }

    /// <summary>
    /// The local owner: copies its action-owned relic state into its own AP progress, which its
    /// Relic Coupons counter, menu badge and checkpoint read.
    /// </summary>
    internal static void MirrorOwner(RunState run, bool spentBank = false, bool publish = true)
    {
        if (!MultiplayerSupport.IsLocalOwnApSlot
            || LocalContext.GetMe(run) is not Player player
            || !ApReceiptLedger.TryGetState(player, out ApPlayerRunState state))
        {
            return;
        }

        ArchipelagoProgress progress = ArchipelagoClient.Progress;
        bool paired = false;
        foreach ((int index, ApRelicReceiptState.Claim claim) in
                 State(run).Claims.GetValueOrDefault(player.NetId) ?? new Dictionary<int, ApRelicReceiptState.Claim>())
        {
            if (claim.Destination == ApRelicReceiptState.MenuDestination || !claim.Consumed
                || !progress.Items.IsRegistered(index) || progress.Items.IsUsed(index))
            {
                continue;
            }
            progress.Items.MarkUsed(index);
            ApRunGraphRecorder.RecordItemClaim(index);
            paired = true;
        }
        progress.RelicRewardsAttempted = GetAttempted(state);
        progress.BankedRelicRewards = Math.Max(0, GetAvailableBanks(run, player, state));
        if (paired || spentBank)
            RelicCoupons.Activate(player);
        else
            RelicCoupons.RefreshCounter(player);
        if (publish)
            ApRunData.PublishLocalProgress(player);
    }
}
