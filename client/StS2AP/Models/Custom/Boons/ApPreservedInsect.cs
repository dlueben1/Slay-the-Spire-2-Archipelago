using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models.RelicPools;
using MegaCrit.Sts2.Core.Rooms;
using STS2RitsuLib.Interop.AutoRegistration;

namespace StS2AP.Models;

/// <summary>
/// The Preserved Insect boon, from the first game: enemies in Elite fights have 25% less HP. The
/// game has no hook for a monster's starting HP, so <c>Patches_ApEnemyHpScaling</c> calls
/// <see cref="ShrinkNewEnemy"/> as each enemy is created. In multiplayer every player holds one, and
/// the enemy shrinks once. <see cref="ApRunModifiers"/> grants it at run start.
/// </summary>
[RegisterRelic(typeof(SharedRelicPool))]
public sealed class ApPreservedInsect : ApGameArtRelic
{
    private const decimal HpFactor = 0.75m;

    protected override string ArtName => "preserved_insect";

    public override Task AfterRoomEntered(AbstractRoom room)
    {
        if (room.RoomType == RoomType.Elite)
            Flash();
        return Task.CompletedTask;
    }

    /// <summary>Shrinks a new enemy's max and current HP, after any other HP scaling.</summary>
    public static void ShrinkNewEnemy(Creature enemy)
    {
        enemy.SetMaxHpInternal(Shrink(enemy.MaxHp));
        enemy.SetCurrentHpInternal(Shrink(enemy.CurrentHp));
    }

    private static int Shrink(int hp) => Math.Max(1, (int)Math.Round(hp * HpFactor, MidpointRounding.AwayFromZero));
}
