namespace StS2AP.Persistence;

/// <summary>Card and relic starter recipes owned by one STS player.</summary>
public sealed class ApProgressiveStarterPlayerState
{
    public ApProgressiveStarterKindState Card { get; set; } = new();
    public ApProgressiveStarterKindState Relic { get; set; } = new();
}
