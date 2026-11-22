using MegaCrit.Sts2.Core.Entities.Players;

namespace StS2AP.Models;

/// <summary>Players draw one fewer card on the first turn of each combat.</summary>
public sealed class ApSlowStart : ApRunModifier
{
    protected override string IconModifierEntry => "sealed_deck";

    public override decimal ModifyHandDraw(Player player, decimal count) =>
        player.PlayerCombatState is { TurnNumber: 1 } ? Math.Max(0m, count - 1) : count;
}
