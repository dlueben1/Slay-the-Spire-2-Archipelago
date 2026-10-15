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
}
