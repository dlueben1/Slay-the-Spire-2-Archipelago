namespace StS2AP.Models;

/// <summary>
/// Compact before/after form of a reviewed persistent card-reward hook effect. Absolute values
/// make application idempotent across menu reopening, save restoration, and reconnect.
/// </summary>
public sealed class ApRewardEffectSpec
{
    public string EffectId { get; set; } = string.Empty;
    public int BeforeValue { get; set; }
    public int AfterValue { get; set; }
}
