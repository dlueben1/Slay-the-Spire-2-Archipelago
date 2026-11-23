using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Combat.History.Entries;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.RelicPools;
using STS2RitsuLib.Interop.AutoRegistration;

namespace StS2AP.Models;

/// <summary>
/// The Cursed Kettle boon: the first time you draw a Curse each turn, draw 1 card. Works like the
/// game's Iteration power does for Status cards, counting this turn's draws in the combat history.
/// <see cref="ApRunModifiers"/> grants it at run start.
/// </summary>
[RegisterRelic(typeof(SharedRelicPool))]
public sealed class ApCursedKettle : ApGameArtRelic
{
    private const decimal CardsPerCurse = 1m;

    protected override string ArtName => "cursed_kettle";

    public override async Task AfterCardDrawn(PlayerChoiceContext choiceContext, CardModel card, bool fromHandDraw)
    {
        if (card.Owner != Owner || card.Type != CardType.Curse)
            return;
        // The history already holds this draw, so the first Curse of the turn counts as 1.
        int cursesDrawnThisTurn = CombatManager.Instance.History.Entries
            .OfType<CardDrawnEntry>()
            .Count(entry => entry.HappenedThisTurn(card.CombatState)
                && entry.Actor == Owner.Creature
                && entry.Card.Type == CardType.Curse);
        if (cursesDrawnThisTurn > 1)
            return;
        Flash();
        await CardPileCmd.Draw(choiceContext, CardsPerCurse, Owner);
    }
}
