using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Players;

namespace StS2AP.Models;

/// <summary>
/// At the start of Acts 2 and 3, each player gains 20% of the gold they hold, up to 100. The
/// payout is ordinary gold, so Gilded and Bowler's Hat raise it too.
/// </summary>
public sealed class ApCompoundInterest : ApRunModifier
{
    private const decimal InterestRate = 0.20m;
    private const int MaxInterest = 100;

    protected override string IconModifierEntry => "midas";

    public override async Task AfterActEntered()
    {
        if (RunState.CurrentActIndex == 0)
            return;
        foreach (Player player in RunState.Players)
        {
            int interest = Math.Min(MaxInterest, (int)Math.Floor(player.Gold * InterestRate));
            if (interest > 0)
                await PlayerCmd.GainGold(interest, player);
        }
    }
}
