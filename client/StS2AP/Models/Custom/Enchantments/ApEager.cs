using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Enchantments;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;

namespace StS2AP.Models.Enchantments;

/// <summary>
/// The first time this card is drawn each combat, it costs 1 less until it is played. Built like
/// the game's Slumbering Essence (the cost change) and Swift (once per combat).
/// </summary>
public sealed class ApEager : EnchantmentModel
{
    // Combat cards are fresh copies of deck cards, so this resets every combat.
    private bool _discounted;

    public override bool CanEnchant(CardModel card) =>
        base.CanEnchant(card) && !card.Keywords.Contains(CardKeyword.Unplayable) && !card.EnergyCost.CostsX;

    public override Task AfterCardDrawn(PlayerChoiceContext choiceContext, CardModel card, bool fromHandDraw)
    {
        if (card != Card || _discounted || Card.Pile?.Type != PileType.Hand)
            return Task.CompletedTask;
        _discounted = true;
        Card.EnergyCost.AddUntilPlayed(-1);
        return Task.CompletedTask;
    }

    public override Task AfterCardPlayed(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        if (cardPlay.Card == Card)
            Status = EnchantmentStatus.Disabled;
        return Task.CompletedTask;
    }
}
