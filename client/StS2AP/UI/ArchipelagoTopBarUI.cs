using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.HoverTips;
using MegaCrit.Sts2.Core.Nodes.TopBar;
using StS2AP.Utils;
using System;

namespace StS2AP.UI
{
    /// <summary>
    /// Static class that creates and manages the Archipelago button in the game's top bar.
    /// The button displays an icon with an item count badge and opens the
    /// <see cref="ArchipelagoRewardUI"/> reward screen when pressed.
    /// Injected into the top bar via the <see cref="TopBarInjectionPatch"/> Harmony postfix.
    /// </summary>
    public static class ArchipelagoTopBarUI
    {
        #region Node References

        /// <summary>The injected Archipelago button placed inside the top bar container.</summary>
        private static Button? _button;

        /// <summary>
        /// Floating label that shows the unclaimed reward count in the bottom-right corner of the button.
        /// Parented to the <see cref="NTopBar"/> (not the container) so it renders above sibling controls.
        /// </summary>
        private static Label? _countLabel;

        /// <summary>Active tween used for the oscillation (wiggle) animation on focus.</summary>
        private static Tween? _oscillateTween;

        #endregion

        #region Constants

        /// <summary>URI Resource path to the bold Kreon font used by the count label.</summary>
        private const string FontBold = "res://themes/kreon_bold_glyph_space_two.tres";

        /// <summary>URI Resource path to the Archipelago icon texture displayed on the button.</summary>
        private const string IconPath = "res://images/APIcon.png";

        // Count label style
        private const int CountFontSize    = 24;
        private const int CountOutlineSize = 10;

        /// <summary>Vertical padding subtracted when positioning the count label at the button's bottom edge.</summary>
        private const int CountLabelPadding = 4;

        // Oscillation animation parameters
        private const float OscillationAngle    = 0.12f;
        private const float OscillationDuration = 0.3f;
        private const float SettleDuration      = 0.5f;

        /// <summary>Maximum displayable count (clamped to three digits).</summary>
        private const int MaxDisplayCount = 999;

        /// <summary>Vertical offset between the button's bottom edge and the tooltip.</summary>
        private const float TooltipOffsetY = 20f;

        #endregion

        #region Tooltip

        /// <summary>Hover tooltip shown when the player focuses or hovers over the button.</summary>
        private static readonly HoverTip _hoverTip = new HoverTip(
            new LocString("static_hover_tips", "AP_BTN.title"),
            new LocString("static_hover_tips", "AP_BTN.description"));

        #endregion

        #region Public API

        /// <summary>
        /// Injects the Archipelago button into the top bar next to the map button.
        /// Called by <see cref="TopBarInjectionPatch"/> each time the <see cref="NTopBar"/> becomes ready.
        /// Safely no-ops if the button is already present in the same container.
        /// </summary>
        /// <param name="topBar">The <see cref="NTopBar"/> instance that just entered the scene tree.</param>
        public static void InjectButton(NTopBar topBar)
        {
            try
            {
                // Locate the map button to use as a position anchor
                var mapButton = FindChildByType<NTopBarMapButton>(topBar);
                if (mapButton == null)
                {
                    LogUtility.Info("[AP] Could not find MapButton anchor.");
                    return;
                }

                var container = mapButton.GetParent() as Container;
                if (container == null)
                {
                    LogUtility.Error("[AP] MapButton parent is not a Container.");
                    return;
                }

                // Guard against duplicate injection if the top bar reloads
                if (_button != null && GodotObject.IsInstanceValid(_button) && _button.GetParent() == container)
                    return;

                // Create and insert the button immediately before the map button
                _button = CreateButton();
                container.AddChild(_button);
                container.MoveChild(_button, Math.Max(0, mapButton.GetIndex()));

                // Count label is parented to topBar (not the container) so it floats above siblings
                _countLabel = CreateCountLabel();
                topBar.AddChild(_countLabel);

                // Keep the count label pinned after layout changes
                _button.Ready   += () => RepositionCountLabel();
                _button.Resized += () => RepositionCountLabel();

                LogUtility.Success("Archipelago TopBar button injected successfully!");
            }
            catch (Exception ex)
            {
                LogUtility.Error($"Failed to inject TopBar UI: {ex.Message}");
            }
        }

