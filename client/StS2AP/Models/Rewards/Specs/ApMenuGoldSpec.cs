namespace StS2AP.Models;

/// <summary>Serializable form of one condensed AP gold row in a native reward menu.</summary>
public sealed class ApMenuGoldSpec
{
    public int SourceAmount { get; set; }
    public int GrantedAmount { get; set; }
    public int RedeemedRawAfter { get; set; }

    public ApGoldClaim ToClaim() => new(SourceAmount, GrantedAmount, RedeemedRawAfter);
}
