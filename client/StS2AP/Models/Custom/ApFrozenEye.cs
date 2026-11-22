using Godot;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models.RelicPools;
using MegaCrit.Sts2.Core.Runs;
using StS2AP.Utils.Combat;
using STS2RitsuLib.Interop.AutoRegistration;
using STS2RitsuLib.Scaffolding.Content;

namespace StS2AP.Models;

/// <summary>
/// Shows that the Pile Sight bonus is active: the draw pile screen lists cards in draw order, as
/// Frozen Eye did in the first game. The relic does nothing itself;
/// <see cref="Patches.Patches_PileSightDrawOrder"/> does the work. As with Relic Coupons, the
/// relic localization keys must stay ARCHIPELAGO to match the ModId.
/// </summary>
[RegisterRelic(typeof(SharedRelicPool))]
public sealed class ApFrozenEye : ModRelicTemplate
{
    // The game ships Frozen Eye's small sprite and outline in its relic atlases, though it has no
    // such relic, so the mod shows that art without carrying a copy. Glass Eye stands in if a game
    // update drops it.
    private const string FrozenEyeArt = "frozen_eye";
    private const string StandInArt = "glass_eye";

    private static readonly Lazy<string> ArtName = new(() =>
        ResourceLoader.Exists(ImageHelper.GetImagePath($"atlases/relic_atlas.sprites/{FrozenEyeArt}.tres"))
            ? FrozenEyeArt
            : StandInArt);

    // Naming the game's own art, with no asset profile, leaves the game's atlas loading in charge.
    // RitsuLib's custom icon paths do not accept atlas sprites (see Aged Cheese).
    protected override string IconBaseName => ArtName.Value;

    // Frozen Eye has no large picture, so the inspect view enlarges the small sprite.
    protected override string BigIconPath =>
        ResourceLoader.Exists(base.BigIconPath) ? base.BigIconPath : PackedIconPath;

    // Granted by AP, never a reward, so it has no rarity Touch of Orobas could pick up.
    public override RelicRarity Rarity => RelicRarity.None;

    // Keeps relic-trading events from taking it, as Relic Coupons does. A traded-away Frozen Eye
    // would come back on the next load.
    public override bool HasUponPickupEffect => true;

    // AP grants this relic explicitly; it must never enter a natural reward pool.
#if STS2_AT_LEAST_0_113_0
    protected override bool IsAllowedInternal(IRunState runState) => false;
#else
    public override bool IsAllowed(IRunState runState) => false;
#endif

    /// <summary>
    /// Adds the relic when the player has the Pile Sight bonus and lacks it. Singleplayer only: the
    /// bonus itself also works in multiplayer, but there each peer simulates every player's
    /// relics and has no way to know another slot's Sight items.
    /// </summary>
    public static void EnsureOwnedIfEarned(Player? player, bool silent = false)
    {
        if (player == null || MultiplayerSupport.IsMultiplayerScope || !PileSight.ShowsDrawOrder)
            return;
        SightBonusRelic.EnsureOwned<ApFrozenEye>(player, silent);
    }
}
