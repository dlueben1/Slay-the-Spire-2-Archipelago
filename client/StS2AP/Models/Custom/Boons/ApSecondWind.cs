using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models.RelicPools;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves.Runs;
using STS2RitsuLib.Interop.AutoRegistration;
using STS2RitsuLib.Scaffolding.Content;

namespace StS2AP.Models;

/// <summary>
/// The Second Wind boon: once per run, the player survives a killing blow and heals 35% of max HP.
/// Works like the base game's Lizard Tail, whose art it borrows, so it also stops lethal damage
/// from an incoming DeathLink, which goes through the same death check. Once used, it stays greyed
/// out for the rest of the run. <see cref="ApRunModifiers"/> grants it at run start; the run save
/// keeps it from then on.
/// </summary>
[RegisterRelic(typeof(SharedRelicPool))]
public sealed class ApSecondWind : ModRelicTemplate, IApTintedRelic
{
    private const decimal HealShare = 0.35m;
    private static readonly string LizardTailIconPath = ImageHelper.GetImagePath("relics/lizard_tail.png");

    private bool _used;

    public Godot.Color IconTint => IApTintedRelic.BoonTint;

    // Granted by AP, never a reward.
    public override RelicRarity Rarity => RelicRarity.None;

    // Keeps relic-trading events from taking it, as Relic Coupons does.
    public override bool HasUponPickupEffect => true;

    public override RelicAssetProfile AssetProfile { get; } = new(
        IconPath: LizardTailIconPath,
        IconOutlinePath: LizardTailIconPath,
        BigIconPath: LizardTailIconPath
    );

#if STS2_AT_LEAST_0_113_0
    protected override bool IsAllowedInternal(IRunState runState) => false;
#else
    public override bool IsAllowed(IRunState runState) => false;
#endif

    public override bool IsUsedUp => Used;

    [SavedProperty]
    public bool Used
    {
        get => _used;
        set
        {
            AssertMutable();
            _used = value;
            Status = value ? RelicStatus.Disabled : RelicStatus.Normal;
        }
    }

    // The late check, as Lizard Tail uses, lets other death-preventing effects go first.
    public override bool ShouldDieLate(Creature creature) => creature != Owner.Creature || Used;

    public override async Task AfterPreventingDeath(Creature creature)
    {
        Flash();
        Used = true;
        await CreatureCmd.Heal(creature, Math.Max(1m, creature.MaxHp * HealShare));
    }
}
