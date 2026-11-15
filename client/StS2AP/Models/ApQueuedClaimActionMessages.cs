namespace StS2AP.Models;

/// <summary>
/// What every queued AP claim carries, whatever it claims. The owner writes it; every peer checks
/// it against replicated run state when it executes the claim in queue order.
/// </summary>
public abstract class ApQueuedClaimMessage
{
    public Guid RunId { get; set; }
    // New for each request, so the owner can match the action to its waiting row and every
    // peer's log names the same claim.
    public Guid ClaimId { get; set; }
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
/// Asks every peer to assign relics to the owner's standard Relic receipts, and Ancient choices to
/// their Progressive Ancient receipts, before the owner's AP menu opens. Each peer draws them at
/// the same point in the run: a relic leaves the relic bag, and preparing an Ancient choice can
/// roll the player's reward RNG (Dusty Tome).
/// </summary>
public sealed class ApRelicAssignmentActionMessage : ApQueuedClaimMessage
{
    public List<ApRelicAssignmentRequest> Receipts { get; set; } = new();
    public List<ApAncientAssignmentRequest> Ancients { get; set; } = new();
}

public sealed class ApRelicAssignmentRequest
{
    public int ReceivedItemIndex { get; set; }
    // A receipt past the run's anytime relics spends one of the owner's banked natural relic
    // rewards, as the host's menu approval records it today.
    public bool RequiresBank { get; set; }
}

/// <summary>
/// The owner's recipe for one Progressive Ancient's three choices, as
/// <c>ArchipelagoProgress.TryGetAncientDrawRequest</c> works it out from the owner's AP receipts
/// and settings. Every peer draws the choices from it.
/// </summary>
public sealed class ApAncientAssignmentRequest
{
    public int ReceivedItemIndex { get; set; }
    public string ChoiceKey { get; set; } = string.Empty;
    // The act whose Ancients the choices come from, and the pool act (null for True Chaos).
    public int AncientActIndex { get; set; }
    public int? PoolActIndex { get; set; }
    // Balanced pools draw from a single Ancient of that act.
    public bool Balanced { get; set; }
}

/// <summary>One queued Ancient claim: the receipt and which of its three assigned relics was picked.</summary>
public sealed class ApAncientClaimActionMessage : ApQueuedClaimMessage
{
    public int ReceivedItemIndex { get; set; }
    public int OptionIndex { get; set; }
}

/// <summary>
/// AP items the owner received that the receipt ledger may not have yet. Every peer appends the
/// ones it lacks, by index, so a repeat (AP replays the whole history on every connection) is
/// harmless. It also carries the other AP-server facts that construction reads: the run's anytime
/// relics, and campfire checks the server has but no rest site recorded (a collect, another run).
/// </summary>
public sealed class ApReceiptsArrivedActionMessage : ApQueuedClaimMessage
{
    public List<ApLedgerReceipt> Receipts { get; set; } = new();
    // The owner's anytime relic count for this run; only the first value a peer records counts.
    public int? RelicRewardsAvailableAnytime { get; set; }
    public List<long> CheckedCampfireLocationIds { get; set; } = new();
}

/// <summary>
/// One queued card reward claim. Every peer generates or reloads the offer in the action and
/// waits there for the owner's pick.
/// </summary>
public sealed class ApCardClaimActionMessage : ApQueuedClaimMessage
{
    public int ReceivedItemIndex { get; set; }
    public bool IsRare { get; set; }
    // The act whose upgrade odds a regular reward uses; null for a rare one or the current act.
    public int? ActIndex { get; set; }
    // The owner's Strict Card Rewards setting, which adds Discard to the picker on every peer.
    public bool StrictCardRewards { get; set; }
}
