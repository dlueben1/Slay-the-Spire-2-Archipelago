using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using StS2AP.UI;

namespace StS2AP.Patches;

/// <summary>Close AP rewards when the move starts, before native room-exit cleanup.</summary>
public static class Patches_APRewardTravel
{
    // This native task covers the full travel animation and room entry.
    [HarmonyPatch(typeof(NMapScreen), nameof(NMapScreen.TravelToMapCoord))]
    private static class CloseAtTravelStart
    {
        [HarmonyPrefix]
        private static void Prefix(out int? __state) =>
            __state = ArchipelagoRewardUI.BeginTravel();

        [HarmonyPostfix]
        private static void Postfix(ref Task __result, int? __state)
        {
            if (__state is int apLifecycleVersion)
                __result = FinishTravel(__result, apLifecycleVersion);
        }

        private static async Task FinishTravel(Task travel, int apLifecycleVersion)
        {
            try
            {
                await travel;
            }
            finally
            {
                ArchipelagoRewardUI.EndTravel(apLifecycleVersion);
            }
        }
    }
}
