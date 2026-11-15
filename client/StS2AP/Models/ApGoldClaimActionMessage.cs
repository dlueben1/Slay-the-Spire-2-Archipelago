namespace StS2AP.Models;

/// <summary>
/// Owner-authored payload of one queued AP gold claim. Every peer executes it in the native
/// action queue; the granted amount is computed there, not here.
/// </summary>
public sealed class ApGoldClaimActionMessage
{
    public Guid RunId { get; set; }
    // Also the gold row's identity in the replicated menu, so each peer can match the row's
    // selection to this action.
    public Guid ClaimId { get; set; }
    public ulong OwnerNetId { get; set; }
    public int ApSlotId { get; set; }
    // The owner's raw redeemed-gold cursor when the menu was built, and the raw gold redeemed.
    public int CursorBefore { get; set; }
    public int SourceAmount { get; set; }
}
