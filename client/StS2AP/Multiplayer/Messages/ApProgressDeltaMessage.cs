namespace StS2AP.Multiplayer.Messages;

/// <summary>Carries one ordered mutation to a player's canonical progress.</summary>
public sealed class ApProgressDeltaMessage
{
    public Guid RunId { get; set; }
    public ulong OwnerNetId { get; set; }
    public long BaseRevision { get; set; }
    public long Revision { get; set; }
    public ApProgressDelta Delta { get; set; } = new();
}
