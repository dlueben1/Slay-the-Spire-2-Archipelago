using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Nodes.Screens.TreasureRoomRelic;
using MegaCrit.Sts2.Core.Runs;

namespace StS2AP.Patches;

/// <summary>
/// Keeps the native multiplayer treasure picker usable when AP receipt gating leaves fewer
/// shared relic candidates than players. The remaining relics deliberately stay shared: any
/// player may vote for and win a candidate funded by another player's AP receipt.
/// </summary>
public static class Patches_TreasureRoomRelicScarcity
{
    [HarmonyPatch(typeof(NTreasureRoomRelicCollection), "get_DefaultFocusedControl")]
    private static class FocusFirstVisibleScarcityRelic
    {
        [HarmonyPrefix]
        private static bool Prefix(
            ref Control? __result,
            List<NTreasureRoomRelicHolder> ____holdersInUse,
            IRunState ____runState)
        {
            if (!IsScarcityChest(____runState))
                return true;

            // The base getter indexes holders by local player slot. That is only valid when the
            // picker contains one candidate per player; use any visible prize as the initial
            // controller focus when AP intentionally creates a smaller shared pool.
            __result = ____holdersInUse.FirstOrDefault(holder => holder.Visible);
            return false;
        }
    }

    [HarmonyPatch(typeof(NHandImageCollection), "_Input")]
    private static class IgnoreHandInputWithoutLocalPlayer
    {
        [HarmonyPrefix]
        private static bool Prefix()
        {
            // NHandImageCollection dereferences LocalContext.NetId.Value. If chest setup fails or
            // networking is between contexts, input can still reach the node without a local ID.
            return LocalContext.NetId.HasValue;
        }
    }

    private static bool IsScarcityChest(IRunState runState)
    {
        if (!MultiplayerSupport.IsRealMultiplayerRun
            || !MultiplayerSupport.ShouldRunReplicatedConstruction(
                MultiplayerFeature.CombatRewardLocations))
        {
            return false;
        }

        var relics = RunManager.Instance.TreasureRoomRelicSynchronizer.CurrentRelics;
        return relics is { Count: > 0 }
            && relics.Count < runState.Players.Count;
    }
}
