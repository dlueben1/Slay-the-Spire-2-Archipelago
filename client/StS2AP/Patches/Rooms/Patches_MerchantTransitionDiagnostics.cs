using System.Diagnostics;
using HarmonyLib;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Merchant;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens.Shops;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;

namespace StS2AP.Patches;

/// <summary>
/// Narrow breadcrumbs for failures that can strand map travel behind the black room-transition
/// overlay. These patches observe and rethrow failures; only the optional AP-page construction
/// has a safe vanilla fallback (implemented in <see cref="Patches_ShopPages"/>).
/// </summary>
public static class Patches_MerchantTransitionDiagnostics
{
    private static readonly TimeSpan StallWarningDelay = TimeSpan.FromSeconds(10);
    private static long _nextTravelId;
    private static long _activeTravelId;
    private static string _activePointType = "none";
    private static int _traceActive;

    private sealed record TravelObservation(
        long Id,
        string PointType,
        Stopwatch Timer,
        bool TraceEnabled);

    private static string ActiveTravel =>
        $"travel={Volatile.Read(ref _activeTravelId)} point={_activePointType}";

    private static bool TraceActive => Volatile.Read(ref _traceActive) != 0;

    private static string NetworkContext()
    {
        try
        {
            RunManager? manager = RunManager.Instance;
            RunState? runState = manager?.DebugOnlyGetState();
            if (manager == null || runState == null)
                return "run=unavailable";

            string players = string.Join(",", runState.Players.Select(player => player.NetId));
            return $"role={manager.NetService.Type} localNetId={manager.NetService.NetId} "
                + $"players=[{players}] act={runState.CurrentActIndex + 1} "
                + $"floor={runState.ActFloor}";
        }
        catch (Exception ex)
        {
            return $"run=context-error({ex.GetType().Name})";
        }
    }

    private static bool? IsLocalPlayer(Player player)
    {
        try
        {
            return LocalContext.IsMe(player);
        }
        catch
        {
            return null;
        }
    }

    private static async Task ObserveTask(
        Task task,
        string stage,
        Stopwatch timer,
        bool warnIfStalled)
    {
        if (warnIfStalled)
        {
            Task first = await Task.WhenAny(task, Task.Delay(StallWarningDelay));
            if (!ReferenceEquals(first, task))
            {
                LogUtility.Warn(
                    $"MerchantTransition: STALLED stage={stage} elapsed={timer.ElapsedMilliseconds}ms "
                        + $"{ActiveTravel} {NetworkContext()}"
                );
            }
        }

        try
        {
            await task;
            LogUtility.Info(
                $"MerchantTransition: completed stage={stage} elapsed={timer.ElapsedMilliseconds}ms "
                    + $"{ActiveTravel} {NetworkContext()}"
            );
        }
        catch (Exception ex)
        {
            LogUtility.Error(
                $"MerchantTransition: FAILED stage={stage} elapsed={timer.ElapsedMilliseconds}ms "
                    + $"{ActiveTravel} {NetworkContext()} {ex}"
            );
            throw;
        }
    }

    [HarmonyPatch(typeof(RunManager), nameof(RunManager.EnterMapPointInternal))]
    private static class TraceMapPointTravel
    {
        [HarmonyPrefix]
        private static void Prefix(
            int actFloor,
            MapPointType pointType,
            bool saveGame,
            out TravelObservation __state)
        {
            long id = Interlocked.Increment(ref _nextTravelId);
            Volatile.Write(ref _activeTravelId, id);
            _activePointType = pointType.ToString();
            bool traceEnabled = pointType == MapPointType.Shop;
            Volatile.Write(ref _traceActive, traceEnabled ? 1 : 0);
            __state = new TravelObservation(
                id,
                _activePointType,
                Stopwatch.StartNew(),
                traceEnabled);
            if (!traceEnabled)
                return;

            LogUtility.Info(
                $"MerchantTransition: starting stage=map-travel travel={id} point={pointType} "
                    + $"targetFloor={actFloor} save={saveGame} {NetworkContext()}"
            );
        }

        [HarmonyPostfix]
        private static void Postfix(ref Task __result, TravelObservation __state)
        {
            if (!__state.TraceEnabled)
                return;

            __result = ObserveTravel(__result, __state);
        }

        private static async Task ObserveTravel(Task task, TravelObservation observation)
        {
            await ObserveTask(
                task,
                $"map-travel/{observation.PointType}",
                observation.Timer,
                warnIfStalled: true
            );
        }
    }

