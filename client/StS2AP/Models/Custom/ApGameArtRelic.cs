using Godot;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Runs;
using STS2RitsuLib.Scaffolding.Content;

namespace StS2AP.Models;

/// <summary>
/// An AP relic drawn with art the game ships in its relic atlases but gives to no relic of its
/// own, such as the first game's Ink Bottle or Torii. AP grants these relics itself, so they are
/// never rewards. As with Relic Coupons, the relic localization keys must stay ARCHIPELAGO to
/// match the ModId.
/// </summary>
public abstract class ApGameArtRelic : ModRelicTemplate
{
    /// <summary>The sprite's name in the game's relic atlases, such as "ink_bottle".</summary>
    protected abstract string ArtName { get; }

    // Naming the game's own art, with no asset profile, leaves the game's atlas loading in charge.
    // RitsuLib's custom icon paths do not accept atlas sprites (see Aged Cheese).
    protected override string IconBaseName => ArtName;

    // Most of these sprites have no large picture, so the inspect view enlarges the small one.
    protected override string BigIconPath =>
        ResourceLoader.Exists(base.BigIconPath) ? base.BigIconPath : PackedIconPath;

    // Granted by AP, never a reward, so it has no rarity Touch of Orobas could pick up.
    public override RelicRarity Rarity => RelicRarity.None;

    // Keeps relic-trading events from taking it, as Relic Coupons does.
    public override bool HasUponPickupEffect => true;

    // AP grants these relics explicitly; they must never enter a natural reward pool.
#if STS2_AT_LEAST_0_113_0
    protected override bool IsAllowedInternal(IRunState runState) => false;
#else
    public override bool IsAllowed(IRunState runState) => false;
#endif
}
