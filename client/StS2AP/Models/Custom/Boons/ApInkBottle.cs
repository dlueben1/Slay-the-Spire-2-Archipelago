using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models.RelicPools;
using MegaCrit.Sts2.Core.Saves.Runs;
using STS2RitsuLib.Interop.AutoRegistration;

namespace StS2AP.Models;

/// <summary>
/// The Ink Bottle boon, from the first game: every 10th card played draws 1 card. The count carries
/// over between fights, as Nunchaku's does. <see cref="ApRunModifiers"/> grants it at run start.
/// </summary>
[RegisterRelic(typeof(SharedRelicPool))]
public sealed class ApInkBottle : ApGameArtRelic
{
    private const int CardsPerDraw = 10;

    private int _cardsPlayed;

    protected override string ArtName => "ink_bottle";

    public override bool ShowCounter => true;

    public override int DisplayAmount => CardsPlayed % CardsPerDraw;

    [SavedProperty]
    public int CardsPlayed
    {
        get => _cardsPlayed;
        set
        {
            AssertMutable();
            _cardsPlayed = value;
            // As Nunchaku does, glow when the next card will trigger it.
            Status = value % CardsPerDraw == CardsPerDraw - 1 ? RelicStatus.Active : RelicStatus.Normal;
            InvokeDisplayAmountChanged();
        }
    }

    public override async Task AfterCardPlayed(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        if (cardPlay.Card.Owner != Owner)
            return;
        CardsPlayed++;
        if (CombatManager.Instance.IsInProgress && CardsPlayed % CardsPerDraw == 0)
        {
            Flash();
            await CardPileCmd.Draw(choiceContext, 1m, Owner);
        }
    }
}
