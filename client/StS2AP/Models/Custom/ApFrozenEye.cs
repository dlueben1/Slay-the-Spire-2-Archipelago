using Godot;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Models.RelicPools;
using MegaCrit.Sts2.Core.Runs;
using StS2AP.Utils.Combat;
using STS2RitsuLib.Interop.AutoRegistration;
using STS2RitsuLib.Scaffolding.Content;

namespace StS2AP.Models;

/// <summary>
/// The Pile Sight bonus. The draw pile screen lists cards in draw order, as Frozen Eye did in the
/// first game; <see cref="Patches.Patches_PileSightDrawOrder"/> does that part. The relic itself
/// rewards planning around the shuffle: the first shuffle each turn draws 1 more card next turn.
/// The card waits for next turn so that a shuffle can never pay for another shuffle in the same
/// turn. As with Relic Coupons, the relic localization keys must stay ARCHIPELAGO to match the
/// ModId.
/// </summary>
[RegisterRelic(typeof(SharedRelicPool))]
public sealed class ApFrozenEye : ModRelicTemplate
{
    // The game ships Frozen Eye's small sprite and outline in its relic atlases, though it has no
    // such relic, so the mod shows that art without carrying a copy. Glass Eye stands in if a game
    // update drops it.
    private const string FrozenEyeArt = "frozen_eye";
    private const int CardsPerShuffle = 1;
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

    // The fight and turn of the last shuffle that earned a card. Not saved: a reload restarts the
    // fight, and with it the turn count.
    private object? _rewardedCombat;
    private int _rewardedTurn;

    public override async Task AfterShuffle(PlayerChoiceContext choiceContext, Player shuffler)
    {
        if (shuffler != Owner || Owner.PlayerCombatState is not { } combatState)
            return;
        object? combat = Owner.Creature.CombatState;
        if (ReferenceEquals(combat, _rewardedCombat) && combatState.TurnNumber == _rewardedTurn)
            return;
        _rewardedCombat = combat;
        _rewardedTurn = combatState.TurnNumber;
        Flash();
        // The game's own next-turn draw, as Forewarned uses.
        await PowerCmd.Apply<DrawCardsNextTurnPower>(choiceContext, Owner.Creature, CardsPerShuffle, Owner.Creature, null);
    }

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
