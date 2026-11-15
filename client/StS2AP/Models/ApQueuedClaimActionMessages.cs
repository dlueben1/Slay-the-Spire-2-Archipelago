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
    // Which selection of that row this is, counted from 1. A claim that did not apply (a full
    // potion belt) leaves its row in the menu, and selecting it again requests a new action.
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

/// <summary>
/// One queued claim of an AP receipt: a potion rolled with AP RNG when the owner built the menu,
/// an assigned bonus wax relic, or a standard relic.
/// </summary>
public sealed class ApReceiptClaimActionMessage : ApQueuedClaimMessage
{
    public int ReceivedItemIndex { get; set; }
    // The reward in MegaCrit's save schema, exactly as the menu row offered it. Empty for a
    // standard relic, which every peer reads from its own assignment instead.
    public string SerializedModel { get; set; } = string.Empty;
}

/// <summary>
/// Asks every peer to assign relics to the owner's standard Relic receipts before the owner's AP
/// menu opens, so each peer draws them from the same relic bag at the same point in the run.
/// </summary>
public sealed class ApRelicAssignmentActionMessage : ApQueuedClaimMessage
{
    public List<ApRelicAssignmentRequest> Receipts { get; set; } = new();
}

public sealed class ApRelicAssignmentRequest
{
    public int ReceivedItemIndex { get; set; }
    // A receipt past the run's anytime relics spends one of the owner's banked natural relic
    // rewards, as the host's menu approval records it today.
    public bool RequiresBank { get; set; }
}
