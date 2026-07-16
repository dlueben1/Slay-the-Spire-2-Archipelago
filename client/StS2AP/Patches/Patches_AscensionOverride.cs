using Godot;
using HarmonyLib;
using MegaCrit.sts2.Core.Nodes.TopBar;
using MegaCrit.Sts2.addons.mega_text;
using MegaCrit.Sts2.Core.Entities.Ascension;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.HoverTips;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves.Runs;

namespace StS2AP.Patches
{
    /// <summary>
    /// Patches for to override ascension UI behavior and values for Archipelago.
    /// </summary>
    public static class Patches_AscensionOverride
    {
        #region Set In-Game Ascension Level


        /// <summary>
        /// Sets the hover tooltip based on the currently enabled ascensions.
        /// </summary>
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

        private static MegaLabel? _ascensionLabel;
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

        /// <summary>
        /// Sets the Ascension number in the top left during a run to be the number of enabled ascensions.
        /// </summary>
        public static void ChangeAscensionLabel(String newText)
        {
            Callable.From(() => AscensionLabel?.SetTextAutoSize(newText)).CallDeferred();
        }


        /// <summary>
        /// Captures the ascension number label, so we can change the number.
        /// </summary>
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


        ///<summary>
        /// Changes the AscensionManager lookup to check an in memory Set for whether a particular level is toggled.
        /// During a run, everything gets piped to this method.  There are some things that happen outside of runs, but
        /// we mostly don't care.
        /// </summary>
        [HarmonyPatch(typeof(MegaCrit.Sts2.Core.Entities.Ascension.AscensionManager), "HasLevel")]
        public static class InGameAscensionOverride
        {
            [HarmonyPostfix]
            public static void Postfix(AscensionLevel level, ref bool __result)
            {
                if(!RunManager.Instance.IsInProgress)
                {
                    // Not sure we can trust the CurrentAscension Set in this case or not.
                    return;
                }
                __result = ArchipelagoClient.Progress.Ascensions.HasLevel(level);
            }
        }
        
        /// <summary>
        /// Helps disable Double Boss when that ascension was active, but is no longer.
        /// </summary>
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

        /// <summary>
        /// Helps disable Double Boss when that ascension was active, but is no longer.
        /// </summary>
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

        /// <summary>
        /// Helps disable Double Boss when that ascension was active, but is no longer.
        /// </summary>
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


        #endregion
    }
}