namespace StS2AP.Persistence;

/// <summary>Host-authored facts embedded in the canonical MegaCrit run snapshot.</summary>
public sealed class ApRunSharedState
{
    public int SchemaVersion { get; set; } = ApRunData.RunSchemaVersion;
    public Guid RunId { get; set; }
    public ArchipelagoSettings? HostSettings { get; set; }
    public bool AscensionStateInitialized { get; set; }
    public long? HostCharacterOffset { get; set; }
    public List<int> ConfiguredAscensions { get; set; } = new();
    public List<int> CurrentAscensions { get; set; } = new();
    public List<int> HandledAscensionDownReceiptIndexes { get; set; } = new();
    public ApRelicReceiptState RelicReceipts { get; set; } = new();
}
