using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Rooms;

namespace StS2AP.Models;

/// <summary>Elites and bosses start each combat with extra Strength.</summary>
public sealed class ApEliteStrength : ApRunModifier
{
    private const int StrengthAmount = 1;

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

    private static Task AddStrength(Creature enemy) =>
        PowerCmd.Apply<StrengthPower>(
            new ThrowingPlayerChoiceContext(), enemy, StrengthAmount, null, null);
}
