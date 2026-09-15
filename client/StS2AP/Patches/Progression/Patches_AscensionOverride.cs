using Godot;
using HarmonyLib;
using MegaCrit.sts2.Core.Nodes.TopBar;
using MegaCrit.Sts2.addons.mega_text;
using MegaCrit.Sts2.Core.Entities.Ascension;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.HoverTips;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;
using MegaCrit.Sts2.Core.Saves.Runs;
using MegaCrit.Sts2.Core.Models;
using StS2AP.Data;
using StS2AP.Utils;

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
                // AP_MP: Ascension presentation stays native until the shared set is staged.
                if (!MultiplayerSupport.ShouldRunReplicatedConstruction(
                        MultiplayerFeature.AscensionEffects))
                    return true;

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
                // AP_MP: Ascension UI overrides require a host-authoritative shared set.
                if (!MultiplayerSupport.ShouldRunReplicatedConstruction(
                        MultiplayerFeature.AscensionEffects))
                    return;

                _ascensionLabel = ____ascensionLabel;
                ChangeAscensionLabel(AscensionMultiplayer.GetCurrentCount().ToString());
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
                // AP_MP: Ascension queries require a host-authoritative shared set.
                if (!MultiplayerSupport.ShouldRunReplicatedConstruction(
                        MultiplayerFeature.AscensionEffects))
                    return;

                if (AscensionMultiplayer.TryHasLevel(level, out bool enabled))
                    __result = enabled;
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
                // AP_MP: Double-boss map changes must be identical on every peer.
                if (!MultiplayerSupport.ShouldRunReplicatedConstruction(
                        MultiplayerFeature.AscensionEffects))
                    return;

                if (AscensionMultiplayer.TryHasLevel(
                        AscensionLevel.DoubleBoss,
                        out bool enabled)
                    && !enabled)
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
                // AP_MP: Double-boss serialization waits for synchronized ascension state.
                if (!MultiplayerSupport.ShouldRunReplicatedConstruction(
                        MultiplayerFeature.AscensionEffects))
                    return;

                if (AscensionMultiplayer.TryHasLevel(
                        AscensionLevel.DoubleBoss,
                        out bool enabled)
                    && !enabled)
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
                // AP_MP: Double-boss restore waits for synchronized ascension state.
                if (!MultiplayerSupport.ShouldRunReplicatedConstruction(
                        MultiplayerFeature.AscensionEffects))
                    return;

                if (AscensionMultiplayer.TryHasLevel(
                        AscensionLevel.DoubleBoss,
                        out bool enabled)
                    && !enabled)
                {
                    __result = null;
                }
            }

        }

        #endregion

        #region Update Ascension-Related UI

        /// <summary>
        /// Shows the configured AP ascension count for the selected character without
        /// changing the base game's ascension or lobby state.
        /// </summary>
        public static void UpdateCharacterSelectAscension(
            NCharacterSelectScreen screen,
            NCharacterSelectButton characterButton,
            CharacterModel character
        )
        {
            ArchipelagoSettings? settings = ArchipelagoClient.Settings;
            if (settings == null
                || !MultiplayerSupport.IsFeatureEnabled(MultiplayerFeature.AscensionEffects))
                return;

            var panel = AccessTools.Field(typeof(NCharacterSelectScreen), "_ascensionPanel")
                ?.GetValue(screen) as NAscensionPanel;
            if (panel == null)
            {
                return;
            }

            if (characterButton.IsLocked
                || !settings.Characters.TryGetValue(
                    character.Id.Entry,
                    out var config
                ))
            {
                panel.Visible = false;
                return;
            }

            var levelLabel = AccessTools.Field(typeof(NAscensionPanel), "_ascensionLevel")
                ?.GetValue(panel) as MegaLabel;
            if (levelLabel == null)
            {
                return;
            }

            levelLabel.SetTextAutoSize(CountEffectiveAscensions(config).ToString());
            panel.Visible = true;
        }

        private static int CountEffectiveAscensions(CharacterConfig config)
        {
            var effectiveAscensions = new HashSet<AscensionLevel>();
            foreach (var configuredAscension in config.Ascension)
            {
                var level = Utils.AscensionManager.GetLevel(configuredAscension);
                if (level.HasValue)
                {
                    effectiveAscensions.Add(level.Value);
                }
            }

            foreach (var receivedItem in ArchipelagoClient.Progress.AllReceivedItems)
            {
                var item = receivedItem.Item;
                if (!ArchipelagoIdCodec.IsCharacterItemId(item.ItemId)
                    || item.GetAPCharacterNumber() != config.CharOffset)
                {
                    continue;
                }

                var itemId = item.GetCharacterItemType();
                if ((int)itemId < 19 || (int)itemId > 28)
                {
                    continue;
                }

                effectiveAscensions.Remove(Utils.AscensionManager.ToAscensionLevel(itemId));
            }

            return effectiveAscensions.Count;
        }

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
                // AP_MP: Ascension controls require host overwrite and mismatch diagnostics.
                if (!MultiplayerSupport.IsFeatureEnabled(MultiplayerFeature.AscensionEffects))
                    return;

                // Access Left/Right Ascension Modifying Arrows
                Control? leftObj = __instance._leftArrow;
                Control? rightObj = __instance._rightArrow;

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
