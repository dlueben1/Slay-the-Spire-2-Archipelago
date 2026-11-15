namespace StS2AP.Models;

/// <summary>Serializable form of one condensed AP gold row in a native reward menu.</summary>
public sealed class ApMenuGoldSpec
{
    public int SourceAmount { get; set; }
    public int GrantedAmount { get; set; }
    public int RedeemedRawAfter { get; set; }
    // Set when the owner claims this gold as a queued action (ApQueuedClaims); every peer's
    // row then waits for that action instead of applying the gold itself.
    public Guid? QueuedClaimId { get; set; }

    public ApGoldClaim ToClaim() => new(SourceAmount, GrantedAmount, RedeemedRawAfter);
}
