using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using StS2AP.Utils;

namespace StS2AP.Patches;

/// <summary>
/// The points where every peer pairs AP Relic receipts with natural relic rewards
/// (<see cref="RelicReceiptMultiplayer"/>): each is fixed by the host's action order.
/// </summary>
public static class Patches_NaturalRelicDecisions
{
    private static bool Enabled => MultiplayerSupport.IsRealMultiplayerRun
        && MultiplayerSupport.ShouldRunReplicatedConstruction(MultiplayerFeature.CombatRewardLocations);

    [HarmonyPatch(typeof(Hook), nameof(Hook.AfterRoomEntered))]
    private static class DecideChestBeforePicker
    {
        [HarmonyPostfix]
        private static void Postfix(IRunState runState, AbstractRoom room, ref Task __result)
        {
            // TreasureRoom awaits this hook immediately before BeginRelicPicking in both APIs.
            if (Enabled && room is TreasureRoom && runState is RunState run)
                __result = AfterHooks(__result, run);
        }

        private static async Task AfterHooks(Task original, RunState run)
        {
            await original;
            RelicReceiptMultiplayer.DecideChest(run);
        }
    }

    [HarmonyPatch(typeof(Hook), nameof(Hook.AfterCombatEnd),
        new[] { typeof(IRunState), typeof(ICombatState), typeof(CombatRoom) })]
    private static class ReserveEliteReceipts
    {
        [HarmonyPrefix]
        private static void Prefix(IRunState runState, CombatRoom room)
        {
            if (Enabled && runState is RunState run)
                RelicReceiptMultiplayer.ReserveEliteReceipts(run, room);
        }
    }

    [HarmonyPatch(typeof(MoveToMapCoordAction), "ExecuteAction")]
    private static class SettleElitesBeforeMoving
    {
        [HarmonyPrefix]
        private static void Prefix()
        {
            if (Enabled && RunManager.Instance.DebugOnlyGetState() is RunState run)
                RelicReceiptMultiplayer.SettleElites(run);
        }
    }
}
