using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models.RelicPools;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves.Runs;
using StS2AP.Utils;
using STS2RitsuLib.Interop.AutoRegistration;
using STS2RitsuLib.Scaffolding.Content;

namespace StS2AP.Models;

/// <summary>
/// The Wayfinder boon: three times per run, travel to any room in the next row, ignoring the
/// paths. Works like the base game's Winged Boots, whose art it borrows, and shares free moves with
/// it and Route Diagram, so one move spends one use (<see cref="FreeTravelUses"/>). The counter
/// shows the moves left. Singleplayer only, as Winged Boots is, because the map is shared in
/// multiplayer. <see cref="ApRunModifiers"/> grants it at run start; the run save keeps it from
/// then on.
/// </summary>
[RegisterRelic(typeof(SharedRelicPool))]
public sealed class ApWayfinder : ModRelicTemplate, IApTintedRelic
{
    private const int UsesPerRun = 3;
    private static readonly string WingedBootsIconPath = ImageHelper.GetImagePath("relics/winged_boots.png");

    private int _timesUsed;

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

    public override bool IsUsedUp => TimesUsed >= UsesPerRun;

    public override bool ShowCounter => !IsUsedUp;

    public override int DisplayAmount => UsesPerRun - TimesUsed;

    [SavedProperty]
    public int TimesUsed
    {
        get => _timesUsed;
        set
        {
            AssertMutable();
            _timesUsed = value;
            InvokeDisplayAmountChanged();
            Status = IsUsedUp ? RelicStatus.Disabled : RelicStatus.Normal;
        }
    }

    public override bool ShouldAllowFreeTravel() => !IsUsedUp;

    public override Task AfterRoomEntered(AbstractRoom room)
    {
        if (FreeTravelUses.ShouldSpend(this))
        {
            Flash();
            TimesUsed++;
        }
        return Task.CompletedTask;
    }
}
