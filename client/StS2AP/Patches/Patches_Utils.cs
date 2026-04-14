using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes;
using StS2AP.UI;

namespace StS2AP.Patches
{
    /// <summary>
    /// Hooking into Godot's per frame rendering
    /// </summary>
    [HarmonyPatch(typeof(NRun), nameof(NRun._Process))]
    public static class Patches_NRun_Process
    {

        [HarmonyPostfix]
        public static void Hook()
        {
            ArchipelagoNotificationUI.CheckAndHandleNotification();
        }
    }
}
