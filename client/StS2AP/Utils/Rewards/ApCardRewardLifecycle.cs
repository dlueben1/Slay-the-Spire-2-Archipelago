using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Runs;

namespace StS2AP.Utils;

/// <summary>Shared AP card lifecycle operations, using the publicized game API.</summary>
internal static class ApCardRewardLifecycle
{
    // Disable broad relic-pickup updates; generation hooks run once, Eggs refresh before selection.
    // Detaching also lets discarded menu rows be collected instead of retained by the player event.
    internal static void Freeze(CardReward reward) =>
        reward.Player.RelicObtained -= reward.OnRelicObtained;

    internal static void RefreshEggUpgrades(
        Player player, List<CardCreationResult> cards, CardCreationOptions options)
    {
        // This is not another generation pass: pool changes, Tress, Crucible, and other reward
        // hooks must not run again. Use the native Egg hooks for card-type/flag eligibility.
        // Only unupgraded choices need this refresh; reopening must not repeatedly upgrade a
        // modded card which supports more than one upgrade level.
        List<CardCreationResult> unupgraded = cards.Where(result => !result.Card.IsUpgraded).ToList();
        foreach (RelicModel relic in player.Relics)
        {
            if (relic is MoltenEgg or ToxicEgg or FrozenEgg)
                relic.TryModifyCardRewardOptionsLate(player, unupgraded, options);
        }
    }
}
