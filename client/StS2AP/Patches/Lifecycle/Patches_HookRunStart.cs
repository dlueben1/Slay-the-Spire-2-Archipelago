using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Game.Lobby;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves.Runs;
using MegaCrit.Sts2.Core.Unlocks;
using StS2AP.UI;
using StS2AP.Utils;

namespace StS2AP.Patches
{



    /// <summary>
    /// Patches for managing the current player reference and run lifecycle events for Archipelago.
    /// </summary>
    public static class Patches_HookRunStart
    {
        ///<summary>
        /// Sets up the character config on run start
        /// </summary>
        [HarmonyPatch(typeof(NGame), nameof(NGame.StartNewSingleplayerRun))]
        public static class OnRunPreStart
        {
            [HarmonyPrefix]
            public static void Prefix(CharacterModel character, ref int ascensionLevel, ref string seed)
            {
                var officialName = character.Id.Entry;
                ArchipelagoSettings? settings = ArchipelagoClient.Settings;
                if (settings == null
                    || !settings.Characters.TryGetValue(officialName, out CharacterConfig? config))
                {
                    string message = $"Cannot start the AP run because character settings for "
                        + $"'{officialName}' are unavailable.";
                    LogUtility.Error(message);
                    throw new InvalidOperationException(message);
                }
                GameUtility.CurrentConfig = config;
                if(GameUtility.CurrentConfig.Ascension.Count == 0)
                {
                    ascensionLevel = 0;
                }
                else
                {
                    // Not 100% sure this is correct, but in testing this didn't have a negative impact.
                    ascensionLevel = 10;
                }
                var configuredSeed = GameUtility.CurrentConfig.Seed;
                if(configuredSeed != null && configuredSeed.Length > 0)
                {
                    seed = configuredSeed;
                }
            }
        }

        /// <summary>
        /// Marks the run as multiplayer before MegaCrit creates every Player. This also covers
        /// invite/join paths that did not originate from the visible main-menu button.
        /// </summary>
        [HarmonyPatch(typeof(NGame), nameof(NGame.StartNewMultiplayerRun))]
        public static class OnMultiplayerRunPreStart
        {
            [HarmonyPrefix]
            public static void Prefix(StartRunLobby lobby)
            {
                MultiplayerSupport.SelectDestination(ApPlayDestination.Multiplayer);
                if (!MultiplayerSupport.CanEmbark(
                        BetaMainCompatibility.GetLocalCharacter(lobby),
                        out string blockedReason))
                {
                    NotificationUtility.ShowRawText(blockedReason);
                    throw new InvalidOperationException(
                        $"AP multiplayer run launch refused: {blockedReason}"
                    );
                }

                if (lobby.NetService.Type == NetGameType.Host
                    && !AscensionMultiplayer.IsLobbyConstructionPrepared(
                        lobby,
                        out blockedReason))
                {
                    NotificationUtility.ShowRawText(blockedReason);
                    throw new InvalidOperationException(
                        $"AP multiplayer ascension staging failed: {blockedReason}"
                    );
                }
            }
        }

        /// <summary>
        /// Does a bunch of work we need when a run starts, including caching references, resetting game state/progress, and hooking event listeners.
        /// </summary>
        [HarmonyPatch(typeof(Player), nameof(Player.CreateForNewRun), new Type[] { typeof(CharacterModel), typeof(UnlockState), typeof(ulong) })]
        public static class OnRunStart
        {
            [HarmonyPostfix]
            public static void Postfix(Player __result)
            {
                // A multiplayer process creates every player before LocalContext is ready.
                // Bind exactly once from RunManager.Launch instead.
                if (MultiplayerSupport.PendingDestination == ApPlayDestination.Multiplayer)
                    return;

                // Get rid of the tracker UI
                ArchipelagoCharTrackerUI.RemoveUI();
                ArchipelagoGoalTrackerUI.RemoveUI();

                // Grab a reference to the current player
                GameUtility.CurrentPlayer = __result;

                // Reset progress
                ArchipelagoClient.Progress.InitializeTrackers(__result);

                // Relic Coupons is a presentation-only starting relic backed by the saved bank.
                RelicCoupons.EnsureOwnedBy(__result);

                // At start of game, listen to Combat Manager
                //CombatManager.Instance.CombatWon -= GameUtility.OnCombatWin;
                //CombatManager.Instance.CombatWon += GameUtility.OnCombatWin;

                // Send "Press Start" check
                GameUtility.TrySendPressStartCheck();
            }
        }

