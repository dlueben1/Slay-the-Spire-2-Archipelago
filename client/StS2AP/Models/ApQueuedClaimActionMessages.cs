namespace StS2AP.Models;

/// <summary>
/// What every queued AP claim carries, whatever it claims. The owner writes it; every peer checks
/// it against replicated run state when it executes the claim in queue order.
/// </summary>
public abstract class ApQueuedClaimMessage
{
    public Guid RunId { get; set; }
    // Also the claim's row in the replicated menu, so each peer can match the row's selection
    // to this action.
    public Guid ClaimId { get; set; }
    // Which selection of that row this is, counted from 1. A claim that did not apply leaves its
    // row in the menu, and selecting it again requests a new action.
    public int Attempt { get; set; }
    public ulong OwnerNetId { get; set; }
    public int ApSlotId { get; set; }
}

/// <summary>One queued AP gold claim. The granted amount is computed at execution, not here.</summary>
public sealed class ApGoldClaimActionMessage : ApQueuedClaimMessage
{
    // The owner's raw redeemed-gold cursor when the menu was built, and the raw gold redeemed.
    public int CursorBefore { get; set; }
    public int SourceAmount { get; set; }
}
