namespace StS2AP.Models;

/// <summary>
/// Immutable aggregate gold claim materialized by one AP reward-menu button.
/// The raw source and cursor are stored in the owner's host-carried AP progress;
/// only <see cref="GrantedAmount"/> is applied through MegaCrit's synchronizer.
/// </summary>
public sealed record ApGoldClaim(
    int SourceAmount,
    int GrantedAmount,
    int RedeemedRawAfter);
