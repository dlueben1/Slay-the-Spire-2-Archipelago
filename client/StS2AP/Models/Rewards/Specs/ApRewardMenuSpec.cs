namespace StS2AP.Models;

/// <summary>
/// One immutable AP reward-menu snapshot, from which the owner builds the native reward screen.
/// </summary>
public sealed class ApRewardMenuSpec
{
    public Guid MenuId { get; set; } = Guid.NewGuid();
    public int ApSlotId { get; set; }
    public ulong OwnerNetId { get; set; }
    public ApMenuGoldSpec? Gold { get; set; }
    // The owner's Strict Card Rewards setting, which adds Discard to card pickers. A multiplayer
    // card claim carries it, so every peer builds the same picker alternatives.
    public bool StrictCardRewards { get; set; }
    public List<ApMirroredRewardSpec> Rewards { get; set; } = new();
}
