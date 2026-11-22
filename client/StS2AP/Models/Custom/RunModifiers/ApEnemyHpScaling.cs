using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Saves.Runs;

namespace StS2AP.Models;

/// <summary>
/// Enemies get the slot's percentage of extra max HP in Acts 2 and 3. The game has no hook for a
/// monster's starting HP, so <c>Patches_ApEnemyHpScaling</c> calls <see cref="ScaleNewEnemy"/>
/// as each enemy is created.
/// </summary>
public sealed class ApEnemyHpScaling : ApRunModifier
{
    private int _percent;

    // Saved with the run, so a run keeps the percentage it started with.
    [SavedProperty]
    public int Percent
    {
        get => _percent;
        set
        {
            AssertMutable();
            _percent = value;
        }
    }

    protected override string IconModifierEntry => "insanity";

    public override LocString Description
    {
        get
        {
            LocString description = base.Description;
            description.Add("Percent", Percent);
            return description;
        }
    }

    /// <summary>
    /// Scales a new enemy's max and current HP, after the game has rolled them and scaled them
    /// for multiplayer.
    /// </summary>
    public void ScaleNewEnemy(Creature enemy)
    {
        decimal factor = LaterActsFactor(Percent);
        if (factor == 1m)
            return;
        enemy.SetMaxHpInternal(Scale(enemy.MaxHp, factor));
        enemy.SetCurrentHpInternal(Scale(enemy.CurrentHp, factor));
    }

    private static int Scale(int hp, decimal factor) =>
        (int)Math.Round(hp * factor, MidpointRounding.AwayFromZero);
}
