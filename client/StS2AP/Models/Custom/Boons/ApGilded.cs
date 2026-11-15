using MegaCrit.Sts2.Core.Entities.Players;

namespace StS2AP.Models;

/// <summary>
/// Players gain 30% more gold, as Bowler's Hat does for 25%. AP gold items are granted through the
/// same gold command, so they are raised too.
/// </summary>
public sealed class ApGilded : ApRunModifier
{
    private const decimal GoldMultiplier = 1.3m;

    protected override string IconModifierEntry => "midas";

    public override decimal ModifyGoldGained(Player player, decimal amount) => amount * GoldMultiplier;
}