    [HarmonyPatch(typeof(CombatStateSynchronizer), nameof(CombatStateSynchronizer.StartSync))]
    private static class TraceSyncStart
    {
        [HarmonyPrefix]
        private static void Prefix()
        {
            if (!TraceActive)
                return;

            LogUtility.Info(
                $"MerchantTransition: starting stage=multiplayer-sync {ActiveTravel} "
                    + NetworkContext()
            );
        }

        [HarmonyFinalizer]
        private static Exception? Finalizer(Exception? __exception)
        {
            if (TraceActive && __exception != null)
            {
                LogUtility.Error(
                    $"MerchantTransition: FAILED stage=multiplayer-sync-start {ActiveTravel} "
                        + $"{NetworkContext()} {__exception}"
                );
            }
            return __exception;
        }
    }

    [HarmonyPatch(typeof(CombatStateSynchronizer), nameof(CombatStateSynchronizer.WaitForSync))]
    private static class TraceSyncWait
    {
        [HarmonyPrefix]
        private static void Prefix(out Stopwatch? __state)
        {
            if (!TraceActive)
            {
                __state = null;
                return;
            }

            __state = Stopwatch.StartNew();
            LogUtility.Info(
                $"MerchantTransition: waiting stage=multiplayer-sync {ActiveTravel} "
                    + NetworkContext()
            );
        }

        [HarmonyPostfix]
        private static void Postfix(ref Task __result, Stopwatch? __state)
        {
            if (__state == null)
                return;

            __result = ObserveTask(
                __result,
                "multiplayer-sync",
                __state,
                warnIfStalled: true
            );
        }
    }

    [HarmonyPatch(typeof(MerchantInventory), nameof(MerchantInventory.CreateForNormalMerchant))]
    private static class TraceMerchantInventoryCreation
    {
        [HarmonyPrefix]
        private static void Prefix(Player player, out Stopwatch __state)
        {
            __state = Stopwatch.StartNew();
            LogUtility.Info(
                $"MerchantTransition: starting stage=inventory-create player={player.NetId} "
                    + $"local={IsLocalPlayer(player)} {ActiveTravel} {NetworkContext()}"
            );
        }

        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(
            Player player,
            MerchantInventory __result,
            Stopwatch __state)
        {
            LogUtility.Info(
                $"MerchantTransition: completed stage=inventory-create player={player.NetId} "
                    + $"local={IsLocalPlayer(player)} elapsed={__state.ElapsedMilliseconds}ms "
                    + $"cards={__result.CharacterCardEntries.Count}+{__result.ColorlessCardEntries.Count} "
                    + $"relics={__result.RelicEntries.Count} potions={__result.PotionEntries.Count} "
                    + $"stocked={__result.AllEntries.Count(entry => entry.IsStocked)} {ActiveTravel}"
            );
        }

        [HarmonyFinalizer]
        [HarmonyPriority(Priority.Last)]
        private static Exception? Finalizer(
            Exception? __exception,
            Player player,
            Stopwatch __state)
        {
            if (__exception != null)
            {
                LogUtility.Error(
                    $"MerchantTransition: FAILED stage=inventory-create player={player.NetId} "
                        + $"local={IsLocalPlayer(player)} elapsed={__state.ElapsedMilliseconds}ms "
                        + $"{ActiveTravel} {NetworkContext()} {__exception}"
                );
            }
            return __exception;
        }
    }

    [HarmonyPatch(typeof(SaveManager), nameof(SaveManager.SaveRun))]
    private static class TraceRunSave
    {
        [HarmonyPrefix]
        private static void Prefix(out Stopwatch? __state)
        {
            if (!TraceActive)
            {
                __state = null;
                return;
            }

            __state = Stopwatch.StartNew();
            LogUtility.Info(
                $"MerchantTransition: starting stage=run-save {ActiveTravel} "
                    + NetworkContext()
            );
        }

        [HarmonyPostfix]
        private static void Postfix(ref Task __result, Stopwatch? __state)
        {
            if (__state == null)
                return;

            __result = ObserveTask(__result, "run-save", __state, warnIfStalled: true);
        }
    }

    [HarmonyPatch(typeof(MerchantRoom), nameof(MerchantRoom.EnterInternal))]
    private static class TraceMerchantRoomEntry
    {
        [HarmonyPrefix]
        private static void Prefix(out Stopwatch __state)
        {
            // Also enables the remaining breadcrumbs when an Unknown map point rolls a shop.
            Volatile.Write(ref _traceActive, 1);
            __state = Stopwatch.StartNew();
            LogUtility.Info(
                $"MerchantTransition: starting stage=merchant-room-enter {ActiveTravel} "
                    + NetworkContext()
            );
        }

