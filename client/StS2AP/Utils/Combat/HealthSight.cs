using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using StS2AP.Data;

namespace StS2AP.Utils.Combat;

/// <summary>How much health the local player may see.</summary>
internal enum HealthDetail
{
    // No health numbers or bar levels for anyone.
    None = 0,
    // Health of the player's own side: players, pets and allies.
    OwnSide = 1,
    // Everything the game shows normally.
    Full = 2,
}

/// <summary>
/// The Hidden Health challenge: health stays hidden until the slot receives any Progressive Health
/// Sight, which shows everyone's health. The <see cref="HealthDetail.OwnSide"/> step is unused
/// while every copy unlocks everything (see <see cref="ChallengeSightItems.UnlockedSteps"/>).
/// </summary>
internal static class HealthSight
{
    internal static HealthDetail Detail =>
        ChallengeSightItems.Applies(ArchipelagoClient.Settings?.HiddenHealth == true)
            ? (HealthDetail)ChallengeSightItems.UnlockedSteps(
                ItemTable.APItem.ProgressiveHealthSight,
                (int)HealthDetail.Full)
            : HealthDetail.Full;

    internal static bool IsHidden(Creature creature) =>
        Detail < (creature.IsEnemy ? HealthDetail.Full : HealthDetail.OwnSide);

    /// <summary>Applies a newly received Health Sight to the bars already on screen.</summary>
    internal static void RefreshVisibleHealth()
    {
        NRun.Instance?.GlobalUi.TopBar.Hp.UpdateHealth(0, 0);
        if (NCombatRoom.Instance is not { } room)
            return;
        foreach (NCreature creature in room.CreatureNodes)
        {
            // A newly added creature can be listed before its deferred _Ready sets up the bar.
            if (!creature.IsNodeReady())
                continue;
            creature._stateDisplay._healthBar.RefreshValues();
        }
    }
}
