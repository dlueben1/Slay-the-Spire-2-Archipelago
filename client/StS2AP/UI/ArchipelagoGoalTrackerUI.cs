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
        private const int FontSize = 24;

        // Padding inside the panel
        private const float PanelPadding = 16f;

        // Panel offsets — positioned in the bottom(ish)-left corner of the screen
        private const float PanelLeftOffset   = 220f;
        private const float PanelBottomOffset = 268f;

        // Panel size — kept compact since it only holds a single label
        private const float PanelWidth  = 400f;
        private const float PanelHeight = 70f;

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
        private static void SetContent(string bbcodeText)
        {
            if (_contentLabel != null && IsInstanceValid(_contentLabel))
            {
                // Re-apply the font size each time — the game sometimes resets theme overrides
                _contentLabel.RemoveThemeFontSizeOverride("normal_font_size");
                _contentLabel.AddThemeFontSizeOverride("normal_font_size", FontSize);

                _contentLabel.Text = bbcodeText;
            }
        }

        /// <summary>
        /// Refreshes the goal progress text based on the current number of goal-achieved characters.
        /// </summary>
        public static void UpdateGoalProgress()
        {
            // Get the number of characters you need to beat the game with. If the value for `NumCharsGoal` is `0`, we need to use the number of characters available
            var charsToGoal = ArchipelagoClient.Settings.NumCharsGoal > 0 ? ArchipelagoClient.Settings.NumCharsGoal : ArchipelagoClient.Settings.TotalCharacters;

            // Update the UI
            SetContent($"[gold]Goal: Slay the Spire with {charsToGoal} Characters[/gold]\nProgress: {GameUtility.GoaledCharactersCount} / {charsToGoal}");
        }

        #endregion

        #region UI Construction

        /// <summary>
        /// Builds the entire panel hierarchy from scratch.
        ///
        /// Layout:
        /// <code>
        ///   Control (root, FullRect)
        ///     └─ PanelContainer  ← semi-transparent background, fixed size, bottom-left anchor
        ///          └─ MegaRichTextLabel  ← goal progress text
        /// </code>
        /// </summary>
        private static Control CreateUI()
        {
            // Root
            var root = new Control();
            root.Name = "ArchipelagoGoalTrackerUI";
            root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            // Pass all mouse events through so we don't block game input
            root.MouseFilter = Control.MouseFilterEnum.Ignore;

            // Panel
            var panel = new PanelContainer();
            panel.Name = "GoalTrackerPanel";
            panel.CustomMinimumSize = new Vector2(PanelWidth, PanelHeight);

            // Anchor Positions
            panel.AnchorLeft   = 0f;
            panel.AnchorRight  = 0f;
            panel.AnchorTop    = 1f;
            panel.AnchorBottom = 1f;

            // Offset Positions
            panel.OffsetLeft   = PanelLeftOffset;
            panel.OffsetRight  = PanelLeftOffset + PanelWidth;
            panel.OffsetTop    = -(PanelBottomOffset + PanelHeight);
            panel.OffsetBottom = -PanelBottomOffset;

            // Mouse passthrough — we don't need interaction on the goal tracker panel itself
            panel.MouseFilter = Control.MouseFilterEnum.Ignore;

            // Setup Background
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

            // Content label
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

            /// Font size is intentionally NOT set here — MegaRichTextLabel._Ready() fires when the
            /// node enters the tree and resets font size overrides. SetContent() applies it at runtime
            /// after _Ready() has already fired, matching the pattern in ArchipelagoCharTrackerUI.
            _contentLabel.Text = "";

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
