namespace StS2AP.Persistence;

/// <summary>One atomic progressive-starter identity and tier update.</summary>
public sealed class ApProgressiveStarterState
{
    public string? BaseId { get; set; }
    public string? UpgradedId { get; set; }
    public ProgressiveStarterTier Tier { get; set; }
}
