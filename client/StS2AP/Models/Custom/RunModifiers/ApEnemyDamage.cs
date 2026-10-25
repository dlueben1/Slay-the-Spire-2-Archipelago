using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Saves.Runs;
using MegaCrit.Sts2.Core.ValueProps;

namespace StS2AP.Models;

/// <summary>Enemies deal a percentage of their normal damage to the players' side.</summary>
public sealed class ApEnemyDamage : ApRunModifier
{
    private int _damagePercent = 100;

    // Saved with the run, so a run keeps the percentage it started with.
    [SavedProperty]
    public int DamagePercent
    {
        get => _damagePercent;
        set
        {
            AssertMutable();
            _damagePercent = value;
        }
    }

    protected override string IconModifierEntry => "insanity";

    public override LocString Description
    {
        get
        {
            LocString description = base.Description;
            description.Add(nameof(DamagePercent), DamagePercent);
            return description;
        }
    }

    // The game multiplies damage by each listener's factor after additive bonuses such as
    // Strength, alongside Vulnerable and Weak. Intents use the same pipeline, so they show the
    // scaled damage. The beta API adds the card play to the hook.
#if STS_PUBLIC
    public override decimal ModifyDamageMultiplicative(
        Creature? target,
        decimal amount,
        ValueProp props,
        Creature? dealer,
        CardModel? cardSource) =>
        DamageFactor(target, dealer);
#else
    public override decimal ModifyDamageMultiplicative(
        Creature? target,
        decimal amount,
        ValueProp props,
        Creature? dealer,
        CardModel? cardSource,
        CardPlay? cardPlay) =>
        DamageFactor(target, dealer);
#endif

    // A null target is a preview without a chosen target.
    private decimal DamageFactor(Creature? target, Creature? dealer)
    {
        bool enemyHitsPlayersSide = dealer is { Side: CombatSide.Enemy }
            && target is not { Side: CombatSide.Enemy };
        return enemyHitsPlayersSide ? DamagePercent / 100m : 1m;
    }
}
