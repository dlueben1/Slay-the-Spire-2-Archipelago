namespace StS2AP.Persistence;

/// <summary>
/// Where each player's standard Relic receipts went: a chest or an Elite's relic reward (natural
/// relic sources, decided on every peer at ordered points), or the AP menu (assigned by the queued
/// relic assignment action). Player Net IDs deliberately remain part of the key: players can
/// connect to the same AP slot but each has their own rewards.
/// </summary>
public sealed class ApRelicReceiptState
{
    public const string MenuDestination = "menu";
    public Dictionary<ulong, Dictionary<int, Claim>> Claims { get; set; } = new();
    public Dictionary<string, ChestDecision> Chests { get; set; } = new();
    // Elite combats whose relic rewards have not settled yet, by room key.
    public Dictionary<string, EliteDecision> Elites { get; set; } = new();

    public sealed class Claim
    {
        public string Destination { get; set; } = string.Empty;
        // A natural source's claim is consumed once it settles; a menu claim once it is claimed.
        public bool Consumed { get; set; }
        public bool RequiresBank { get; set; }
        public string? MenuRelicAssignment { get; set; }
    }

    public sealed class ChestDecision
    {
        public string RoomKey { get; set; } = string.Empty;
        public List<Candidate> Candidates { get; set; } = new();
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

    /// <summary>
    /// One Elite combat's relic rewards. Receipts are reserved when the combat ends; the rewards
    /// screen, built when each peer's UI gets there, takes them in order; the next map move settles
    /// what it took.
    /// </summary>
    public sealed class EliteDecision
    {
        public string RoomKey { get; set; } = string.Empty;
        public List<EliteSources> Players { get; set; } = new();
    }

    public sealed class EliteSources
    {
        public ulong PlayerNetId { get; set; }
        // The player's natural relic count when the combat ended; its rewards are numbered after it.
        public int BaseAttempted { get; set; }
        public List<int> Reserved { get; set; } = new();
        // One entry per relic reward the rewards screen built: the receipt it kept, or null.
        public List<int?> Sources { get; set; } = new();
    }

    public Claim? Find(ulong player, int index) =>
        Claims.TryGetValue(player, out var claims) ? claims.GetValueOrDefault(index) : null;

    public bool CanUseMenu(ulong player, int index) =>
        Find(player, index) is { Destination: MenuDestination, Consumed: false };

    public bool TryReserve(ulong player, int index, string destination, bool requiresBank = false)
    {
        if (Find(player, index) is Claim existing)
            return existing.Destination == destination && !existing.Consumed;
        if (!Claims.TryGetValue(player, out var claims))
            Claims[player] = claims = new();
        claims.Add(index, new Claim
        {
            Destination = destination,
            RequiresBank = requiresBank || destination != MenuDestination,
        });
        return true;
    }

    /// <summary>Drops a natural source's reservation that its rewards did not use.</summary>
    public void Release(ulong player, int index)
    {
        if (Find(player, index) is { Destination: not MenuDestination, Consumed: false })
            Claims[player].Remove(index);
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
            throw new InvalidOperationException("Cannot assign a relic without its menu reservation.");
        if (claim!.MenuRelicAssignment != null && claim.MenuRelicAssignment != serializedRelic)
            throw new InvalidOperationException("A reserved relic assignment cannot be rerolled.");
        claim.MenuRelicAssignment = serializedRelic;
    }

    /// <summary>
    /// Banked natural relic rewards: each of the first ten natural sources earns one, a receipt a
    /// natural source kept spends its own, and a menu receipt past the anytime relics spends one.
    /// </summary>
    public int AvailableBanks(ulong player, int relicRewardsAttempted, int maxRelicRewards)
    {
        int spent = Claims.TryGetValue(player, out var claims)
            ? claims.Values.Count(claim => claim.Destination == MenuDestination
                ? claim.RequiresBank
                : claim.Consumed)
            : 0;
        return Math.Min(relicRewardsAttempted, maxRelicRewards) - spent;
    }
}
