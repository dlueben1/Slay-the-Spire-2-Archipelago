namespace StS2AP.Persistence;

/// <summary>
/// A card reward offer that a queued card claim revealed and its owner has not resolved yet.
/// Reopening the reward offers the same cards, and Strict Card Rewards locks other card rewards
/// behind it.
/// </summary>
public sealed class ApQueuedCardOfferState
{
    // The offered cards in MegaCrit's save schema, after any reroll or card taken.
    public List<string> SerializedCards { get; set; } = new();
    public bool CanReroll { get; set; }
    public bool HasBeenRerolled { get; set; }
}
