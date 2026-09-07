namespace StS2AP.Models;

/// <summary>One host-authored, ordered Ascension Down transition.</summary>
public sealed class ApAscensionDownActionMessage
{
    public int SchemaVersion { get; set; } = 1;
    public Guid RunId { get; set; }
    public Guid ActionId { get; set; }
    public ulong OwnerNetId { get; set; }
    public int ApSlotId { get; set; }
    public int ReceivedItemIndex { get; set; }
    public long CharacterOffset { get; set; }
    public int AscensionLevel { get; set; }
}
