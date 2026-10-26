using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Saves.Runs;
using MegaCrit.Sts2.Core.ValueProps;

namespace StS2AP.Models;

/// <summary>
/// Enemies get extra max HP and damage for each act after Act 1: the slot's percentage in Act 2
/// and twice it in Act 3. The game has no hook for a monster's starting HP, so
/// <c>Patches_ApActScaling</c> calls <see cref="ScaleNewEnemy"/> as each enemy is created.
/// </summary>
public sealed class ApActScaling : ApRunModifier
{
    private int _percentPerAct;

    // Saved with the run, so a run keeps the percentage it started with.
    [SavedProperty]
    public int PercentPerAct
    {
        get => _percentPerAct;
        set
        {
            AssertMutable();
            _percentPerAct = value;
        }
    }

    protected override string IconModifierEntry => "insanity";

    public override LocString Description
    {
        get
        {
            LocString description = base.Description;
            description.Add("Act2Percent", PercentPerAct);
            description.Add("Act3Percent", PercentPerAct * 2);
            return description;
        }
    }

    /// <summary>
    /// Scales a new enemy's max and current HP, after the game has rolled them and scaled them
    /// for multiplayer.
    /// </summary>
    public void ScaleNewEnemy(Creature enemy)
    {
        decimal factor = CurrentActFactor();
        if (factor == 1m)
            return;
        enemy.SetMaxHpInternal(Scale(enemy.MaxHp, factor));
        enemy.SetCurrentHpInternal(Scale(enemy.CurrentHp, factor));
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
        return enemyHitsPlayersSide ? CurrentActFactor() : 1m;
    }

    private decimal CurrentActFactor() => 1m + PercentPerAct * RunState.CurrentActIndex / 100m;

    private static int Scale(int hp, decimal factor) =>
        (int)Math.Round(hp * factor, MidpointRounding.AwayFromZero);
}
