using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;

namespace StS2AP.Models;

/// <summary>
/// A relic that makes one unplayable card type playable for 0 energy, exhausting the card when
/// played, as the first game's Medical Kit does for Status cards and Blue Candle for Curses. It
/// swaps the card's Unplayable keyword for Exhaust during combat, so the card's text and the
/// game's own play rules both follow. Cards of that type that are already playable keep their
/// own rules.
/// </summary>
public abstract class ApPlayableCardTypeRelic : ApGameArtRelic
{
    protected abstract CardType PlayableType { get; }

    public override bool TryModifyKeywordsInCombat(CardModel card, ISet<CardKeyword> keywords)
    {
        if (card.Owner != Owner || card.Type != PlayableType || !keywords.Remove(CardKeyword.Unplayable))
            return false;
        keywords.Add(CardKeyword.Exhaust);
        return true;
    }

    public override async Task AfterCardPlayed(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        if (cardPlay.Card.Owner != Owner || cardPlay.Card.Type != PlayableType)
            return;
        Flash();
        await AfterPlayingCardOfType(choiceContext, cardPlay);
    }

    /// <summary>Anything more the relic does when a card of its type is played.</summary>
    protected virtual Task AfterPlayingCardOfType(PlayerChoiceContext choiceContext, CardPlay cardPlay) =>
        Task.CompletedTask;
}
