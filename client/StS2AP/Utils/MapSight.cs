using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using StS2AP.Data;

namespace StS2AP.Utils;

/// <summary>
/// The Hidden Map challenge: rooms beyond the next step stay hidden until the slot receives any
/// Map Sight.
/// </summary>
internal static class MapSight
{
    /// <summary>
    /// Whether a room shows the hidden room icon. Rooms the player has visited or can move to next
    /// always show what they are, so each step is still a choice between known rooms.
    /// </summary>
    internal static bool IsHidden(NNormalMapPoint point) =>
        point.State is not (MapPointState.Travelable or MapPointState.Traveled)
        && ChallengeSightItems.IsHidden(
            ArchipelagoClient.Settings?.HiddenMap == true,
            ItemTable.APItem.MapSight);

    /// <summary>Applies a newly received Map Sight to the map that is already built.</summary>
    internal static void RefreshVisibleMap()
    {
        if (NMapScreen.Instance is not { } screen)
            return;
        foreach (NNormalMapPoint point in screen._mapPointDictionary.Values.OfType<NNormalMapPoint>())
        {
            // A map point can be listed before its deferred _Ready finds the icon nodes.
            if (point.IsNodeReady())
                point.UpdateIcon();
        }
    }
}
