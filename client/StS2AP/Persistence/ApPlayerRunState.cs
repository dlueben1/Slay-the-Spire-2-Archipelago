namespace StS2AP.Persistence;

/// <summary>
/// One player's run contribution, keyed by the MegaCrit Net ID retained across rejoin. Progress
/// is the owner's canonical view; Construction is the current process's independently advancing
/// reward-construction cursor and the fixed host's reconnect baseline.
/// </summary>
public sealed class ApPlayerRunState
{
    public int SchemaVersion { get; set; } = ApRunData.RunSchemaVersion;
    public ApParticipationKind Participation { get; set; } = ApParticipationKind.VanillaGuest;
    public string? ApRoomSeed { get; set; }
    public int? ApTeamId { get; set; }
    public int? ApSlotId { get; set; }
    public ArchipelagoSettings? SlotSettings { get; set; }
    public Dictionary<long, List<int>> InitialRelicReceiptIndexesByCharacter { get; set; } = new();
    public Dictionary<long, int> InitialProgressiveAncientsByCharacter { get; set; } = new();
    public bool ReceiptSourceReady { get; set; }
    public ApRunProgressState Progress { get; set; } = new();
    // Advanced independently by the native combat-end hook on every replica.
    public int CombatsSinceLastWaxMelt { get; set; }
    public ApReplicaConstructionState Construction { get; set; } = new();
    public long ProgressRevision { get; set; }
    public ApProgressiveStarterPlayerState ProgressiveStarters { get; set; } = new();
    // Advanced by buff actions on every replica; retained in the host's save for rejoin.
    public int LastConsumedBuffIndex { get; set; } = -1;
    public CombatEffectLimit CombatBuffLimit { get; set; } = new();
    // Trap receipts have their own cutoff: consuming one must never skip an earlier buff.
    public int LastConsumedTrapIndex { get; set; } = -1;
    public CombatEffectLimit CombatTrapLimit { get; set; } = new();
    // Raw AP gold redeemed through queued gold claims; advanced only by that action, on every
    // replica. Null until the first one. Progress.GoldRedeemed is the owner's published view.
    public int? QueuedGoldClaimCursor { get; set; }
    // AP receipts used up by queued claims, ascending. Written only by claim executors, on every
    // replica, so they can refuse a repeated claim alike; Progress.UsedItems is the owner's
    // published view and changes whenever a delta arrives.
    public List<int> QueuedClaimedReceiptIndexes { get; set; } = new();
    // Card offers revealed by queued card claims and not resolved yet, by receipt index. Written
    // only by that action, on every replica, instead of the owner-published CardAssignments.
    public Dictionary<int, ApQueuedCardOfferState> QueuedCardOffers { get; set; } = new();
    // The three serialized relics each Progressive Ancient receipt offers, drawn by the queued
    // relic assignment action on every replica. Kept after a claim: later draws exclude them.
    public Dictionary<int, List<string>> QueuedAncientChoices { get; set; } = new();
}
