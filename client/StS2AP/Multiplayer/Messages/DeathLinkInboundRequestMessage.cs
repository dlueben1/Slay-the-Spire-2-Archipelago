namespace StS2AP.Multiplayer.Messages;

/// <summary>
/// One external DeathLink received by an own-slot AP connection and relayed unchanged to the
/// native host. The host derives damage and targets from the frozen run state.
/// </summary>
public sealed class DeathLinkInboundRequestMessage
{
    public int SchemaVersion { get; set; } = 3;
    public Guid RunId { get; set; }
    public Guid EventId { get; set; }
    public ulong OwnerNetId { get; set; }
    public long TimestampTicks { get; set; }
    public string Source { get; set; } = string.Empty;
    public string? Cause { get; set; }
}
