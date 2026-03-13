using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace StS2AP.Patches
{
    public static class NGamePatches
    {
        /// <summary>
        /// Forces the Ascension Level during `StartNewSingleplayerRun()`
        /// </summary>
        [HarmonyPatch(typeof(NGame), nameof(NGame.StartNewSingleplayerRun))]
        [HarmonyPrefix]
        static void StartNewSingleplayerRun_Prefix(ref int ascension)
        {
            int? overrideAscension = ArchipelagoClient.Settings?.AscensionLevel;
            if (overrideAscension.HasValue && overrideAscension.Value > 0)
            {
                ascension = overrideAscension.Value;
            }
        }

        /// <summary>
        /// Forces the Ascension Level during `InitializeSingleplayer()`
        /// </summary>
        [HarmonyPatch(typeof(NCharacterSelectScreen), nameof(NCharacterSelectScreen.InitializeSingleplayer))]
        [HarmonyPostfix]
        static void InitializeSingleplayer_Postfix(NCharacterSelectScreen __instance)
        {
            int overrideAscension = ArchipelagoClient.Settings?.AscensionLevel ?? 0;

            // Access the ascension panel and set it
            var ascensionPanelField = typeof(NCharacterSelectScreen).GetField("_ascensionPanel",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            if (ascensionPanelField?.GetValue(__instance) is NAscensionPanel ascensionPanel)
            {
                ascensionPanel.SetAscensionLevel(overrideAscension);
            }
        }
    }
}
