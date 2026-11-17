using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models.RelicPools;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves.Runs;
using STS2RitsuLib.Interop.AutoRegistration;
using STS2RitsuLib.Scaffolding.Content;

namespace StS2AP.Models;

/// <summary>
/// The Wayfinder boon: three times per act, travel to any room in the next row, ignoring the
/// paths. Works like the base game's Winged Boots, whose art it borrows, with its uses refilled at
/// the start of each act. The counter shows the moves left this act. Singleplayer only, as Winged
/// Boots is, because the map is shared in multiplayer. <see cref="ApRunModifiers"/> grants it at
/// run start; the run save keeps it from then on.
/// </summary>
[RegisterRelic(typeof(SharedRelicPool))]
public sealed class ApWayfinder : ModRelicTemplate, IApTintedRelic
{
    private const int UsesPerAct = 3;
    private static readonly string WingedBootsIconPath = ImageHelper.GetImagePath("relics/winged_boots.png");

    private int _usesThisAct;

    public Godot.Color IconTint => IApTintedRelic.BoonTint;

    // Granted by AP, never a reward.
    public override RelicRarity Rarity => RelicRarity.None;

    // Keeps relic-trading events from taking it, as Relic Coupons does.
    public override bool HasUponPickupEffect => true;

    public override RelicAssetProfile AssetProfile { get; } = new(
        IconPath: WingedBootsIconPath,
        IconOutlinePath: WingedBootsIconPath,
        BigIconPath: WingedBootsIconPath
    );

#if STS2_AT_LEAST_0_113_0
    protected override bool IsAllowedInternal(IRunState runState) => false;
#else
    public override bool IsAllowed(IRunState runState) => false;
#endif

    public override bool IsUsedUp => UsesThisAct >= UsesPerAct;

    public override bool ShowCounter => !IsUsedUp;

    public override int DisplayAmount => UsesPerAct - UsesThisAct;

    [SavedProperty]
    public int UsesThisAct
    {
        get => _usesThisAct;
        set
        {
            AssertMutable();
            _usesThisAct = value;
            InvokeDisplayAmountChanged();
            // Unlike Winged Boots, a spent Wayfinder comes back, so the status goes both ways.
            Status = IsUsedUp ? RelicStatus.Disabled : RelicStatus.Normal;
        }
    }

    public override bool ShouldAllowFreeTravel() => !IsUsedUp;

    public override Task AfterActEntered()
    {
        if (UsesThisAct != 0)
            UsesThisAct = 0;
        return Task.CompletedTask;
    }

    // As Winged Boots does: a use is spent when the room just entered was not one of the previous
    // room's children, which only free travel allows.
    public override Task AfterRoomEntered(AbstractRoom room)
    {
        if (IsUsedUp || Owner.RunState is not RunState runState)
            return Task.CompletedTask;
        if (runState.CurrentRoomCount > 1 || runState.VisitedMapCoords.Count <= 1)
            return Task.CompletedTask;

        IReadOnlyList<MapCoord> visited = runState.VisitedMapCoords;
        MapPoint? previous = runState.Map.GetPoint(visited[visited.Count - 2]);
        MapPoint? current = runState.CurrentMapPoint;
        if (previous != null && current != null && !previous.Children.Contains(current))
        {
            Flash();
            UsesThisAct++;
        }
        return Task.CompletedTask;
    }
}
