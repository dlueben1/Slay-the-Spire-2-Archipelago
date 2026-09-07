namespace StS2AP.Models;

/// <summary>
/// One immutable AP reward-menu snapshot. Every multiplayer replica builds the same ordered
/// native RewardsSet before MegaCrit begins synchronizing selections for its owner.
/// </summary>
public sealed class ApRewardMenuSpec
{
    public int SchemaVersion { get; set; } = 5;
    public Guid RunId { get; set; }
    public Guid MenuId { get; set; } = Guid.NewGuid();
    public int ApSlotId { get; set; }
    public ulong OwnerNetId { get; set; }
    public ApMenuGoldSpec? Gold { get; set; }
    public List<ApMirroredRewardSpec> Rewards { get; set; } = new();
}
