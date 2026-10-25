using MegaCrit.Sts2.Core.Entities.Creatures;

namespace StS2AP.Models;

/// <summary>Resting heals 25% of max HP instead of the base game's 30%.</summary>
public sealed class ApWeakerRests : ApRunModifier
{
    private const decimal VanillaHealShare = 0.30m;
    private const decimal HealShare = 0.25m;

    protected override string IconModifierEntry => "night_terrors";

    // Subtract the difference instead of scaling, so flat bonuses such as Regal Pillow keep
    // their full value whatever order the heal modifiers run in.
    public override decimal ModifyRestSiteHealAmount(Creature creature, decimal amount) =>
        Math.Max(0m, amount - creature.MaxHp * (VanillaHealShare - HealShare));
}
