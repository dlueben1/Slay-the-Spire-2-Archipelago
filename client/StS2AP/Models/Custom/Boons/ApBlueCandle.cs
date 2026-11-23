using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models.RelicPools;
using MegaCrit.Sts2.Core.ValueProps;
using STS2RitsuLib.Interop.AutoRegistration;

namespace StS2AP.Models;

/// <summary>
/// The Blue Candle boon, from the first game: Curses can be played for 0 energy; playing one costs
/// 1 HP and exhausts it. <see cref="ApRunModifiers"/> grants it at run start.
/// </summary>
[RegisterRelic(typeof(SharedRelicPool))]
public sealed class ApBlueCandle : ApPlayableCardTypeRelic
{
    private const decimal HpLossPerCurse = 1m;

    protected override string ArtName => "blue_candle";

    protected override CardType PlayableType => CardType.Curse;

    // The same HP loss a card such as Bloodletting deals to its player.
    protected override async Task AfterPlayingCardOfType(PlayerChoiceContext choiceContext, CardPlay cardPlay) =>
        await CreatureCmd.Damage(
            choiceContext,
            Owner.Creature,
            HpLossPerCurse,
            ValueProp.Unblockable | ValueProp.Unpowered | ValueProp.Move,
            cardPlay.Card,
            cardPlay);
}
