using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.HoverTips;
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
        /// <summary>
        /// Reference to the AP Button Control
        /// </summary>
        private static Button? _apButton;

        /// <summary>
        /// Label showing the reward count in the bottom right corner of the button
        /// </summary>
        private static Label? _countLabel;

        /// <summary>
        /// Tooltip for the AP Button
        /// </summary>
        private static readonly HoverTip _hoverTip = new HoverTip(new LocString("static_hover_tips", "AP_BTN.title"), new LocString("static_hover_tips", "AP_BTN.description"));

        /// <summary>
        /// Animation to play when focusing on the button
        /// </summary>
        private static Tween? _focusTween;

        /// <summary>
        /// Animation to play when unfocusing from the button
        /// </summary>
        private static Tween? _unfocusTween;

        static Tween? _oscillateTween;

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

                // Create the count label as a child of topBar (not container) so it floats above
                _countLabel = CreateCountLabel();
                topBar.AddChild(_countLabel);

                // Position the label in the bottom right corner of the button after layout
                _apButton.Ready += () => UpdateCountLabelPosition();
                _apButton.Resized += () => UpdateCountLabelPosition();

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
                CustomMinimumSize = new Vector2(75, 50)
            };

            // Remove background for all button states
            var emptyStyle = new StyleBoxEmpty();
            button.AddThemeStyleboxOverride("normal", emptyStyle);
            button.AddThemeStyleboxOverride("hover", emptyStyle);
            button.AddThemeStyleboxOverride("pressed", emptyStyle);
            button.AddThemeStyleboxOverride("focus", emptyStyle);
            button.AddThemeStyleboxOverride("disabled", emptyStyle);

            // Set pivot offset to the center for rotation animation around center
            button.PivotOffset = button.CustomMinimumSize / 2;

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

            // Hook events
            button.Pressed += OnAPButtonPressed;
            button.FocusEntered += OnFocus;
            button.MouseEntered += OnFocus;
            button.FocusExited += OnUnfocus;
            button.MouseExited += OnUnfocus;
            return button;
        }

        private static Label CreateCountLabel()
        {
            var label = new Label
            {
                Name = "ArchipelagoCountLabel",
                Text = $"{ArchipelagoClient.Progress.UnusedItemCount}",
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                CustomMinimumSize = new Vector2(30, 24),
                MouseFilter = Control.MouseFilterEnum.Ignore,
                ZIndex = 1000
            };

            // Set top-left anchoring
            label.SetAnchorsPreset(Control.LayoutPreset.TopLeft);

            // Style the label with larger font and make it visible
            label.AddThemeFontSizeOverride("font_size", 24);
            label.AddThemeColorOverride("font_color", new Color(1, 1, 1));
            label.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, 0.8f));
            label.AddThemeConstantOverride("shadow_offset_x", 2);
            label.AddThemeConstantOverride("shadow_offset_y", 2);

            return label;
        }

        /// <summary>
        /// Updates the position of the count label to stay in the bottom right corner of the button
        /// </summary>
        private static void UpdateCountLabelPosition()
        {
            if (_apButton == null || _countLabel == null) return;

            // Position label at bottom right corner of button using global position
            _countLabel.GlobalPosition = _apButton.GlobalPosition + new Vector2(
                _apButton.Size.X - _countLabel.Size.X,
                _apButton.Size.Y - _countLabel.Size.Y
            );
        }

        /// <summary>
        /// Sets the count displayed on the label (1-3 digits)
        /// </summary>
        /// <param name="count">The count to display. If 0 or negative, the label is hidden.</param>
        public static void SetCount(int count)
        {
            if (_countLabel == null) return;

            if (count <= 0)
            {
                _countLabel.Text = "";
                _countLabel.Visible = false;
            }
            else
            {
                // Clamp to 3 digits max (999)
                _countLabel.Text = Math.Min(count, 999).ToString();
                _countLabel.Visible = true;
                UpdateCountLabelPosition();
            }
        }

        /// <summary>
        /// Show tooltip and play an animation on focus
        /// </summary>
        private static void OnFocus()
        {
            // Null check
            if(_apButton == null) return;

            // Show the tooltip
            try
            {
                NHoverTipSet nHoverTipSet = NHoverTipSet.CreateAndShow(_apButton, _hoverTip);
                nHoverTipSet.GlobalPosition = _apButton.GlobalPosition + new Vector2(_apButton.Size.X - nHoverTipSet.Size.X, _apButton.Size.Y + 20f);
            }
            catch (Exception ex)
            {
                LogUtility.Error($"Failed to show tooltip: {ex.Message}");
            }

            // Start animating
            StartOscillation();
        }

        /// <summary>
        /// Hide tooltip and stop animating on exit focus
        /// </summary>
        private static void OnUnfocus()
        {
            if(_apButton == null) return;

            // Hide the tooltip
            try
            {
                NHoverTipSet.Remove(_apButton);
            }
            catch (Exception ex)
            {
                LogUtility.Error($"Failed to hide tooltip: {ex.Message}");
            }

            // Stop animating
            StopOscillation();
        }

        public static void StartOscillation()
        {
            _oscillateTween?.Kill();
            _oscillateTween = _apButton.CreateTween();
            _oscillateTween.SetLoops();
            _oscillateTween.TweenProperty(_apButton, "rotation", -0.12f, 0.8).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
            _oscillateTween.TweenProperty(_apButton, "rotation", 0.12f, 0.8).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
        }

        public static void StopOscillation()
        {
            _oscillateTween?.Kill();
            _oscillateTween = _apButton.CreateTween();
            _oscillateTween.TweenProperty(_apButton, "rotation", 0f, 0.5).SetTrans(Tween.TransitionType.Spring).SetEase(Tween.EaseType.Out);
        }

        /// <summary>
        /// Opens the Reward List
        /// </summary>
        private static void OnAPButtonPressed()
        {
            LogUtility.Info("Opening Archipelago Rewards UI...");

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
