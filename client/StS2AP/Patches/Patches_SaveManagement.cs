using Archipelago.MultiClient.Net.Enums;
using Archipelago.MultiClient.Net.Models;
using HarmonyLib;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
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
                if(!RunManager.Instance.ShouldSave || 
                    (RunManager.Instance.NetService.Type != MegaCrit.Sts2.Core.Multiplayer.Game.NetGameType.Singleplayer && RunManager.Instance.NetService.Type != MegaCrit.Sts2.Core.Multiplayer.Game.NetGameType.Host)
                    || preFinishedRoom == null ||
                    (preFinishedRoom.RoomType != RoomType.Boss
                    && preFinishedRoom.RoomType != RoomType.Treasure))
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

                if(GameUtility.APSaves.ContainsKey(charName))
                {
                    LogUtility.Info($"AP Save detected for character {charName}");
                    //var popup = 
                    NModalContainer.Instance.Add(new ContinueSaveUI(__instance));
                    // TODO: create the popup that prompts for a save load
                    return false;
                }
                LogUtility.Info($"No AP Save detected for character {charName}");
                return true;
            }
        }
    }
}
