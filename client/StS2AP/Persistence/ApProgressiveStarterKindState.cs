namespace StS2AP.Persistence;

/// <summary>
/// Concrete, run-scoped recipe for one player's progressive starter kind. Receipt counts belong
/// to the governing AP slot; this state records how that progression materialized for this exact
/// MegaCrit player.
/// </summary>
public sealed class ApProgressiveStarterKindState
{
    public bool Initialized { get; set; }
    public bool Supported { get; set; }
    public string? BaseId { get; set; }
    public string? UpgradedId { get; set; }
    public string? SerializedBaseModel { get; set; }
    public string? SerializedUpgradeRelic { get; set; }
    public ProgressiveStarterTier AppliedTier { get; set; } =
        ProgressiveStarterTier.Unsupported;
}
