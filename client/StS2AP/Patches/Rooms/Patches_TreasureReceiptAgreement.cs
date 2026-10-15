using HarmonyLib;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using StS2AP.Utils;

namespace StS2AP.Patches;

public static class Patches_TreasureReceiptAgreement
{
    private static bool Enabled => MultiplayerSupport.IsRealMultiplayerRun
        && MultiplayerSupport.ShouldRunReplicatedConstruction(MultiplayerFeature.CombatRewardLocations);

    [HarmonyPatch(typeof(Hook), nameof(Hook.AfterRoomEntered))]
    private static class FreezeBeforePicker
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
            await RelicReceiptMultiplayer.FreezeChest(run);
        }
    }

    [HarmonyPatch(typeof(NTreasureRoom), "OnChestButtonReleased")]
    private static class WaitForPicker
    {
        [HarmonyPrefix]
        private static bool Prefix()
        {
            // The room node exists while its entry hook awaits the host. Leave the button enabled
            // but ignore early presses, so it can be clicked normally once the picker is ready.
            if (!Enabled || RunManager.Instance.DebugOnlyGetState() is not RunState run
                || RelicReceiptMultiplayer.IsPickerReady(run)) return true;
            NotificationUtility.ShowRawText("Waiting for the host's chest decision.");
            return false;
        }
    }

    [HarmonyPatch(typeof(NTreasureRoom), "OpenChest")]
    private static class PublishProceedReadiness
    {
        [HarmonyPostfix]
        private static void Postfix(ref Task __result)
        {
            if (Enabled && RunManager.Instance.DebugOnlyGetState() is RunState run)
                __result = AfterChestFinished(__result, run);
        }

        private static async Task AfterChestFinished(Task original, RunState run)
        {
            await original;
            if (Enabled && ReferenceEquals(RunManager.Instance.DebugOnlyGetState(), run))
                RelicReceiptMultiplayer.MarkLocalTreasureProceedReady(run);
        }
    }
}