        /// <summary>
        /// MegaCrit assigns LocalContext immediately before RunManager.Launch. This is the first
        /// clean point at which each process can bind its one AP session to its local STS player.
        /// </summary>
        [HarmonyPatch(typeof(RunManager), nameof(RunManager.Launch))]
        public static class BindLocalMultiplayerPlayer
        {
            [HarmonyPrefix]
            public static void Prefix(RunManager __instance)
            {
                if (__instance.NetService.Type == NetGameType.Singleplayer
                    || MultiplayerSupport.PendingDestination != ApPlayDestination.Multiplayer)
                {
                    return;
                }

                RunState? state = __instance.DebugOnlyGetState();
                if (state == null)
                {
                    const string unavailableReason = "The STS multiplayer run state was unavailable.";
                    NotificationUtility.ShowRawText(unavailableReason);
                    throw new InvalidOperationException(
                        $"AP multiplayer final launch check failed: {unavailableReason}"
                    );
                }

                if (!MultiplayerSupport.CanLaunchRun(state, out string blockedReason))
                {
                    NotificationUtility.ShowRawText(blockedReason);
                    throw new InvalidOperationException(
                        $"AP multiplayer final launch check failed: {blockedReason}"
                    );
                }
            }

            [HarmonyPostfix]
            public static void Postfix(RunState __result)
            {
                Player? localPlayer = MultiplayerSupport.BeginRun(__result);
                if (localPlayer == null)
                    return;
                if (MultiplayerSupport.ClaimsInvalidated)
                {
                    LogUtility.Error(
                        "Skipping AP multiplayer binding because the saved local identity did not match."
                    );
                    return;
                }

                AncientMultiplayer.BindRun(__result);
                StandardRelicPool.BindRun(__result);

                ArchipelagoCharTrackerUI.RemoveUI();
                ArchipelagoGoalTrackerUI.RemoveUI();
                GameUtility.CurrentPlayer = localPlayer;

                if (MultiplayerSupport.IsLocalGuest)
                {
                    GameUtility.CurrentConfig = null;
                    AscensionMultiplayer.BeginRun(__result, localPlayer);
                    LogUtility.Info(
                        $"Bound local multiplayer guest: netId={localPlayer.NetId}, "
                            + $"character={localPlayer.Character.Id.Entry}"
                    );
                    return;
                }

                string officialName = localPlayer.Character.Id.Entry;
                ArchipelagoSettings? settings = ArchipelagoClient.Settings;
                if (settings == null)
                {
                    const string reason = "AP slot settings are unavailable for the local multiplayer player";
                    LogUtility.Error(reason);
                    MultiplayerSupport.InvalidateRunClaims(reason);
                    return;
                }
                if (!settings.Characters.TryGetValue(
                    officialName,
                    out CharacterConfig? config
                ))
                {
                    LogUtility.Error(
                        $"Local multiplayer character {officialName} is not configured for "
                            + "this AP slot; AP rewards are disabled for this run"
                    );
                    MultiplayerSupport.InvalidateRunClaims(
                        $"local character {officialName} is not configured for this AP slot"
                    );
                    return;
                }

                GameUtility.CurrentConfig = config;
                bool restoredProgress = ApRunData.RestoreLocalProgress(localPlayer);
                if (!restoredProgress)
                {
                    ArchipelagoClient.Progress = new ArchipelagoProgress();
                    ArchipelagoClient.Progress.ResetTrackers();
                    ArchipelagoClient.Progress.Ascensions.Initialize(config);
                }
                if (!MultiplayerSupport.RestorePreparedReceiptView(out string receiptError))
                {
                    MultiplayerSupport.InvalidateRunClaims(receiptError);
                    return;
                }
                AscensionMultiplayer.SyncLocalProjection(__result);
                RelicCoupons.EnsureOwnedBy(localPlayer);
                if (!ApRunData.PublishLocalProgress(localPlayer))
                {
                    MultiplayerSupport.InvalidateRunClaims(
                        "initial AP progress could not be published to the host"
                    );
                    return;
                }
                if (!ApGrantDispatcher.BeginRun(__result, config.CharOffset, out string bindError))
                {
                    MultiplayerSupport.InvalidateRunClaims(bindError);
                    return;
                }
                if (!ApMirroredRewardDispatcher.BeginRun(__result, out bindError))
                {
                    MultiplayerSupport.InvalidateRunClaims(bindError);
                    return;
                }
                AscensionMultiplayer.BeginRun(__result, localPlayer);
                ProgressiveStarterMultiplayer.BeginRun(__result, localPlayer);
                if (MultiplayerSupport.IsLocalOwnApSlot)
                    PendingCheckUtility.ReconcileAndSend();
                if (MultiplayerSupport.IsLocalOwnApSlot
                    && MultiplayerSupport.IsFeatureEnabled(MultiplayerFeature.PressStartCheck))
                {
                    GameUtility.TrySendPressStartCheck();
                }

                LogUtility.Info(
                    $"Bound local AP multiplayer player: netId={localPlayer.NetId}, "
                        + $"character={officialName}, slot={ArchipelagoClient.PlayerName}"
                );
            }
        }