        /// <summary>
        /// Updates the unclaimed reward count displayed on the button badge.
        /// Hides the label when the count is zero or negative.
        /// 
        /// This SETS the value to whatever `count` is. If you simply want to refresh it, use `RefreshCount()`, which calculates what it SHOULD be!
        /// </summary>
        /// <param name="count">The number of unclaimed rewards. Values above <see cref="MaxDisplayCount"/> are clamped.</param>
        public static void SetCount(int count)
        {
            if (_countLabel == null) return;

            if (count <= 0)
            {
                _countLabel.Text    = "";
                _countLabel.Visible = false;
            }
            else
            {
                _countLabel.Text    = Math.Min(count, MaxDisplayCount).ToString();
                _countLabel.Visible = true;
                RepositionCountLabel();
            }
        }

        /// <summary>
        /// Recalculates the number of unused items, and sets the count label to it
        /// </summary>
        public static void RefreshCount()
        {
            // Get the total number of unused items from the progress tracker
            int availableCount = ArchipelagoClient.Progress.UnusedItemCount;
            if (ArchipelagoClient.Progress.GoldRemaining > 0) availableCount++;

            // Update the label
            SetCount(availableCount);
        }

        #endregion

        #region UI Construction

        /// <summary>
        /// Creates the Archipelago top bar button with a transparent background,
        /// the AP icon texture, and all signal handlers wired up.
        /// </summary>
        /// <returns>A fully configured <see cref="Button"/> ready to be added to the top bar.</returns>
        private static Button CreateButton()
        {
            var button = new Button
            {
                Name              = "ArchipelagoButton",
                CustomMinimumSize = new Vector2(75, 50)
            };

            // Remove the default theme background for every button state
            var emptyStyle = new StyleBoxEmpty();
            button.AddThemeStyleboxOverride("normal",   emptyStyle);
            button.AddThemeStyleboxOverride("hover",    emptyStyle);
            button.AddThemeStyleboxOverride("pressed",  emptyStyle);
            button.AddThemeStyleboxOverride("focus",    emptyStyle);
            button.AddThemeStyleboxOverride("disabled", emptyStyle);

            // Centre the pivot so the oscillation rotates around the middle
            button.PivotOffset = button.CustomMinimumSize / 2;

            // Load the AP icon; fall back to text if the texture is missing
            var tex = GD.Load<Texture2D>(IconPath);
            if (tex != null)
            {
                button.Icon       = tex;
                button.ExpandIcon = true;
            }
            else
            {
                button.Text = "AP";
            }

            // Wire signal handlers
            button.Pressed      += OnButtonPressed;
            button.FocusEntered += OnButtonFocused;
            button.MouseEntered += OnButtonFocused;
            button.FocusExited  += OnButtonUnfocused;
            button.MouseExited  += OnButtonUnfocused;

            return button;
        }

        /// <summary>
        /// Creates the floating count badge label styled with the game's bold font,
        /// white text, a dark outline, and a drop shadow for readability.
        /// </summary>
        /// <returns>A configured <see cref="Label"/> showing the current <see cref="ArchipelagoClient.Progress.UnusedItemCount"/>.</returns>
        private static Label CreateCountLabel()
        {
            var label = new Label
            {
                Name                = "ArchipelagoCountLabel",
                Text                = $"{ArchipelagoClient.Progress.UnusedItemCount}",
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment   = VerticalAlignment.Center,
                CustomMinimumSize   = new Vector2(30, 24),
                MouseFilter         = Control.MouseFilterEnum.Ignore,
                ZIndex              = 1
            };

            label.SetAnchorsPreset(Control.LayoutPreset.TopLeft);

            // Font style
            label.AddThemeFontSizeOverride("font_size", CountFontSize);
            label.AddThemeColorOverride("font_color",         new Color(1f, 1f, 1f));
            label.AddThemeColorOverride("font_shadow_color",  new Color(0f, 0f, 0f, 0.8f));
            label.AddThemeConstantOverride("shadow_offset_x", 2);
            label.AddThemeConstantOverride("shadow_offset_y", 2);
            label.AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f, 0.7529f));
            label.AddThemeConstantOverride("outline_size",    CountOutlineSize);

