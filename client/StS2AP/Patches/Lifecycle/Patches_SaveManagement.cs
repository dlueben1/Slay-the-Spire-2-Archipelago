using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Audio;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Managers;
using StS2AP.Data;
using StS2AP.Extensions;
using StS2AP.UI;
using StS2AP.Utils;
using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace StS2AP.Patches
{
    
    public static class Patches_RunSaveManager
    {
        [HarmonyPatch(typeof(RunSaveManager), nameof(RunSaveManager.SaveRun), new[] { typeof(AbstractRoom) })]
        public static class SaveRun
        {
            [HarmonyPrefix]
            public static bool replaceSave(RunSaveManager __instance, AbstractRoom? preFinishedRoom, ref Task __result)
            {
                if (MultiplayerSupport.IsRealMultiplayerRun)
                {
                    // MegaCrit owns the multiplayer save and RitsuLib embeds the authoritative
                    // shared/per-player AP payload into that same host checkpoint.
                    if (RunManager.Instance.NetService.Type
                        == MegaCrit.Sts2.Core.Multiplayer.Game.NetGameType.Host)
                    {
                        if (!RunManager.Instance.ShouldSave)
                        {
                            __result = Task.CompletedTask;
                            return false;
                        }

                        ApRunData.CaptureLocalHostProgressBeforeSave();
                        // Every native floor save updates recovery. Only an eligible AP boundary
                        // also advances the separate checkpoint; carry that decision across await.
                        bool isApCheckpoint = TryGetCheckpointEligibility(preFinishedRoom, out _, out _, out _);
                        SerializableRun snapshot = RunManager.Instance.ToSave(preFinishedRoom);
                        __result = ApMultiplayerCampaignStore.SaveHostSnapshot(__instance, snapshot, isApCheckpoint);
                        return false;
                    }

                    __result = Task.CompletedTask;
                    return false;
                }

                if (!ApSingleplayerSaves.IsHandlingSingleplayerRun) return true;

                LogUtility.Info($"Game attempted to save in room of type '{preFinishedRoom?.RoomType}'");
                LogUtility.Info($"Current room type {RunManager.Instance.DebugOnlyGetState()?.CurrentRoom?.RoomType}");
                LogUtility.Info($"Current Map node type {RunManager.Instance.DebugOnlyGetState()?.CurrentMapPoint?.PointType}");
                LogUtility.Info($"Game thinks we should save: {RunManager.Instance.ShouldSave}");

                // Save after boss kills, in treasure rooms, and after ancient selections.
                bool isEligibleCheckpoint = TryGetCheckpointEligibility(
                    preFinishedRoom,
                    out bool isBossAutosave,
                    out bool isTreasureAutosave,
                    out string checkpointReason
                );
                if ((RunManager.Instance.NetService.Type != MegaCrit.Sts2.Core.Multiplayer.Game.NetGameType.Singleplayer && RunManager.Instance.NetService.Type != MegaCrit.Sts2.Core.Multiplayer.Game.NetGameType.Host)
                    || !isEligibleCheckpoint)
                {
                    LogUtility.Info(
                        $"Skipping save {preFinishedRoom?.RoomType}: {checkpointReason}"
                    );
                    __result = Task.CompletedTask;
                    return false;
                }

                LogUtility.Info("Preparing AP checkpoint save");
                SerializableRun saveMe = RunManager.Instance.ToSave(preFinishedRoom);
                ApSingleplayerSaves.Save(saveMe, isBossAutosave ? "boss" : isTreasureAutosave ? "treasure" : "ancient");
                __result = Task.CompletedTask;
                return false;
            }

            private static bool TryGetCheckpointEligibility(
                AbstractRoom? preFinishedRoom,
                out bool isBossAutosave,
                out bool isTreasureAutosave,
                out string reason)
            {
                ArchipelagoSettings? settings = ArchipelagoClient.Settings;
                if (settings == null)
                {
                    isBossAutosave = false;
                    isTreasureAutosave = false;
                    reason = "the Archipelago slot settings are unavailable";
                    return false;
                }
                int maxSaveAct = ArchipelagoClient.Progress.MaxProgressiveAncientLevel(
                    GameUtility.CurrentConfig?.CharOffset ?? -1
                );
                int currentAct = (GameUtility.CurrentPlayer?.RunState.CurrentActIndex ?? 0) + 1;
                MapPointType? currentMapPointType = RunManager
                    .Instance.DebugOnlyGetState()
                    ?.CurrentMapPoint?.PointType;
                // Act 3 has no later supported checkpoint. Preserve its treasure-room save
                // instead of replacing it after either Act 3 boss.
                isBossAutosave = preFinishedRoom?.RoomType == RoomType.Boss && currentAct < 3;
                isTreasureAutosave = currentMapPointType == MapPointType.Treasure;
                bool isEligibleSaveLocation =
                    isBossAutosave
                    || isTreasureAutosave
                    || (
                        // Keep multiplayer Ancient checkpoints unchanged. Singleplayer saves only
                        // the initial Ancient; Acts 2 and 3 use the preceding boss checkpoints.
                        (MultiplayerSupport.IsRealMultiplayerRun || currentAct == 1)
                        && preFinishedRoom?.RoomType == RoomType.Event
                        && currentMapPointType == MapPointType.Ancient
                    );
                AncientRelicLocation ancientRelicLocation = AncientSettingsUtility.Current.Location;
                bool usesProgressiveAncients =
                    settings.APWorldVersion > Constants.VERSION_0_5_3;
                bool ancientIsLocked =
                    usesProgressiveAncients
                    && ancientRelicLocation == AncientRelicLocation.StartOfAct
                    && currentAct > 1
                    && maxSaveAct < currentAct;

                LogUtility.Info(
                    $"Max Act: {maxSaveAct} Current Act: {currentAct} "
                        + $"AncientRelicLocation: {ancientRelicLocation}"
                );

                if (!RunManager.Instance.ShouldSave)
                    reason = "the run is not currently saveable";
                else if (!isEligibleSaveLocation)
                    reason = "this room is not an AP checkpoint";
                else if (ancientIsLocked)
                    reason = "the progressive Ancient checkpoint is locked";
                else
                    reason = string.Empty;
                return reason.Length == 0;
            }

            /// <summary>
            /// Serializes a vanilla run together with all run-scoped Archipelago progress.
            /// Local checkpoints keep native and AP state in this same envelope.
            /// </summary>
            public static string SerializeAndCompress(SerializableRun vanillaSave)
            {
                var save = ArchipelagoClient.Progress.ToSerializable(vanillaSave);
                var json = JsonSerializer.Serialize(
                    save,
                    SerializationUtility.CombinedOptions.GetTypeInfo(typeof(SerializableAP))
                );
                return Zip(json);
            }

            public static string Zip(string str)
            {
                // https://stackoverflow.com/a/7343623
                var bytes = Encoding.UTF8.GetBytes(str);

                using (var msi = new MemoryStream(bytes))
                using (var mso = new MemoryStream())
                {
                    using (var gs = new GZipStream(mso, CompressionMode.Compress))
                    { 
                        msi.CopyTo(gs);
                    }

                    return Convert.ToBase64String(mso.ToArray());
                }
            }

            public static string Unzip(string base64Str)
            {
                using (var msi = new MemoryStream(Convert.FromBase64String(base64Str)))
                using (var mso = new MemoryStream())
                {
                    using (var gs = new GZipStream(msi, CompressionMode.Decompress))
                    {
                        gs.CopyTo(mso);
                    }

                    return Encoding.UTF8.GetString(mso.ToArray());
                }
            }
        }
    }

    public static class Patches_NCharacterSelectScreen
    {
        /// <summary>
        /// Restores a compressed Archipelago save and reconstructs both the vanilla run and
        /// all run-scoped AP state. Local checkpoints share this path.
        /// </summary>
        internal static async Task RestoreRun(string compressedSave, string saveDescription, string expectedCharacter)
        {
            var unzipped = Patches_RunSaveManager.SaveRun.Unzip(compressedSave);
            SerializableAP? apSave = JsonSerializer.Deserialize<SerializableAP>(
                unzipped,
                SerializationUtility.CombinedOptions
            );
            JsonElement? saveData = apSave?.SaveData;
            if (
                apSave == null
                || apSave.PlayerNumber != CoopSlot.PlayerNumber
                || apSave.Progress is not { Initialized: true }
                || saveData == null
                || saveData.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined
            )
            {
                throw new InvalidDataException(
                    $"{saveDescription} does not contain current AP run progress and save data"
                );
            }

            ReadSaveResult<SerializableRun> runResult =
                JsonSerializationUtility.FromJson<SerializableRun>(saveData.Value.GetRawText());
            if (!runResult.Success || runResult.SaveData == null)
            {
                throw new InvalidDataException(
                    $"Failed to deserialize {saveDescription}: "
                        + (runResult.ErrorMessage ?? runResult.Status.ToString())
                );
            }

            SerializableRun serializableRun = runResult.SaveData;
            if (serializableRun.Players.Count != 1
                || serializableRun.Players[0].CharacterId?.Entry != expectedCharacter)
                throw new InvalidDataException("The checkpoint character does not match the selected AP run.");
            RunState runState = RunState.FromSerializable(serializableRun);
            NAudioManager.Instance?.StopMusic();
            await RunManager.Instance.SetUpSavedSingleplayer(runState, serializableRun);
            Log.Info(
                $"Continuing run from {saveDescription} with character: "
                    + serializableRun.Players[0].CharacterId
            );
            SfxCmd.Play(runState.Players[0].Character.CharacterTransitionSfx);

            Player currentPlayer = runState.Players[0];
            GameUtility.CurrentPlayer = currentPlayer;
            ArchipelagoSettings settings = ArchipelagoClient.Settings
                ?? throw new InvalidOperationException(
                    "Cannot continue the AP run because slot settings are unavailable"
                );
            if (!settings.Characters.TryGetValue(
                    currentPlayer.getInternalName(),
                    out CharacterConfig? currentConfig))
            {
                throw new InvalidDataException(
                    $"Cannot continue the AP run because character settings for "
                        + $"'{currentPlayer.getInternalName()}' are unavailable"
                );
            }
            GameUtility.CurrentConfig = currentConfig;
            ArchipelagoClient.Progress = ArchipelagoProgress.FromSerializable(
                apSave,
                currentPlayer
            );
            RelicCoupons.EnsureOwnedBy(currentPlayer, silent: true);
            // Rebuild these counts from AP history.
            ArchipelagoClient.Progress.ProgressiveAncients.Clear();
            ArchipelagoClient.Progress.ProgressiveRests.Clear();
            ArchipelagoClient.Progress.ProgressiveSmiths.Clear();
            Patches_ItemProcessor.ReprocessItems();
            RelicRewardUtility.ReconcileBankedRewards(currentPlayer);
            ArchipelagoClient.Progress.InitializeFromServer(currentPlayer);

            NGame game = NGame.Instance
                ?? throw new InvalidOperationException(
                    "Cannot continue the AP run because the game singleton is unavailable"
                );
            var transition = game.Transition
                ?? throw new InvalidOperationException(
                    "Cannot continue the AP run because the game transition controller is unavailable"
                );
            await transition.FadeOut(
                0.8f,
                runState.Players[0].Character.CharacterSelectTransitionPath
            );
            game.ReactionContainer.InitializeNetworking(new NetSingleplayerGameService());
            await game.LoadRun(runState, serializableRun.PreFinishedRoom);
            await transition.FadeIn();
        }

        [HarmonyPatch(typeof(NCharacterSelectScreen), "OnEmbarkPressed")]
        public static class MaybeLoadAP
        {

            [HarmonyPrefix]
            public static bool intercept(NCharacterSelectScreen __instance)
            {
                if (MultiplayerSupport.PendingDestination == ApPlayDestination.Multiplayer)
                    return true;

                var character = BetaMainCompatibility.GetLocalCharacter(__instance.Lobby);
                if (!ArchipelagoClient.CanSelectCharacter(character, out string blockedReason))
                {
                    __instance.Lobby.SetReady(ready: false);
                    __instance.GetNode<NConfirmButton>("ConfirmButton").Disable();
                    LogUtility.Warn($"Blocked AP singleplayer embark: {blockedReason}");
                    NotificationUtility.ShowRawText(blockedReason);
                    return false;
                }

                ApSingleplayerCheckpointPicker.Show(__instance, character.Id.Entry);
                return false;
            }
        }
    }
}
