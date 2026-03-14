using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.TopBar;
using StS2AP.Utils;
using System;
using System.Collections.Generic;
using System.IO;

namespace StS2AP.UI
{
    /// <summary>
    /// class that creates and manages the Archipelago Reward Top Bar button.
    /// </summary>
    public static class ArchipelagoTopBarUI
    {
        private static Button? _apButton;

        /// <summary>
        /// Injects the Archipelago button into the top bar next to the map button.
        /// Called by the Harmony Postfix below.
        /// </summary>
        public static void InjectButton(NTopBar topBar)
        {
            try
            {
                // Finding the Map Button to use as a position anchor
                var mapBtn = FindChildByType<NTopBarMapButton>(topBar);
                if (mapBtn == null)
                {
                    LogUtility.Info("[AP] Could not find MapButton anchor.");
                    return;
                }

                var container = mapBtn.GetParent() as Container;
                if (container == null)
                {
                    LogUtility.Error("[AP] MapButton parent is not a Container.");
                    return;
                }

                // fixing the issue of duplicate buttons if the TopBar reloads
                if (_apButton != null && GodotObject.IsInstanceValid(_apButton) && _apButton.GetParent() == container)
                {
                    return;
                }

                // Create and add the button to the layout
                _apButton = CreateAPButton();
                container.AddChild(_apButton);

                // Place it right next to the map button
                int mapIndex = mapBtn.GetIndex();
                container.MoveChild(_apButton, Math.Max(0, mapIndex));

                LogUtility.Success("Archipelago TopBar button injected successfully!");
            }
            catch (Exception ex)
            {
                LogUtility.Error($"Failed to inject TopBar UI: {ex.Message}");
            }
        }

        private static Button CreateAPButton()
        {
            var button = new Button
            {
                Name = "ArchipelagoButton",
                CustomMinimumSize = new Vector2(50, 50)
            };

            // Attempt to load the icon
            var tex = GD.Load<Texture2D>("res://images/APIcon.png");

            if (tex != null)
            {
                button.Icon = tex;
                button.ExpandIcon = true;
            }
            else
            {
                button.Text = "AP";
            }

            button.Pressed += OnAPButtonPressed;
            return button;
        }

        /// <summary>
        /// Opens the Reward List
        /// </summary>
        private static void OnAPButtonPressed()
        {
            LogUtility.Info("Opening Archipelago Rewards UI...");
            // Simple dummy rewards for testing visuals (IGNORE)
            // var dummyRewards = new List<string> { "Received: Shuriken", "Received: 50 Gold" };

            // This call now handles its own injection if the UI is missing!
            ArchipelagoRewardUI.ShowRewards();
        }

        private static Texture2D? LoadExternalTexture(string relativePath)
        {
            string basePath = OS.GetExecutablePath().GetBaseDir();
            string fullPath = Path.Combine(basePath, relativePath);

            if (!File.Exists(fullPath))
            {
                LogUtility.Info($"[AP] Texture not found at: {fullPath}");
                return null;
            }

            var img = Image.LoadFromFile(fullPath);
            return ImageTexture.CreateFromImage(img);
        }

        private static T? FindChildByType<T>(Node parent) where T : Node
        {
            foreach (var child in parent.GetChildren())
            {
                if (child is T typedChild) return typedChild;
                var result = FindChildByType<T>(child);
                if (result != null) return result;
            }
            return null;
        }
    }

    /// <summary>
    /// Harmony patch to trigger the TopBar UI injection.
    /// </summary>
    [HarmonyPatch(typeof(NTopBar), "_Ready")]
    public static class TopBarInjectionPatch
    {
        [HarmonyPostfix]
        public static void Postfix(NTopBar __instance)
        {
            ArchipelagoTopBarUI.InjectButton(__instance);
        }
    }
}
