using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Rooms;

namespace StS2AP.Models;

/// <summary>
/// A difficulty modifier that the slot's Run Modifiers option adds to every new AP run. They are
/// ordinary run modifiers, so saves, multiplayer and the top bar treat them like the base game's,
/// but they are never offered in Daily or Custom runs and Neow ignores them (see
/// <see cref="ApRunModifiers"/>).
/// </summary>
public abstract class ApRunModifier : ModifierModel
{
    /// <summary>The base-game modifier whose icon this modifier borrows.</summary>
    protected abstract string IconModifierEntry { get; }

    protected override string IconPath => ApRunModifiers.ModifierIconPath(IconModifierEntry);

    protected static bool IsEnemy(Creature creature) => creature.Side != CombatSide.Player;

    protected static bool IsEliteOrBossCombat(RoomType roomType) =>
        roomType is RoomType.Elite or RoomType.Boss;

    /// <summary>The enemies present when a combat room is entered.</summary>
    protected static IReadOnlyList<Creature> StartingEnemies(AbstractRoom room) =>
        room is CombatRoom combatRoom
            ? combatRoom.CombatState.Enemies.ToList()
            : Array.Empty<Creature>();
}
