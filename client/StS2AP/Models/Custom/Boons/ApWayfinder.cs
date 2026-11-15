using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Saves.Runs;

namespace StS2AP.Models;

/// <summary>
/// Three times per act, travel to any room in the next row, ignoring the paths. Works like the
/// base game's Winged Boots, with its uses refilled at the start of each act.
/// </summary>
public sealed class ApWayfinder : ApRunModifier
{
    private const int UsesPerAct = 3;

    private int _usesThisAct;

    [SavedProperty]
    public int UsesThisAct
    {
        get => _usesThisAct;
        set
        {
            AssertMutable();
            _usesThisAct = value;
        }
    }

    protected override string IconModifierEntry => "flight";

    public override bool ShouldAllowFreeTravel() => UsesThisAct < UsesPerAct;

    public override Task AfterActEntered()
    {
        UsesThisAct = 0;
        return Task.CompletedTask;
    }

    // As Winged Boots does: a use is spent when the room just entered was not one of the previous
    // room's children, which only free travel allows.
    public override Task AfterRoomEntered(AbstractRoom room)
    {
        if (RunState.CurrentRoomCount > 1 || RunState.VisitedMapCoords.Count <= 1)
            return Task.CompletedTask;

        IReadOnlyList<MapCoord> visited = RunState.VisitedMapCoords;
        MapPoint? previous = RunState.Map.GetPoint(visited[visited.Count - 2]);
        MapPoint? current = RunState.CurrentMapPoint;
        if (previous != null && current != null && !previous.Children.Contains(current))
            UsesThisAct++;
        return Task.CompletedTask;
    }
}
