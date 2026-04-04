using Archipelago.MultiClient.Net.Enums;
using Archipelago.MultiClient.Net.Models;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Audio;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Multiplayer;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Managers;
using StS2AP.Extensions;
using StS2AP.Models;
using StS2AP.UI;
using StS2AP.Utils;
using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace StS2AP.Patches
{
    
    public static class Patches_RunSaveManager
    {
        [HarmonyPatch(typeof(RunSaveManager), "SaveRun")]
        public static class SaveRun
        {
            [HarmonyPrefix]
            public static bool replaceSave(AbstractRoom? preFinishedRoom, ref Task __result)
            {
                LogUtility.Info($"Game attempted to save in room of type '{preFinishedRoom?.RoomType}'");
                LogUtility.Info($"Current room type {RunManager.Instance.DebugOnlyGetState()?.CurrentRoom?.RoomType}");
                LogUtility.Info($"Current Map node type {RunManager.Instance.DebugOnlyGetState()?.CurrentMapPoint?.PointType}");
                LogUtility.Info($"Game thinks we should save: {RunManager.Instance.ShouldSave}");
                // Goal is to just save on boss kills, treasure rooms, and after ancient selections
                if (!RunManager.Instance.ShouldSave ||
                    (RunManager.Instance.NetService.Type != MegaCrit.Sts2.Core.Multiplayer.Game.NetGameType.Singleplayer && RunManager.Instance.NetService.Type != MegaCrit.Sts2.Core.Multiplayer.Game.NetGameType.Host)
                    || (preFinishedRoom?.RoomType != RoomType.Boss
                    && RunManager.Instance.DebugOnlyGetState()?.CurrentMapPoint?.PointType != MapPointType.Treasure
                    && !(preFinishedRoom?.RoomType == RoomType.Event && RunManager.Instance.DebugOnlyGetState()?.CurrentMapPoint?.PointType == MapPointType.Ancient)))
                {
                    LogUtility.Info($"Skipping save {preFinishedRoom?.RoomType}");
                    __result = Task.CompletedTask;
                    return false;
                }

                LogUtility.Info("Saving to AP");
                SerializableRun saveMe = RunManager.Instance.ToSave(preFinishedRoom);
                __result = asyncSave(saveMe);
                return false;
            }

            public static async Task asyncSave(SerializableRun saveMe)
            {
                var result = JsonSerializer.Serialize(saveMe, JsonSerializationUtility.GetTypeInfo<SerializableRun>());
                var zipped = Zip(result);
                if(GameUtility.CurrentPlayer == null)
                {
                    return;
                }
                var saveDict = new Dictionary<string, string>();
                saveDict[GameUtility.CurrentPlayer.APName()] = zipped;
                ArchipelagoClient.Session.DataStorage[Scope.Slot, $"StS2AP_Saves"]
                    += Operation.Update(saveDict);
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

    public static class Patches_SerializableRun
    {
        [HarmonyPatch(typeof(SerializableRun), "Serialize")]
        public static class SaveAP
        {
            [HarmonyPostfix]
            public static void APSave(PacketWriter writer)
            {
                ArchipelagoClient.Progress.Serialize(writer);
            }
        }

        [HarmonyPatch(typeof(SerializableRun), "Deserialize")]
        public static class LoadAP
        {
            [HarmonyPostfix]
            public static void APLoad(PacketReader reader)
            {
                ArchipelagoClient.Progress = reader.Read<ArchipelagoProgress>();
            }
        }
    }

    public static class Patches_NCharacterSelectScreen
    {
        [HarmonyPatch(typeof(NCharacterSelectScreen), "OnEmbarkPressed")]
        public static class MaybeLoadAP
        {

            [HarmonyPrefix]
            public static bool intercept(NCharacterSelectScreen __instance)
            {

                var charName = __instance.Lobby.LocalPlayer.character.GetType().Name;
                foreach(var entry in GameUtility.APSaves)
                {
                    if (entry.Value.Length > 0)
                    {
                        LogUtility.Info($"Have save for {entry.Key}");
                    }
                }
                if(GameUtility.APSaves.TryGetValue(charName, out var saveData) && saveData != null && saveData.Length > 0)
                {
                    LogUtility.Info($"AP Save detected for character {charName}");
                    var popup = new ConfirmPopup();
                    popup.Header = new LocString("main_menu_ui", "CONTINUE_RUN.header");
                    popup.Body = new LocString("main_menu_ui", "CONTINUE_RUN.body");
                    popup.ButtonPressed = (yesPressed) =>
                    {
                        if(yesPressed)
                        {
                            _ = ContinueRun(__instance);
                        }
                        else
                        {
                            __instance.Lobby.SetReady(ready: true);
                        }
                    };
                    NModalContainer.Instance.Add(popup.Popup);
                    popup.Show();
                    return false;
                }
                LogUtility.Info($"No AP Save detected for character {charName}");
                return true;
            }

            private static async Task ContinueRun(NCharacterSelectScreen _charSelect)
            {
                try
                {
                    NAudioManager.Instance?.StopMusic();
                    string saveStr;
                    var charName = _charSelect.Lobby.LocalPlayer.character.GetType().Name;
                    if (GameUtility.APSaves.TryGetValue(charName, out saveStr))
                    {
                        var unzipped = Patches_RunSaveManager.SaveRun.Unzip(saveStr);
                        ReadSaveResult<SerializableRun> result = JsonSerializationUtility.FromJson<SerializableRun>(unzipped);
                        if (!result.Success)
                        {
                            LogUtility.Error($"Failed to load save {result.ErrorMessage}");
                            _charSelect.Lobby.SetReady(ready: true);
                            return;
                        }
                        SerializableRun serializableRun = result.SaveData;
                        RunState runState = RunState.FromSerializable(serializableRun);
                        RunManager.Instance.SetUpSavedSinglePlayer(runState, serializableRun);
                        Log.Info($"Continuing run with character: {serializableRun.Players[0].CharacterId}");
                        SfxCmd.Play(runState.Players[0].Character.CharacterTransitionSfx);

                        GameUtility.CurrentPlayer = runState.Players[0];

                        await NGame.Instance.Transition.FadeOut(0.8f, runState.Players[0].Character.CharacterSelectTransitionPath);
                        NGame.Instance.ReactionContainer.InitializeNetworking(new NetSingleplayerGameService());
                        await NGame.Instance.LoadRun(runState, serializableRun.PreFinishedRoom);
                        await NGame.Instance.Transition.FadeIn();
                    }
                }
                catch (Exception ex)
                {
                    LogUtility.Error($"Failed to load AP save: {ex.Message}");
                }
                LogUtility.Error("Somehow got here, but we don't have a save, starting the run");
                _charSelect.Lobby.SetReady(ready: true);
            }
        }
    }
}
