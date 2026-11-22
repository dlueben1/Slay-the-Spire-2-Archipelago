using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Runs;

namespace StS2AP.Models;

/// <summary>Every player starts the run with 10% less max HP (rounded up).</summary>
public sealed class ApFrailStart : ApRunModifier
{
    private const decimal MaxHpShareLost = 0.10m;

    protected override string IconModifierEntry => "terminal";

    // Runs once when a new run is set up, before Neow heals the player to full.
    protected override void AfterRunCreated(RunState runState)
    {
        foreach (Player player in runState.Players)
        {
            Creature creature = player.Creature;
            int lost = (int)Math.Ceiling(creature.MaxHp * MaxHpShareLost);
            creature.SetMaxHpInternal(creature.MaxHp - lost);
        }
    }
}
