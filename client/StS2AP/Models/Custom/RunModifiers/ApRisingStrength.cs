using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Rooms;

namespace StS2AP.Models;

/// <summary>
/// Elites and bosses start each combat with 1 Strength in Act 2 and 2 in Act 3; Act 1 is
/// unchanged.
/// </summary>
public sealed class ApRisingStrength : ApRunModifier
{
    protected override string IconModifierEntry => "big_game_hunter";

    // Monsters placed by the encounter join without the creature-added hook, so they are buffed
    // on room entry; summons arrive through the hook. The base game's Murderous does the same.
    public override async Task AfterRoomEntered(AbstractRoom room)
    {
        if (!IsEliteOrBossCombat(room.RoomType))
            return;
        foreach (Creature enemy in StartingEnemies(room))
            await AddStrength(enemy);
    }

    public override Task AfterCreatureAddedToCombat(Creature creature)
    {
        if (!IsEnemy(creature)
            || creature.CombatState?.Encounter is not { } encounter
            || !IsEliteOrBossCombat(encounter.RoomType))
        {
            return Task.CompletedTask;
        }
        return AddStrength(creature);
    }

    // One Strength for each act after Act 1.
    private Task AddStrength(Creature enemy)
    {
        int strength = RunState.CurrentActIndex;
        return strength > 0
            ? PowerCmd.Apply<StrengthPower>(new ThrowingPlayerChoiceContext(), enemy, strength, null, null)
            : Task.CompletedTask;
    }
}
