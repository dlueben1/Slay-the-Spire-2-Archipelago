namespace StS2AP.Models;

/// <summary>
/// Immutable aggregate gold claim materialized by one AP reward-menu button.
/// The raw source and cursor are stored in the owner's host-carried AP progress;
/// only <see cref="GrantedAmount"/> is applied through MegaCrit's synchronizer.
/// <see cref="GainCount"/> is how many Combat, Elite and Boss Gold items the claim pays out, so
/// "after gaining gold" effects can fire once per item.
/// </summary>
public sealed record ApGoldClaim(
    int SourceAmount,
    int GrantedAmount,
    int RedeemedRawAfter,
    int GainCount = 0);
