namespace StS2AP.Multiplayer.Messages;

/// <summary>Establishes a player's complete canonical progress baseline.</summary>
public sealed class ApProgressSnapshotMessage
{
    public Guid RunId { get; set; }
    public ulong OwnerNetId { get; set; }
    public long Revision { get; set; }
    public ApRunProgressState Progress { get; set; } = new();
}
