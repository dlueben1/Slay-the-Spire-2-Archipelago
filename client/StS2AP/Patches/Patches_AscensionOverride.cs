using Godot;
using HarmonyLib;
using MegaCrit.sts2.Core.Nodes.TopBar;
using MegaCrit.Sts2.addons.mega_text;
using MegaCrit.Sts2.Core.Entities.Ascension;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.HoverTips;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Runs;
using StS2AP.Models;
using StS2AP.Utils;
using System.Reflection;

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
        //[HarmonyPatch(typeof(Player), nameof(Player.MaxAscensionWhenRunStarted), MethodType.Getter)]
        //public static class OverrideMaxAscensionWhenRunStarted
        //{
        //    [HarmonyPostfix]
        //    public static void Postfix(ref int __result)
        //    {
        //        // TODO: update to max ascension for character?
        //        __result = ArchipelagoClient.Settings?.AscensionLevel ?? __result;
        //    }
        //}

        ///// <summary>
        ///// Overrides the "Preferred Ascension" level for a character, which is used in the Character Select screen and other places.
        ///// </summary>
        //[HarmonyPatch(typeof(CharacterStats), nameof(CharacterStats.PreferredAscension), MethodType.Getter)]
        //public static class OverridePreferredAscension
        //{
        //    static void Postfix(ref int __result)
        //    {
        //        __result = ArchipelagoClient.Settings?.AscensionLevel ?? __result;
        //    }
        //}

        ///// <summary>
        ///// Overrides the "Preferred Ascension" level for "multiplayer", which I *suspect* is also used by the random character select option.
        ///// </summary>
        //[HarmonyPatch(typeof(ProgressState), nameof(ProgressState.PreferredMultiplayerAscension), MethodType.Getter)]
        //public static class OverridePreferredAscensionMultiplayer
        //{
        //    static void Postfix(ref int __result)
        //    {
        //        __result = ArchipelagoClient.Settings?.AscensionLevel ?? __result;
        //    }
        //}

        ///// <summary>
        ///// Sets the Ascension Level at the start of a run
        ///// </summary>
        //[HarmonyPatch(typeof(NGame), nameof(NGame.StartNewSingleplayerRun))]
        //public static class ForceAscensionOnGameStart
        //{
        //    [HarmonyPrefix]
        //    public static void Prefix(ref int ascensionLevel)
        //    {
        //        int? overrideAscension = ArchipelagoClient.Settings?.AscensionLevel;
        //        if (overrideAscension.HasValue)
        //        {
        //            ascensionLevel = overrideAscension.Value;
        //        }
        //        else
        //        {
        //            // if we somehow don't have an override, 
        //            ascensionLevel = 1;
        //        }
        //    }
        //}

        [HarmonyPatch(typeof(NTopBarPortraitTip), "OnFocus")]
        public static class ChangeHoverTip
        {
            [HarmonyPrefix]
            public static bool Prefix(NTopBarPortraitTip __instance)
            {
                if(__instance.ShowTip)
                {
                    NHoverTipSet.CreateAndShow(__instance, ArchipelagoClient.Progress.Ascensions.HoverTip)
                        ?.SetGlobalPosition(__instance.GlobalPosition + new Vector2(0f, __instance.Size.Y + 20f));
                }
                return false;
            }
        }

        private static MegaLabel _ascensionLabel;
        public static MegaLabel? AscensionLabel
        {
            get
            {
                if(_ascensionLabel == null || !GodotObject.IsInstanceValid(_ascensionLabel))
                {
                    return null;
                }
                return _ascensionLabel;
            }
        }

        public static void ChangeAscensionLabel(String newText)
        {
            Callable.From(() => AscensionLabel?.SetTextAutoSize(newText)).CallDeferred();
        }

        [HarmonyPatch(typeof(NTopBar), "Initialize")]
        public static class CaptureAscensionLabel
        {
            [HarmonyPostfix]
            public static void PostFix(MegaLabel ____ascensionLabel)
            {
                _ascensionLabel = ____ascensionLabel;
                ChangeAscensionLabel(ArchipelagoClient.Progress.Ascensions.CurrentAscension.Count.ToString());
            }
        }


        ///// <summary>
        ///// Overrides the Ascension Level of an AscensionManager instance during integer constructor.
        ///// Overrides the constructor that takes an int parameter, which is used in the copy constructor.
        ///// </summary>
        //[HarmonyPatch(typeof(AscensionManager), MethodType.Constructor, new[] { typeof(int) })]
        //public static class OverrideAscensionManagerInt
        //{
        //    private static readonly FieldInfo s_levelField =
        //        typeof(AscensionManager).GetField("_level", BindingFlags.Instance | BindingFlags.NonPublic);

        //    [HarmonyPostfix]
        //    public static void Postfix(AscensionManager __instance, int level)
        //    {
        //        if (s_levelField == null || __instance == null) return;

        //        // Prefer the value from Archipelago settings if available; otherwise use original value
        //        var desired = ArchipelagoClient.Settings?.AscensionLevel ?? level;
        //        LogUtility.Debug($"Patching Ascension Level to {desired}");
        //        s_levelField.SetValue(__instance, desired);
        //    }
        //}

        ///// <summary>
        ///// Overrides the Ascension Level of an AscensionManager instance during enum constructor.
        ///// Overrides the constructor that takes an AscensionLevel enum parameter, which is used in the copy constructor.
        ///// </summary>
        //[HarmonyPatch(typeof(AscensionManager), MethodType.Constructor, new[] { typeof(AscensionLevel) })]
        //public static class OverrideAscensionManagerEnum
        //{
        //    private static readonly FieldInfo s_levelField =
        //        typeof(AscensionManager).GetField("_level", BindingFlags.Instance | BindingFlags.NonPublic);

        //    [HarmonyPostfix]
        //    public static void Postfix(AscensionManager __instance, AscensionLevel level)
        //    {
        //        if (s_levelField == null || __instance == null) return;

        //        // Prefer the value from Archipelago settings if available; otherwise use original value
        //        var desired = ArchipelagoClient.Settings?.AscensionLevel ?? (int)level;
        //        LogUtility.Debug($"Patching Ascension Level to {desired}");
        //        s_levelField.SetValue(__instance, desired);
        //    }
        //}

        ///<summary>
        /// Changes the AscensionManager lookup to check an inmemory hashset for whether a particular level is toggled.
        /// </summary>
        [HarmonyPatch(typeof(MegaCrit.Sts2.Core.Entities.Ascension.AscensionManager), "HasLevel")]
        public static class InGameAscensionOverride
        {
            [HarmonyPostfix]
            public static void Postfix(AscensionLevel level, ref bool __result)
            {
                // TODO: does this need to check if we're in a game?
                __result = ArchipelagoClient.Progress.Ascensions.CurrentAscension.Contains(level);
            }
        }
        
        [HarmonyPatch(typeof(SavedActMap), nameof(SavedActMap.SecondBossMapPoint), MethodType.Getter)]
        public static class DisableSecondBossMapPointSaved
        {
            [HarmonyPostfix]
            public static void Postfix(ref MapPoint? __result)
            {
                if (!ArchipelagoClient.Progress.Ascensions.CurrentAscension.Contains(AscensionLevel.DoubleBoss))
                {
                    __result = null;
                }
            }

        }

        [HarmonyPatch(typeof(SerializableActMap), nameof(SerializableActMap.SecondBossPoint), MethodType.Getter)]
        public static class DisableSecondBossMapPointSerializable
        {
            [HarmonyPostfix]
            public static void Postfix(ref SerializableMapPoint? __result)
            {
                if (!ArchipelagoClient.Progress.Ascensions.CurrentAscension.Contains(AscensionLevel.DoubleBoss))
                {
                    __result = null;
                }
            }

        }

        [HarmonyPatch(typeof(StandardActMap), nameof(StandardActMap.SecondBossMapPoint), MethodType.Getter)]
        public static class DisableSecondBossMapPoint
        {
            [HarmonyPostfix]
            public static void Postfix(ref MapPoint? __result)
            {
                if (!ArchipelagoClient.Progress.Ascensions.CurrentAscension.Contains(AscensionLevel.DoubleBoss))
                {
                    __result = null;
                }
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

        ///// <summary>
        ///// Overrides the Max Ascension for the Character Select Screen UI
        ///// </summary>
        //[HarmonyPatch(typeof(NAscensionPanel), nameof(NAscensionPanel.SetMaxAscension))]
        //public static class OverrideMaxAscensionOnUI
        //{
        //    [HarmonyPrefix]
        //    public static void Prefix(ref int maxAscension)
        //    {
        //        if (ArchipelagoClient.Settings == null) return;
        //        maxAscension = 10;
        //    }
        //}

        ///// <summary>
        ///// Overrides the Ascension for the Character Select Screen UI
        ///// </summary>
        //[HarmonyPatch(typeof(NAscensionPanel), nameof(NAscensionPanel.SetAscensionLevel))]
        //public static class OverrideAscensionOnUI
        //{
        //    [HarmonyPrefix]
        //    public static void Prefix(ref int ascension)
        //    {
        //        if (ArchipelagoClient.Settings == null) return;
        //        ascension = 10;
        //    }
        //}

        ///// <summary>
        ///// Forces the Ascension Level during Character Select Screen initialization.
        ///// Honestly not sure how this is different from `OverrideAscensionOnUI` but it seems like we need both (maybe).
        ///// </summary>
        //[HarmonyPatch(typeof(NCharacterSelectScreen), nameof(NCharacterSelectScreen.InitializeSingleplayer))]
        //public static class ForceAscensionOnCharacterSelect
        //{
        //    [HarmonyPostfix]
        //    public static void Postfix(NCharacterSelectScreen __instance)
        //    {
        //        int overrideAscension = ArchipelagoClient.Settings?.AscensionLevel ?? 0;

        //        var ascensionPanelField = typeof(NCharacterSelectScreen).GetField("_ascensionPanel",
        //            BindingFlags.NonPublic | BindingFlags.Instance);

        //        if (ascensionPanelField?.GetValue(__instance) is NAscensionPanel ascensionPanel)
        //        {
        //            ascensionPanel.SetAscensionLevel(overrideAscension);
        //        }
        //    }
        //}

        #endregion
    }
}