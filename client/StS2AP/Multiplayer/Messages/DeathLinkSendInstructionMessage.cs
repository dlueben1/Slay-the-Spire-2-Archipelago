namespace StS2AP.Multiplayer.Messages;

/// <summary>
/// Host authorization for a non-host own-slot process to send one DeathLink through its AP
/// connection. Delivery is best effort and is never retried.
/// </summary>
public sealed class DeathLinkSendInstructionMessage
{
    public int SchemaVersion { get; set; } = 3;
    public Guid RunId { get; set; }
    public Guid EventId { get; set; }
    public ulong OwnerNetId { get; set; }
    public string CharacterName { get; set; } = string.Empty;
    public string FloorCause { get; set; } = string.Empty;
}
