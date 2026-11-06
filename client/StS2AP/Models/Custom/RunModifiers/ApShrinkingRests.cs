using MegaCrit.Sts2.Core.Entities.Creatures;

namespace StS2AP.Models;

/// <summary>
/// Resting heals 25% of max HP in Act 2 and 20% in Act 3, instead of the base game's 30%; Act 1
/// is unchanged.
/// </summary>
public sealed class ApShrinkingRests : ApRunModifier
{
    private const decimal HealShareCutPerAct = 0.05m;

    protected override string IconModifierEntry => "night_terrors";

    // Subtract the cut instead of scaling, so flat bonuses such as Regal Pillow keep their full
    // value whatever order the heal modifiers run in.
    public override decimal ModifyRestSiteHealAmount(Creature creature, decimal amount) =>
        Math.Max(0m, amount - creature.MaxHp * HealShareCutPerAct * RunState.CurrentActIndex);
}
