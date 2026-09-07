namespace StS2AP.Models;


/// <summary>
/// Tracks the gold represented by an Archipelago card reward offer,
/// including any reduction caused by Poverty Ascension.
/// </summary>
public readonly record struct ArchipelagoGoldOffer(
    int SourceAmount,
    int GrantedAmount,
    int WithheldAmount,
    bool PovertyApplied)
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

    /// <summary>
    /// The amount of gold withheld from the player due to Poverty Ascension.
    /// This is retained in case the offer was prepared before the ascension down was received.
    /// Occurs when viewing AP reward menu while receiving the ascension down.
    /// </summary>
    public int WithheldAmount { get; init; } = WithheldAmount;

    /// <summary>
    /// Whether Poverty Ascension was applied to this offer.
    /// </summary>
    public bool PovertyApplied { get; init; } = PovertyApplied;
}