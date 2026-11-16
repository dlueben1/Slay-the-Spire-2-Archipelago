namespace StS2AP.Multiplayer.Messages;

/// <summary>A player's complete AP progress, sent to the host as that player's checkpoint.</summary>
public sealed class ApProgressCheckpointMessage
{
    public Guid RunId { get; set; }
    public ulong OwnerNetId { get; set; }
    public ApRunProgressState Progress { get; set; } = new();
}