        [HarmonyPostfix]
        private static void Postfix(ref Task __result, Stopwatch __state)
        {
            __result = ObserveTask(
                __result,
                "merchant-room-enter",
                __state,
                warnIfStalled: true
            );
        }
    }

    [HarmonyPatch(typeof(PreloadManager), nameof(PreloadManager.LoadRoomMerchantAssets))]
    private static class TraceMerchantAssetPreload
    {
        [HarmonyPrefix]
        private static void Prefix(out Stopwatch __state)
        {
            __state = Stopwatch.StartNew();
            LogUtility.Info(
                $"MerchantTransition: starting stage=merchant-assets {ActiveTravel} "
                    + NetworkContext()
            );
        }

        [HarmonyPostfix]
        private static void Postfix(ref Task __result, Stopwatch __state)
        {
            __result = ObserveTask(
                __result,
                "merchant-assets",
                __state,
                warnIfStalled: true
            );
        }
    }

    [HarmonyPatch(typeof(NMerchantRoom), nameof(NMerchantRoom._Ready))]
    private static class TraceMerchantRoomNodeReady
    {
        [HarmonyPrefix]
        private static void Prefix()
        {
            LogUtility.Info(
                $"MerchantTransition: starting stage=merchant-node-ready {ActiveTravel} "
                    + NetworkContext()
            );
        }

        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        private static void Postfix()
        {
            LogUtility.Info(
                $"MerchantTransition: completed stage=merchant-node-ready {ActiveTravel} "
                    + NetworkContext()
            );
        }

        [HarmonyFinalizer]
        [HarmonyPriority(Priority.Last)]
        private static Exception? Finalizer(Exception? __exception)
        {
            if (__exception != null)
            {
                LogUtility.Error(
                    $"MerchantTransition: FAILED stage=merchant-node-ready {ActiveTravel} "
                        + $"{NetworkContext()} {__exception}"
                );
            }
            return __exception;
        }
    }

    [HarmonyPatch(typeof(NMerchantInventory), nameof(NMerchantInventory.Initialize))]
    private static class TraceMerchantInventoryNodeInitialization
    {
        [HarmonyPrefix]
        private static void Prefix(MerchantInventory inventory, out Stopwatch __state)
        {
            __state = Stopwatch.StartNew();
            LogUtility.Info(
                $"MerchantTransition: starting stage=inventory-node-initialize "
                    + $"page={PageKind(inventory)} owner={inventory.Player.NetId} "
                    + $"stocked={inventory.AllEntries.Count(entry => entry.IsStocked)} "
                    + $"{ActiveTravel} {NetworkContext()}"
            );
        }

        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(MerchantInventory inventory, Stopwatch __state)
        {
            LogUtility.Info(
                $"MerchantTransition: completed stage=inventory-node-initialize "
                    + $"page={PageKind(inventory)} owner={inventory.Player.NetId} "
                    + $"elapsed={__state.ElapsedMilliseconds}ms {ActiveTravel}"
            );
        }

        [HarmonyFinalizer]
        [HarmonyPriority(Priority.Last)]
        private static Exception? Finalizer(
            Exception? __exception,
            MerchantInventory inventory,
            Stopwatch __state)
        {
            if (__exception != null)
            {
                LogUtility.Error(
                    $"MerchantTransition: FAILED stage=inventory-node-initialize "
                        + $"page={PageKind(inventory)} owner={inventory.Player.NetId} "
                        + $"elapsed={__state.ElapsedMilliseconds}ms {ActiveTravel} "
                        + $"{NetworkContext()} {__exception}"
                );
            }
            return __exception;
        }

        private static string PageKind(MerchantInventory inventory)
        {
            try
            {
                return inventory.AllEntries.Any(Patches_ShopSanity.IsApSlot) ? "ap" : "vanilla";
            }
            catch (Exception ex)
            {
                return $"unknown({ex.GetType().Name})";
            }
        }
    }

    [HarmonyPatch(typeof(NTransition), nameof(NTransition.RoomFadeIn))]
    private static class TraceRoomFadeIn
    {
        [HarmonyPrefix]
        private static void Prefix(out Stopwatch? __state)
        {
            if (!TraceActive)
            {
                __state = null;
                return;
            }

            __state = Stopwatch.StartNew();
            LogUtility.Info(
                $"MerchantTransition: starting stage=room-fade-in {ActiveTravel} "
                    + NetworkContext()
            );
        }

        [HarmonyPostfix]
        private static void Postfix(ref Task __result, Stopwatch? __state)
        {
            if (__state == null)
                return;

            __result = ObserveTask(
                __result,
                "room-fade-in",
                __state,
                warnIfStalled: true
            );
        }
    }
}