        /// <summary>
        /// Reconciles progressive starters after the base game has finalized starting relic effects,
        /// but before it launches the run scene. At this point each player has a real RunState and
        /// relic removal can pair AfterRemoved with the base game's completed AfterObtained call.
        /// </summary>
        [HarmonyPatch(typeof(RunManager), nameof(RunManager.FinalizeStartingRelics))]
        public static class OnStartingRelicsFinalized
        {
            [HarmonyPostfix]
            public static void Postfix(ref Task __result)
            {
                __result = ReconcileProgressiveStarters(__result);
            }

            private static async Task ReconcileProgressiveStarters(Task finalizeTask)
            {
                await finalizeTask;

                // Multiplayer authors concrete per-player recipes after AP ownership is bound in
                // RunManager.Launch; its mutations travel through non-combat managed actions.
                if (MultiplayerSupport.IsRealMultiplayerRun)
                    return;

                if (!MultiplayerSupport.IsFeatureEnabled(MultiplayerFeature.ProgressiveStarters))
                    return;

                var player = GameUtility.CurrentPlayer;
                var runState = RunManager.Instance.DebugOnlyGetState();
                if (player == null || !ReferenceEquals(player.RunState, runState))
                    return;

                await ProgressiveStarterUtility.InitializeForRun(player);
            }
        }

        /// <summary>
        /// Similar to `OnRunStart` but only happens on loading a run. We don't have to initialize anything, but we still need to do some work.
        /// </summary>
        [HarmonyPatch(typeof(Player), nameof(Player.FromSerializable), new Type[] { typeof(SerializablePlayer) })]
        public static class OnRunLoad
        {
            [HarmonyPostfix]
            public static void Postfix()
            {
                // Get rid of the tracker UI
                ArchipelagoCharTrackerUI.RemoveUI();
                ArchipelagoGoalTrackerUI.RemoveUI();
            }
        }

        /// <summary>
        /// Clears the CurrentPlayer reference when the run ends to avoid stale state.
        /// </summary>
        [HarmonyPatch(typeof(NGame), nameof(NGame.ReturnToMainMenu))]
        public static class ClearPlayerOnReturnToMenu
        {
            [HarmonyPostfix]
            public static void Postfix()
            {
                ArchipelagoRewardUI.RemoveUI();
                MultiplayerSupport.EndRun();
                MultiplayerSupport.ClearPendingPlaySelection();
                GameUtility.CurrentPlayer = null;
                GameUtility.CurrentConfig = null;
                LogUtility.Info("CurrentPlayer cleared (returned to main menu)");
            }
        }
    }
}
