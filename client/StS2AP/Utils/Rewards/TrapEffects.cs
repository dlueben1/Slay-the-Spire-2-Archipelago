using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Powers;
using static StS2AP.Data.ItemTable;

namespace StS2AP.Utils;

internal static class TrapEffects
{
    public static async Task<bool> Apply(APItem item, Player player, PlayerChoiceContext context)
    {
        Task effect = item switch
        {
            APItem.WeakTrap => ApplyTimed<WeakPower>(player, context),
            APItem.FrailTrap => ApplyTimed<FrailPower>(player, context),
            APItem.VulnerableTrap => ApplyTimed<VulnerablePower>(player, context),
            APItem.NoDrawTrap => PowerCmd.Apply<NoDrawPower>(context, player.Creature, 1, null, null),
            APItem.TangledTrap => PowerCmd.Apply<TangledPower>(context, player.Creature, 1, null, null),
            APItem.VakuuTrap => PowerCmd.Apply<VakuuTrapPower>(context, player.Creature, 1, null, null),
            APItem.ConfusedTrap => PowerCmd.Apply<ConfusedPower>(context, player.Creature, 1, null, null),
            APItem.SlothTrap => ApplySloth(player, context),
            // Run on every peer through the native managed action
            APItem.DazedTrap => CardPileCmd.AddToCombatAndPreview<Dazed>(
                player.Creature, PileType.Draw, 2, null, CardPilePosition.Random),
            _ => throw new ArgumentOutOfRangeException(nameof(item)),
        };
        await effect;
        return true;
    }

    private static async Task ApplyTimed<T>(Player player, PlayerChoiceContext context) where T : PowerModel
    {
        var existing = player.Creature.GetPower<T>();
        var power = await PowerCmd.Apply<T>(context, player.Creature, 1, null, null);
        // These traps arrive on the player's turn, before the next enemy turn. The native
        // default skips that enemy-end tick, which would give a fresh trap an extra turn.
        // Preserve an existing debuff's duration policy when adding a stack to it. This can lead to funny situations
        // but its easier this way
        if (existing == null && power != null)
            power.SkipNextDurationTick = false;
    }

    private static async Task ApplySloth(Player player, PlayerChoiceContext context)
    {
        // Keep native stacking: adding six to an existing cap can help the player.
        var existing = player.Creature.GetPower<SlothPower>();
        var power = await PowerCmd.Apply<SlothPower>(context, player.Creature, 6, null, null);
        if (existing != null || power == null || player.Creature.GetPower<SlothPower>() != power)
            return; // Preserve an existing counter; Artifact may return an unattached power.

        // Multiplayer receipts may arrive after cards were played. Start the native counter
        // from this turn's history so a late trap does not grant another six card plays.
        power._cardsPlayedThisTurn = CombatManager.Instance.History.CardPlaysStarted.Count(
            entry => entry.CardPlay.Card.Owner == player && entry.HappenedThisTurn(player.Creature.CombatState));
        power.InvokeDisplayAmountChanged();
    }
}
