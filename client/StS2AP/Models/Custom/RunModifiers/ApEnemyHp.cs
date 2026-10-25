using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Saves.Runs;

namespace StS2AP.Models;

/// <summary>
/// Every enemy has a percentage of its normal max HP. The game has no hook for a monster's
/// starting HP, so <c>Patches_ApEnemyHp</c> calls <see cref="ScaleNewEnemy"/> as each enemy is
/// created.
/// </summary>
public sealed class ApEnemyHp : ApRunModifier
{
    private int _hpPercent = 100;

    // Saved with the run, so a run keeps the percentage it started with.
    [SavedProperty]
    public int HpPercent
    {
        get => _hpPercent;
        set
        {
            AssertMutable();
            _hpPercent = value;
        }
    }

    protected override string IconModifierEntry => "hoarder";

    public override LocString Description
    {
        get
        {
            LocString description = base.Description;
            description.Add(nameof(HpPercent), HpPercent);
            return description;
        }
    }

    /// <summary>
    /// Scales a new enemy's max and current HP, after the game has rolled them and scaled them
    /// for multiplayer.
    /// </summary>
    public void ScaleNewEnemy(Creature enemy)
    {
        enemy.SetMaxHpInternal(Scale(enemy.MaxHp));
        enemy.SetCurrentHpInternal(Scale(enemy.CurrentHp));
    }

    private int Scale(int hp) =>
        (int)Math.Round(hp * HpPercent / 100m, MidpointRounding.AwayFromZero);
}
