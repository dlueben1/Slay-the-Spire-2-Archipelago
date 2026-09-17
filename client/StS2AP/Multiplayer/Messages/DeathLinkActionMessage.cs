namespace StS2AP.Multiplayer.Messages;

/// <summary>
/// AP-owner-authored damage recipe carried by the native multiplayer combat action queue.
/// </summary>
public sealed class DeathLinkActionMessage
{
    public sealed class TargetPlan
    {
        public ulong NetId { get; set; }
        public int Damage { get; set; }
    }

    public Guid RunId { get; set; }
    public Guid EventId { get; set; }
    public ulong SlotOwnerNetId { get; set; }
    public int DamagePercent { get; set; }
    public string Source { get; set; } = string.Empty;
    public string? Cause { get; set; }
    public List<TargetPlan> Targets { get; set; } = new();
}
