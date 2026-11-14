using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Saves.Runs;

namespace StS2AP.Models;

/// <summary>
/// Once per act, the player survives a killing blow and heals 25% of max HP. Works like the base
/// game's Lizard Tail, so it also stops lethal damage from an incoming DeathLink, which goes
/// through the same death check. Singleplayer only: the charge belongs to the whole modifier.
/// </summary>
public sealed class ApSecondWind : ApRunModifier
{
    private const decimal HealShare = 0.25m;

    private bool _usedThisAct;

    [SavedProperty]
    public bool UsedThisAct
    {
        get => _usedThisAct;
        set
        {
            AssertMutable();
            _usedThisAct = value;
        }
    }

    protected override string IconModifierEntry => "night_terrors";

    public override Task AfterActEntered()
    {
        UsedThisAct = false;
        return Task.CompletedTask;
    }

    // The late check, as Lizard Tail uses, lets other death-preventing effects go first.
    public override bool ShouldDieLate(Creature creature) => !creature.IsPlayer || UsedThisAct;

    public override async Task AfterPreventingDeath(Creature creature)
    {
        UsedThisAct = true;
        await CreatureCmd.Heal(creature, Math.Max(1m, creature.MaxHp * HealShare));
    }
}
