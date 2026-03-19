using HarmonyLib;
using Godot;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Entities.Ascension;
using System.Reflection;
using StS2AP.Utils;
using MegaCrit.Sts2.Core.Entities.Players;

namespace StS2AP.Patches
{
    /// <summary>
    /// Patches for to override ascension UI behavior and values for Archipelago.
    /// </summary>
    public static class Patches_AscensionOverride
    {
        #region Set In-Game Ascenion Level

        /// <summary>
        /// Override `Player`'s `MaxAscensionWhenRunStarted` with the Ascension Level
        /// </summary>
        [HarmonyPatch(typeof(Player), nameof(Player.MaxAscensionWhenRunStarted), MethodType.Getter)]
        public static class OverrideMaxAscensionWhenRunStarted
        {
            [HarmonyPostfix]
            public static void Postfix(ref int __result)
            {
                __result = ArchipelagoClient.Settings?.AscensionLevel ?? __result;
            }
        }

        /// <summary>
        /// Sets the Ascension Level at the start of a run
        /// </summary>
        [HarmonyPatch(typeof(NGame), nameof(NGame.StartNewSingleplayerRun))]
        public static class ForceAscensionOnGameStart
        {
            [HarmonyPrefix]
            public static void Prefix(ref int ascension)
            {
                int? overrideAscension = ArchipelagoClient.Settings?.AscensionLevel;
                if (overrideAscension.HasValue && overrideAscension.Value > 0)
                {
                    ascension = overrideAscension.Value;
                }
            }
        }

        /// <summary>
        /// Overrides the Ascension Level of an AscensionManager instance during integer constructor.
        /// Overrides the constructor that takes an int parameter, which is used in the copy constructor.
        /// </summary>
        [HarmonyPatch(typeof(AscensionManager), MethodType.Constructor, new[] { typeof(int) })]
        public static class OverrideAscensionManagerInt
        {
            private static readonly FieldInfo s_levelField =
                typeof(AscensionManager).GetField("_level", BindingFlags.Instance | BindingFlags.NonPublic);

            [HarmonyPostfix]
            public static void Postfix(AscensionManager __instance, int level)
            {
                if (s_levelField == null || __instance == null) return;

                // Prefer the value from Archipelago settings if available; otherwise use original value
                var desired = ArchipelagoClient.Settings?.AscensionLevel ?? level;
                LogUtility.Debug($"Patching Ascension Level to {desired}");
                s_levelField.SetValue(__instance, desired);
            }
        }

        /// <summary>
        /// Overrides the Ascension Level of an AscensionManager instance during enum constructor.
        /// Overrides the constructor that takes an AscensionLevel enum parameter, which is used in the copy constructor.
        /// </summary>
        [HarmonyPatch(typeof(AscensionManager), MethodType.Constructor, new[] { typeof(AscensionLevel) })]
        public static class OverrideAscensionManagerEnum
        {
            private static readonly FieldInfo s_levelField =
                typeof(AscensionManager).GetField("_level", BindingFlags.Instance | BindingFlags.NonPublic);

            [HarmonyPostfix]
            public static void Postfix(AscensionManager __instance, AscensionLevel level)
            {
                if (s_levelField == null || __instance == null) return;

                // Prefer the value from Archipelago settings if available; otherwise use original value
                var desired = ArchipelagoClient.Settings?.AscensionLevel ?? (int)level;
                LogUtility.Debug($"Patching Ascension Level to {desired}");
                s_levelField.SetValue(__instance, desired);
            }
        }

        #endregion

        #region Update Ascension-Related UI

        /// <summary>
        /// Hides the Ascension Arrows from the UI during Character Select
        /// </summary>
        [HarmonyPatch(typeof(NAscensionPanel))]
        public static class HideAscensionArrows
        {
            [HarmonyPatch("RefreshArrowVisibility")]
            [HarmonyPostfix]
            public static void Postfix(NAscensionPanel __instance)
            {
                // Access Left/Right Ascension Modifying Arrows
                var leftField = AccessTools.Field(typeof(NAscensionPanel), "_leftArrow");
                var rightField = AccessTools.Field(typeof(NAscensionPanel), "_rightArrow");
                var leftObj = leftField?.GetValue(__instance) as Control;
                var rightObj = rightField?.GetValue(__instance) as Control;

                if (leftObj != null)
                {
                    leftObj.Visible = false;
                }

                if (rightObj != null)
                {
                    rightObj.Visible = false;
                }
            }
        }

        /// <summary>
        /// Overrides the Max Ascension for the Character Select Screen UI
        /// </summary>
        [HarmonyPatch(typeof(NAscensionPanel), nameof(NAscensionPanel.SetMaxAscension))]
        public static class OverrideMaxAscensionOnUI
        {
            [HarmonyPrefix]
            public static void Prefix(ref int maxAscension)
            {
                if (ArchipelagoClient.Settings == null) return;
                maxAscension = ArchipelagoClient.Settings.AscensionLevel;
            }
        }

        /// <summary>
        /// Overrides the Ascension for the Character Select Screen UI
        /// </summary>
        [HarmonyPatch(typeof(NAscensionPanel), nameof(NAscensionPanel.SetAscensionLevel))]
        public static class OverrideAscensionOnUI
        {
            [HarmonyPrefix]
            public static void Prefix(ref int ascension)
            {
                if (ArchipelagoClient.Settings == null) return;
                ascension = ArchipelagoClient.Settings.AscensionLevel;
            }
        }

        /// <summary>
        /// Forces the Ascension Level during Character Select Screen initialization.
        /// Honestly not sure how this is different from `OverrideAscensionOnUI` but it seems like we need both (maybe).
        /// </summary>
        [HarmonyPatch(typeof(NCharacterSelectScreen), nameof(NCharacterSelectScreen.InitializeSingleplayer))]
        public static class ForceAscensionOnCharacterSelect
        {
            [HarmonyPostfix]
            public static void Postfix(NCharacterSelectScreen __instance)
            {
                int overrideAscension = ArchipelagoClient.Settings?.AscensionLevel ?? 0;

                var ascensionPanelField = typeof(NCharacterSelectScreen).GetField("_ascensionPanel",
                    BindingFlags.NonPublic | BindingFlags.Instance);

                if (ascensionPanelField?.GetValue(__instance) is NAscensionPanel ascensionPanel)
                {
                    ascensionPanel.SetAscensionLevel(overrideAscension);
                }
            }
        }

        #endregion
    }
}