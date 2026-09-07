namespace StS2AP.Persistence;

/// <summary>
/// Host-approved destinations for standard Relic receipts. Player Net IDs deliberately remain
/// part of the key: players can connect to the same AP slot but each has their own rewards.
/// </summary>
public sealed class ApRelicReceiptState
{
    public const string MenuDestination = "menu";
    public Dictionary<ulong, Dictionary<int, Claim>> Claims { get; set; } = new();
    public Dictionary<string, ChestDecision> Chests { get; set; } = new();

    public sealed class Claim
    {
        public string Destination { get; set; } = string.Empty;
        public bool Consumed { get; set; }
        public int RewardNumber { get; set; }
        public bool RequiresBank { get; set; }
        public string? MenuRelicAssignment { get; set; }
    }

    public sealed class ChestDecision
    {
        public string RoomKey { get; set; } = string.Empty;
        public List<Candidate> Candidates { get; set; } = new();
        public List<string>? NativeRelicIds { get; set; }
        public HashSet<ulong> SettledPlayers { get; set; } = new();
    }

    public sealed class Candidate
    {
        public ulong PlayerNetId { get; set; }
        public bool GeneratesRelic { get; set; }
        public bool ApGated { get; set; }
        public int RewardNumber { get; set; }
        public int? ReceiptIndex { get; set; }
        public bool Keep => GeneratesRelic && (!ApGated || ReceiptIndex.HasValue);
    }

    public Claim? Find(ulong player, int index) =>
        Claims.TryGetValue(player, out var claims) ? claims.GetValueOrDefault(index) : null;

    public bool CanUseMenu(ulong player, int index) =>
        Find(player, index) is { Destination: MenuDestination, Consumed: false };

    public bool TryReserve(ulong player, int index, string destination, int rewardNumber = 0, bool requiresBank = false)
    {
        if (Find(player, index) is Claim existing)
            return existing.Destination == destination && !existing.Consumed;
        if (!Claims.TryGetValue(player, out var claims))
            Claims[player] = claims = new();
        claims.Add(index, new Claim
        {
            Destination = destination, RewardNumber = rewardNumber,
            RequiresBank = requiresBank || destination != MenuDestination,
        });
        return true;
    }

    public void AddChest(ChestDecision decision)
    {
        if (Chests.TryGetValue(decision.RoomKey, out var existing))
        {
            if (!existing.Candidates.Select(Key).SequenceEqual(decision.Candidates.Select(Key)))
                throw new InvalidOperationException($"Conflicting chest decision {decision.RoomKey}.");
            if (existing.NativeRelicIds != null && decision.NativeRelicIds != null
                && !existing.NativeRelicIds.SequenceEqual(decision.NativeRelicIds))
                throw new InvalidOperationException($"Conflicting native relics for {decision.RoomKey}.");
            existing.NativeRelicIds ??= decision.NativeRelicIds;
            return;
        }
        // Validate the complete decision before changing any reservation.
        foreach (var candidate in decision.Candidates.Where(c => c.ReceiptIndex.HasValue))
        {
            var claim = Find(candidate.PlayerNetId, candidate.ReceiptIndex!.Value);
            if (claim != null && claim.Destination != decision.RoomKey)
                throw new InvalidOperationException("Chest receipt already belongs to another reward.");
        }
        foreach (var candidate in decision.Candidates.Where(c => c.ReceiptIndex.HasValue))
            TryReserve(candidate.PlayerNetId, candidate.ReceiptIndex!.Value,
                decision.RoomKey, candidate.RewardNumber);
        Chests.Add(decision.RoomKey, decision);
    }

    public void Consume(ulong player, int index, string destination)
    {
        var claim = Find(player, index);
        if (claim == null || claim.Destination != destination)
            throw new InvalidOperationException($"Relic receipt {player}:{index} has no matching reservation.");
        claim.Consumed = true;
    }

    public void AssignMenu(ulong player, int index, string serializedRelic)
    {
        var claim = Find(player, index);
        if (!CanUseMenu(player, index))
            throw new InvalidOperationException("Cannot assign a relic without its host reservation.");
        if (claim!.MenuRelicAssignment != null && claim.MenuRelicAssignment != serializedRelic)
            throw new InvalidOperationException("A reserved relic assignment cannot be rerolled.");
        claim.MenuRelicAssignment = serializedRelic;
    }

    public List<int> ApproveMenu(ulong player, IEnumerable<int> requested,
        IReadOnlyList<int> catalog, ApRunProgressState progress)
    {
        int anytimeCount = Math.Clamp(progress.RelicRewardsAvailableAnytimeForRun, 0, 10);
        ReconcileProgress(player, progress, catalog.Skip(anytimeCount));
        var anytime = catalog.Take(anytimeCount).ToHashSet();
        int outstanding = Claims.GetValueOrDefault(player)?.Count(kv =>
            kv.Value.Destination == MenuDestination && kv.Value.RequiresBank && !kv.Value.Consumed
            && !progress.UsedItems.Contains(kv.Key) && !progress.RelicChoiceAssignments.ContainsKey(kv.Key)) ?? 0;
        int banks = Math.Max(0, progress.BankedRelicRewards - outstanding);
        var approved = new List<int>();
        foreach (int index in requested.Distinct())
        {
            if (!catalog.Contains(index) || progress.UsedItems.Contains(index)) continue;
            bool requiresBank = !anytime.Contains(index);
            bool newBank = requiresBank && Find(player, index) == null
                && !progress.RelicChoiceAssignments.ContainsKey(index);
            if (newBank && banks <= 0) continue;
            if (!TryReserve(player, index, MenuDestination, requiresBank: requiresBank)) continue;
            if (newBank) banks--;
            approved.Add(index);
        }
        return approved;
    }

    /// <summary>
    /// Owner progress can arrive after native construction or selection. Preserve completed
    /// chest sources and exact menu assignments independently of that snapshot's age.
    /// </summary>
    public void ReconcileProgress(ulong player, ApRunProgressState progress, IEnumerable<int> gatedIndexes)
    {
        foreach (var chest in Chests.Values.Where(c => c.SettledPlayers.Contains(player)))
            progress.RelicRewardsAttempted = Math.Max(progress.RelicRewardsAttempted,
                chest.Candidates.Single(c => c.PlayerNetId == player).RewardNumber);
        var gated = gatedIndexes.ToHashSet();
        if (Claims.TryGetValue(player, out var claims))
        {
            foreach (var (index, claim) in claims)
            {
                if (claim.RequiresBank) gated.Add(index);
                if (claim.Consumed)
                {
                    if (!progress.UsedItems.Contains(index)) progress.UsedItems.Add(index);
                    progress.RelicChoiceAssignments.Remove(index);
                }
                else if (claim.MenuRelicAssignment != null && !progress.UsedItems.Contains(index))
                    progress.RelicChoiceAssignments[index] = [claim.MenuRelicAssignment];
            }
        }
        int paired = gated.Count(index => progress.UsedItems.Contains(index)
            || progress.RelicChoiceAssignments.ContainsKey(index));
        progress.BankedRelicRewards = Math.Max(0, Math.Min(progress.RelicRewardsAttempted, 10) - paired);
    }

    private static (ulong, bool, bool, int, int?) Key(Candidate c) =>
        (c.PlayerNetId, c.GeneratesRelic, c.ApGated, c.RewardNumber, c.ReceiptIndex);
}