            // Attempt to use the in-game bold font
            try
            {
                var font = GD.Load<Font>(FontBold);
                if (font != null)
                    label.AddThemeFontOverride("font", font);
                else
                    LogUtility.Warn($"Could not load unused item count label font: {FontBold}");
            }
            catch (Exception ex)
            {
                LogUtility.Warn($"Failed to load unused item count label font: {ex.Message}");
            }

            return label;
        }

        #endregion

        #region Event Handlers

        /// <summary>
        /// Handles the button press by opening the <see cref="ArchipelagoRewardUI"/> reward screen.
        /// </summary>
        private static void OnButtonPressed()
        {
            LogUtility.Info("Opening Archipelago Rewards UI...");
            ArchipelagoRewardUI.ShowRewards();
        }

        /// <summary>
        /// Called when the button gains keyboard focus or the mouse enters.
        /// Shows the hover tooltip and starts the wiggle animation.
        /// </summary>
        private static void OnButtonFocused()
        {
            if (_button == null) return;

            // Show the tooltip anchored below the button
            try
            {
                var tipSet = NHoverTipSet.CreateAndShow(_button, _hoverTip);
                tipSet.GlobalPosition = _button.GlobalPosition + new Vector2(
                    _button.Size.X - tipSet.Size.X,
                    _button.Size.Y + TooltipOffsetY);
            }
            catch (Exception ex)
            {
                LogUtility.Error($"Failed to show tooltip: {ex.Message}");
            }

            StartOscillation();
        }

        /// <summary>
        /// Called when the button loses keyboard focus or the mouse exits.
        /// Hides the hover tooltip and settles the rotation back to zero.
        /// </summary>
        private static void OnButtonUnfocused()
        {
            if (_button == null) return;

            try
            {
                NHoverTipSet.Remove(_button);
            }
            catch (Exception ex)
            {
                LogUtility.Error($"Failed to hide tooltip: {ex.Message}");
            }

            StopOscillation();
        }

        #endregion

        #region Animations

        /// <summary>
        /// Starts an infinite sine-wave oscillation (wiggle) on the button's rotation.
        /// Any existing oscillation tween is killed first.
        /// </summary>
        private static void StartOscillation()
        {
            _oscillateTween?.Kill();
            _oscillateTween = _button!.CreateTween();
            _oscillateTween.SetLoops();
            _oscillateTween.TweenProperty(_button, "rotation", -OscillationAngle, OscillationDuration)
                .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
            _oscillateTween.TweenProperty(_button, "rotation", OscillationAngle, OscillationDuration)
                .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
        }

        /// <summary>
        /// Stops the oscillation and smoothly returns the button's rotation to zero
        /// using a spring ease-out transition.
        /// </summary>
        private static void StopOscillation()
        {
            _oscillateTween?.Kill();
            _oscillateTween = _button!.CreateTween();
            _oscillateTween.TweenProperty(_button, "rotation", 0f, SettleDuration)
                .SetTrans(Tween.TransitionType.Spring).SetEase(Tween.EaseType.Out);
        }

        #endregion

        #region Helpers

        /// <summary>
        /// Repositions the count label so it sits in the bottom-right corner of the button.
        /// Uses global coordinates so the label tracks correctly regardless of container layout.
        /// </summary>
        private static void RepositionCountLabel()
        {
            if (_button == null || _countLabel == null) return;

            _countLabel.GlobalPosition = _button.GlobalPosition + new Vector2(
                _button.Size.X - _countLabel.Size.X,
                _button.Size.Y - _countLabel.Size.Y - CountLabelPadding);
        }

        /// <summary>
        /// Recursively searches a node's children for the first descendant of type <typeparamref name="T"/>.
        /// </summary>
        /// <typeparam name="T">The <see cref="Node"/> subtype to locate.</typeparam>
        /// <param name="parent">The root node to begin the search from.</param>
        /// <returns>The first matching descendant, or <c>null</c> if none is found.</returns>
        private static T? FindChildByType<T>(Node parent) where T : Node
        {
            foreach (var child in parent.GetChildren())
            {
                if (child is T match) return match;
                var result = FindChildByType<T>(child);
                if (result != null) return result;
            }
            return null;
        }

        #endregion
    }

    /// <summary>
    /// Harmony postfix patch on <see cref="NTopBar._Ready"/> that triggers
    /// <see cref="ArchipelagoTopBarUI.InjectButton"/> each time the top bar enters the scene tree.
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
