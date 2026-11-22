using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Rooms;

namespace StS2AP.Models;

/// <summary>
/// Enemies start combat with one small random buff in Act 2 and two different ones in Act 3;
/// Act 1 is unchanged.
/// </summary>
public sealed class ApEscalatingBuffs : ApRunModifier
{
    private static readonly Func<Creature, Task>[] Buffs =
    [
        enemy => Apply<StrengthPower>(enemy, 1),
        enemy => Apply<PlatingPower>(enemy, 4),
        enemy => Apply<ThornsPower>(enemy, 2),
        enemy => Apply<ArtifactPower>(enemy, 1),
        enemy => Apply<RegenPower>(enemy, 3),
        enemy => Apply<CurlUpPower>(enemy, 4),
        enemy => Apply<VigorPower>(enemy, 4),
        enemy => Apply<SlipperyPower>(enemy, 1),
    ];

    // Counts the enemies buffed in the current combat. Not saved: a reloaded run restarts the
    // combat from its room, so the count starts again at zero.
    private int _enemiesBuffed;

    protected override string IconModifierEntry => "murderous";

    // Encounter monsters are buffed on room entry and summons through the creature-added hook,
    // as with the base game's Murderous.
    public override async Task AfterRoomEntered(AbstractRoom room)
    {
        if (room is not CombatRoom)
            return;
        _enemiesBuffed = 0;
        foreach (Creature enemy in StartingEnemies(room))
            await BuffRandomly(enemy);
    }

    public override Task AfterCreatureAddedToCombat(Creature creature) =>
        IsEnemy(creature) ? BuffRandomly(creature) : Task.CompletedTask;

    private async Task BuffRandomly(Creature enemy)
    {
        // One buff for each act after Act 1.
        int buffCount = RunState.CurrentActIndex;
        if (buffCount <= 0)
            return;

        // Seed the picks from the run and the enemy's place in this combat, so every multiplayer
        // peer and every reload of the same fight gives the same buffs.
        var rng = new Rng(
            RunState.Rng.Seed,
            $"ap_enemy_buff_{RunState.CurrentActIndex}_{RunState.ActFloor}_{_enemiesBuffed++}"
        );
        List<Func<Creature, Task>> remaining = Buffs.ToList();
        for (int i = 0; i < buffCount && remaining.Count > 0; i++)
        {
            Func<Creature, Task> buff = rng.NextItem(remaining)!;
            remaining.Remove(buff);
            await buff(enemy);
        }
    }

    private static Task Apply<TPower>(Creature enemy, int amount) where TPower : PowerModel =>
        PowerCmd.Apply<TPower>(new ThrowingPlayerChoiceContext(), enemy, amount, null, null);
}
