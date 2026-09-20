using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes;
using StS2AP.Services;

namespace StS2AP.Patches;

/// <summary>
/// Bridges the in-run Godot process loop to queued Archipelago item processing.
/// </summary>
public static class Patches_ProcessReceivedItems
{
    /// <summary>
    /// Drains queued receipts during a run. Outside a run, the item service schedules its own
    /// deferred main-thread drain because <see cref="NRun._Process"/> is not active.
    /// </summary>
    [HarmonyPatch(typeof(NRun), nameof(NRun._Process))]
    public static class ProcessQueuedItemsDuringRun
    {
        [HarmonyPrefix]
        public static void Prefix()
        {
            ArchipelagoItemService.ProcessQueuedItems();
        }
    }
}
