using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models.Cards;

namespace StS2AP.Models;

/// <summary>
/// An Ascender's Bane at the start of Act 2 and of Act 3. The card is Eternal, so unlike Mounting
/// Curses no card removal can undo it.
/// </summary>
public sealed class ApMountingBanes : ApRunModifier
{
    protected override string IconModifierEntry => "hoarder";

    // Adds the card the way the base game's Cursed Run adds its curses.
    public override async Task AfterActEntered()
    {
        if (RunState.CurrentActIndex == 0)
            return;
        foreach (Player player in RunState.Players)
        {
            AscendersBane bane = RunState.CreateCard<AscendersBane>(player);
            CardPileAddResult result = await CardPileCmd.Add(bane, PileType.Deck);
            if (LocalContext.IsMe(player))
                CardCmd.PreviewCardPileAdd(result);
        }
    }
}
