using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Runs;
using StS2AP.Models;

namespace StS2AP.Utils;

/// <summary>
/// Shares free travel between the relics that allow it: Route Diagram, the Wayfinder Boon and the
/// game's Winged Boots. Any of them unlocks a free move, and on their own each would spend a use on
/// it, so a player holding two would pay twice for one move. Instead exactly one pays, chosen in
/// <see cref="SpendOrder"/>.
/// </summary>
internal static class FreeTravelUses
{
    // Route Diagram's use comes back each act, so it is spent before the uses that never return.
    private static readonly Type[] SpendOrder = [typeof(ApRouteDiagram), typeof(ApWayfinder), typeof(WingedBoots)];

    // Every relic asks about the same move as the room is entered, and the payer spends its use
    // between those calls, so the choice is made once per move and remembered.
    private static (Player Player, int ActIndex, int RoomsVisited, RelicModel? Payer)? _lastMove;

    /// <summary>Whether the player holds an AP relic that shares free travel with Winged Boots.</summary>
    internal static bool HasApFreeTravelRelic(Player player) =>
        player.Relics.Any(relic => relic is ApRouteDiagram or ApWayfinder);

    /// <summary>
    /// Whether <paramref name="relic"/> pays for the room its owner just entered. Call it from the
    /// relic's AfterRoomEntered, before spending a use.
    /// </summary>
    internal static bool ShouldSpend(RelicModel relic)
    {
        if (relic.Owner.RunState is not RunState runState || !EnteredByFreeTravel(runState))
            return false;
        return ReferenceEquals(PayerFor(relic.Owner, runState), relic);
    }

    // As Winged Boots checks: on first entering a map point, the move was free when the room is not
    // one of the previous room's children.
    private static bool EnteredByFreeTravel(RunState runState)
    {
        if (runState.CurrentRoomCount > 1 || runState.VisitedMapCoords.Count <= 1)
            return false;
        IReadOnlyList<MapCoord> visited = runState.VisitedMapCoords;
        MapPoint? previous = runState.Map.GetPoint(visited[visited.Count - 2]);
        MapPoint? current = runState.CurrentMapPoint;
        return previous != null && current != null && !previous.Children.Contains(current);
    }

    private static RelicModel? PayerFor(Player player, RunState runState)
    {
        int actIndex = runState.CurrentActIndex;
        int roomsVisited = runState.VisitedMapCoords.Count;
        if (_lastMove is { } last
            && ReferenceEquals(last.Player, player)
            && last.ActIndex == actIndex
            && last.RoomsVisited == roomsVisited)
            return last.Payer;

        // A run modifier such as Flight makes every move free, so no relic spends a use on it.
        RelicModel? payer = runState.Modifiers.Any(modifier => modifier.ShouldAllowFreeTravel())
            ? null
            : SpendOrder
                .Select(type => player.Relics.FirstOrDefault(relic => relic.GetType() == type && !relic.IsUsedUp))
                .FirstOrDefault(relic => relic != null);
        _lastMove = (player, actIndex, roomsVisited, payer);
        return payer;
    }
}
