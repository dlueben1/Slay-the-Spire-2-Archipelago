using Godot;
using MegaCrit.Sts2.addons.mega_text;
using StS2AP.Utils;
using System;

namespace StS2AP.UI
{
    /// <summary>
    /// Static class that creates and manages the Archipelago Goal Tracker panel
    /// on the Character Select screen.
    ///
    /// The panel is positioned in the bottom-left corner of the screen and contains
    /// a single <see cref="MegaRichTextLabel"/> for displaying goal progress text.
    ///
    /// Injected and removed alongside <see cref="ArchipelagoCharTrackerUI"/> via the
    /// <c>CharTrackerPanelPatches</c> in <c>Patches_MainMenuBehavior</c>.
    /// </summary>
    public static class ArchipelagoGoalTrackerUI
    {
        #region Node References

        /// <summary>The root Control node added to the CanvasLayer.</summary>
        private static Control? _rootPanel;

        /// <summary>The CanvasLayer that hosts our panel so it renders on top of the scene.</summary>
        private static CanvasLayer? _canvasLayer;

        /// <summary>
        /// The rich-text label inside the panel where goal content will be written.
        /// Uses <see cref="MegaRichTextLabel"/> to support BBCode and the game's custom text effects.
        /// </summary>
        private static MegaRichTextLabel? _contentLabel;

        #endregion

        #region Constants

        // The game font used throughout the mod for consistency
        private const string FontPath = "res://fonts/kreon_regular.ttf";

        // Font size for the goal text
        private const int FontSize = 32;

        // Padding inside the panel
        private const float PanelPadding = 16f;

        // Panel offsets — positioned in the bottom-left corner of the screen
        private const float PanelLeftOffset   = 220f;
        private const float PanelBottomOffset = 250f;

        // Panel size — kept compact since it only holds a single label
        private const float PanelWidth  = 400f;
        private const float PanelHeight = 80f;

        // Semi-transparent dark background matching ArchipelagoCharTrackerUI
        private static readonly Color PanelBgColor = new Color(0.10f, 0.10f, 0.13f, 0.5f);

        // CanvasLayer rendering order — same layer as ArchipelagoCharTrackerUI
        private const int CanvasLayerIndex = 0;

        #endregion

        #region Public API

        /// <summary>
        /// Whether the goal tracker panel is currently present and visible.
        /// </summary>
        public static bool IsVisible => _rootPanel?.Visible ?? false;

        /// <summary>
        /// Injects the goal tracker panel into the scene tree, creating it if it does not yet exist.
        /// Safe to call multiple times — duplicate injection is ignored.
        /// </summary>
        public static void InjectUI()
        {
            try
            {
                var sceneTree = Engine.GetMainLoop() as SceneTree;
                if (sceneTree == null)
                {
                    LogUtility.Error("[GoalTracker] Failed to get SceneTree — cannot inject goal tracker UI");
                    return;
                }

                var root = sceneTree.Root;
                if (root == null)
                {
                    LogUtility.Error("[GoalTracker] Failed to get root node — cannot inject goal tracker UI");
                    return;
                }

                // Avoid rebuilding the UI if it is already present
                if (_rootPanel != null && IsInstanceValid(_rootPanel))
                {
                    Show();
                    return;
                }

                // Build and attach
                _rootPanel = CreateUI();

                _canvasLayer = new CanvasLayer();
                _canvasLayer.Name = "ArchipelagoGoalTrackerLayer";
                _canvasLayer.Layer = CanvasLayerIndex;
                _canvasLayer.AddChild(_rootPanel);
                root.AddChild(_canvasLayer);

                LogUtility.Success("[GoalTracker] Archipelago goal-tracker UI injected successfully");
            }
            catch (Exception ex)
            {
                LogUtility.Error($"[GoalTracker] Failed to inject goal tracker UI: {ex.Message}");
            }
        }

        /// <summary>
        /// Removes the goal tracker panel from the scene tree entirely and resets all node references.
        /// Called when the Character Select screen is closed.
        /// </summary>
        public static void RemoveUI()
        {
            if (_canvasLayer != null && IsInstanceValid(_canvasLayer))
            {
                _canvasLayer.QueueFree();
                _canvasLayer  = null;
                _rootPanel    = null;
                _contentLabel = null;
            }
        }

        /// <summary>
        /// Makes the goal tracker panel visible.
        /// </summary>
        public static void Show()
        {
            if (_rootPanel != null && IsInstanceValid(_rootPanel))
            {
                _rootPanel.Visible = true;
            }
        }

        /// <summary>
        /// Hides the goal tracker panel without removing it from the tree.
        /// </summary>
        public static void Hide()
        {
            if (_rootPanel != null && IsInstanceValid(_rootPanel))
            {
                _rootPanel.Visible = false;
            }
        }

