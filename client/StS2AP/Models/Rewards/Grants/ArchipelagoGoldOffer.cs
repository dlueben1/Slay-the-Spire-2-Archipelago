namespace StS2AP.Models;


/// <summary>
/// Raw AP gold and the amount to grant after applying Poverty.
/// </summary>
public readonly record struct ArchipelagoGoldOffer(
    int SourceAmount,
    int GrantedAmount)
{
    /// <summary>
    /// The raw amount of gold represented by the offer before any reductions.
    /// </summary>
    public int SourceAmount { get; init; } = SourceAmount;

    /// <summary>
    /// The amount of gold displayed and granted to the player.
    /// This may be lower than <see cref="SourceAmount"/> due to Poverty Ascension.
    /// </summary>
    public int GrantedAmount { get; init; } = GrantedAmount;
}
