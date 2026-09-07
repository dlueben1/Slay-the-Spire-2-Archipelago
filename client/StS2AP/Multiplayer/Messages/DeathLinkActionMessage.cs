namespace StS2AP.Multiplayer.Messages;

/// <summary>
/// Host-authored HP recipe carried by the native multiplayer action queue. Combat and non-combat
/// descriptors share this payload but admit it only at their corresponding safe boundary.
/// </summary>
public sealed class DeathLinkActionMessage
{
    public sealed class TargetPlan
    {
        public ulong NetId { get; set; }
        public int NewHp { get; set; }
    }

    public int SchemaVersion { get; set; } = 3;
    public Guid RunId { get; set; }
    public Guid EventId { get; set; }
    public ulong SlotOwnerNetId { get; set; }
    public int DamagePercent { get; set; }
    public string Source { get; set; } = string.Empty;
    public string? Cause { get; set; }
    public List<TargetPlan> Targets { get; set; } = new();
}
