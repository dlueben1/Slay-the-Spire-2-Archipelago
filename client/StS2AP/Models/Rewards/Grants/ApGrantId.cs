namespace StS2AP.Models;

/// <summary>
/// Stable identity for one discrete Archipelago receipt. Received item indexes are only
/// unique inside an AP slot, so the slot must remain part of every durable assignment key.
/// </summary>
public readonly record struct ApGrantId(int ApSlotId, int ReceivedItemIndex)
{
    public override string ToString() => $"{ApSlotId}:{ReceivedItemIndex}";
}
