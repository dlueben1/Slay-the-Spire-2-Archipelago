using StS2AP.Persistence;

namespace StS2AP.Models;

/// <summary>
/// Owner-authored recipe for one ordered progressive-starter mutation. A live AP receipt may
/// contain several targets when multiple players share its AP source and character offset.
/// </summary>
public sealed class ApProgressiveStarterActionMessage
{
    public enum ActionReason
    {
        Initialization,
        LiveReceipt,
    }

    public enum StarterKind
    {
        Card,
        Relic,
    }

    public sealed class Target
    {
        public ulong PlayerNetId { get; set; }
        public StarterKind Kind { get; set; }
        public ProgressiveStarterTier TargetTier { get; set; }
        public ApProgressiveStarterKindState Specification { get; set; } = new();
    }

    public int SchemaVersion { get; set; } = 1;
    public Guid RunId { get; set; }
    public Guid ActionId { get; set; }
    public ulong OwnerNetId { get; set; }
    public int ApSlotId { get; set; }
    public int ReceivedItemIndex { get; set; }
    public long? CharacterOffset { get; set; }
    public ActionReason Reason { get; set; }
    public List<Target> Targets { get; set; } = new();
}
