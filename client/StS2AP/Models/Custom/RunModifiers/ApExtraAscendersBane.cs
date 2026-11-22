using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Runs;

namespace StS2AP.Models;

/// <summary>
/// Every player starts the run with an extra Ascender's Bane, on top of the one from the
/// Ascender's Bane ascension.
/// </summary>
public sealed class ApExtraAscendersBane : ApRunModifier
{
    protected override string IconModifierEntry => "cursed_run";

    // Adds the card the way the base game's ascension effects do when a run is set up.
    protected override void AfterRunCreated(RunState runState)
    {
        foreach (Player player in runState.Players)
        {
            AscendersBane bane = runState.CreateCard<AscendersBane>(player);
            bane.FloorAddedToDeck = 1;
            player.Deck.AddInternal(bane, -1, silent: true);
        }
    }
}