        /// <summary>
        /// Replaces the content displayed inside the goal tracker panel.
        /// Supports full BBCode (including the game's custom [sine], [rainbow], etc. effects).
        /// </summary>
        /// <param name="bbcodeText">BBCode-formatted string to display.</param>
        public static void SetContent(string bbcodeText)
        {
            if (_contentLabel != null && IsInstanceValid(_contentLabel))
            {
                // Re-apply the font size each time — the game sometimes resets theme overrides
                _contentLabel.RemoveThemeFontSizeOverride("normal_font_size");
                _contentLabel.AddThemeFontSizeOverride("normal_font_size", FontSize);

                _contentLabel.Text = bbcodeText;
            }
        }

        #endregion

        #region UI Construction

        /// <summary>
        /// Builds the entire panel hierarchy from scratch.
        ///
        /// Layout (bottom-left anchored):
        /// <code>
        ///   Control (root, FullRect)
        ///     └─ PanelContainer  ← semi-transparent background, fixed size, bottom-left anchor
        ///          └─ MegaRichTextLabel  ← goal progress text
        /// </code>
        /// </summary>
        private static Control CreateUI()
        {
            // ── Root ──────────────────────────────────────────────────────────────────
            // Full-rect so anchors/offsets work relative to the full viewport.
            var root = new Control();
            root.Name = "ArchipelagoGoalTrackerUI";
            root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            // Pass all mouse events through so we don't block game input
            root.MouseFilter = Control.MouseFilterEnum.Ignore;

            // ── Panel ─────────────────────────────────────────────────────────────────
            // Anchored to the bottom-left corner of the screen.
            var panel = new PanelContainer();
            panel.Name = "GoalTrackerPanel";
            panel.CustomMinimumSize = new Vector2(PanelWidth, PanelHeight);

            // Position: anchor bottom-left, then shift right by PanelLeftOffset and up by PanelBottomOffset.
            panel.AnchorLeft   = 0f;
            panel.AnchorRight  = 0f;
            panel.AnchorTop    = 1f;
            panel.AnchorBottom = 1f;

            panel.OffsetLeft   = PanelLeftOffset;
            panel.OffsetRight  = PanelLeftOffset + PanelWidth;
            panel.OffsetTop    = -(PanelBottomOffset + PanelHeight);
            panel.OffsetBottom = -PanelBottomOffset;

            // Mouse passthrough — we don't need interaction on the goal tracker panel itself
            panel.MouseFilter = Control.MouseFilterEnum.Ignore;

            // ── Background style ──────────────────────────────────────────────────────
            // Matches the look of ArchipelagoCharTrackerUI: semi-transparent, slightly
            // rounded corners, no visible border.
            var panelStyle = new StyleBoxFlat();
            panelStyle.BgColor = PanelBgColor;
            panelStyle.SetBorderWidthAll(0);
            panelStyle.SetCornerRadiusAll(10);
            panelStyle.ContentMarginLeft   = PanelPadding;
            panelStyle.ContentMarginRight  = PanelPadding;
            panelStyle.ContentMarginTop    = PanelPadding;
            panelStyle.ContentMarginBottom = PanelPadding;
            panel.AddThemeStyleboxOverride("panel", panelStyle);

            root.AddChild(panel);

            // ── Content label ─────────────────────────────────────────────────────────
            // Single MegaRichTextLabel for goal progress text with BBCode support.
            _contentLabel = new MegaRichTextLabel();
            _contentLabel.Name = "GoalTrackerLabel";
            _contentLabel.SizeFlagsHorizontal = Control.SizeFlags.Fill;
            _contentLabel.SizeFlagsVertical   = Control.SizeFlags.ShrinkCenter;
            _contentLabel.FitContent          = true;
            _contentLabel.AutowrapMode        = TextServer.AutowrapMode.Word;
            _contentLabel.BbcodeEnabled       = true;
            _contentLabel.MouseFilter         = Control.MouseFilterEnum.Ignore;

            // Apply the game font so the text matches the rest of the UI
            try
            {
                var font = GD.Load<Font>(FontPath);
                if (font != null)
                {
                    _contentLabel.AddThemeFontOverride("normal_font", font);
                }
                else
                {
                    LogUtility.Warn($"[GoalTracker] Could not load font: {FontPath}");
                }
            }
            catch (Exception ex)
            {
                LogUtility.Warn($"[GoalTracker] Failed to load label font: {ex.Message}");
            }

            // Placeholder text — will be replaced by SetContent() after injection
            _contentLabel.Text = "[gold]Goal: Slay the Spire with X Characters\nProgress: 0 / 3";

            panel.AddChild(_contentLabel);

            return root;
        }

        #endregion

        #region Helpers

        /// <summary>
        /// Checks whether a GodotObject instance is still valid (not null and not freed).
        /// </summary>
        private static bool IsInstanceValid(GodotObject obj)
        {
            return GodotObject.IsInstanceValid(obj);
        }

        #endregion
    }
}
