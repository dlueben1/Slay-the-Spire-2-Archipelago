using Godot;
using MegaCrit.Sts2.addons.mega_text;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;
using StS2AP.Models;
using StS2AP.UI.Components;
using StS2AP.Utils;
using System;

namespace StS2AP.UI
{
    /// <summary>
    /// Static class that creates and manages the Archipelago Progress Tracker panel
    /// on the Character Select screen.
    ///
    /// The panel is positioned on the left side of the screen, offset ~200 logical
    /// pixels from the left edge and near the top of the viewport — intentionally
    /// mirroring the vertical position and size of the game's own InfoPanel
    /// (<see cref="NCharacterSelectScreen"/> node: "InfoPanel").
    ///
    /// Injected via <see cref="CharTrackerInjectionPatch"/> which postfixes
    /// <see cref="NCharacterSelectScreen.OnSubmenuOpened"/> and
    /// <see cref="NCharacterSelectScreen.OnSubmenuClosed"/>.
    /// </summary>
    public static class ArchipelagoCharTrackerUI
    {
        #region Node References

        /// <summary>The root Control node added to the CanvasLayer.</summary>
        private static Control? _rootPanel;

        /// <summary>The CanvasLayer that hosts our panel so it renders on top of the scene.</summary>
        private static CanvasLayer? _canvasLayer;

        /// <summary>
        /// The rich-text label inside the panel where progress content will be written.
        /// Uses <see cref="MegaRichTextLabel"/> (same as <see cref="ArchipelagoNotificationUI"/>)
        /// to support BBCode and the game's custom text effects.
        /// </summary>
        private static MegaRichTextLabel? _contentLabel;

        /// <summary>
        /// The HBoxContainer holding the left list's sub-columns (AP Checks).
        /// Columns are added automatically when the row count reaches <see cref="MaxRowsPerColumn"/>.
        /// </summary>
        private static HBoxContainer? _leftListContainer;

        /// <summary>
        /// The HBoxContainer holding the right list's sub-columns (AP Items).
        /// Columns are added automatically when the row count reaches <see cref="MaxRowsPerColumn"/>.
        /// </summary>
        private static HBoxContainer? _rightListContainer;

        // Tracks the active VBoxContainer column and current row count for each list
        private static VBoxContainer? _leftCurrentColumn;
        private static int            _leftColumnRowCount;
        private static VBoxContainer? _rightCurrentColumn;
        private static int            _rightColumnRowCount;

        #endregion

        #region ItemCountLabel References

        // ── AP Check labels (left list) ───────────────────────────────────────────
        // Each label can be updated at runtime via its SetText() method, e.g.:
        //   ArchipelagoCharTrackerUI.CardChecks?.SetText("(15 / 45)");

        /// <summary>Tracks how many Card Reward AP Checks have been found.</summary>
        public static ItemCountLabel? CardChecks { get; private set; }

        /// <summary>Tracks how many Rare Card Reward AP Checks have been found.</summary>
        public static ItemCountLabel? RareCardChecks { get; private set; }

        /// <summary>Tracks how many Relic Reward AP Checks have been found.</summary>
        public static ItemCountLabel? RelicChecks { get; private set; }

        /// <summary>Tracks how many Floorsanity AP Checks have been sent.</summary>
        public static ItemCountLabel? FloorsanityChecks { get; private set; }

        /// <summary>Tracks how many Potionsanity AP Checks have been found.</summary>
        public static ItemCountLabel? PotionsanityChecks { get; private set; }

        /// <summary>Tracks how many Goldsanity AP Checks have been found.</summary>
        public static ItemCountLabel? GoldsanityChecks { get; private set; }

        /// <summary>Tracks how many Campfiresanity AP Checks have been found.</summary>
        public static ItemCountLabel? CampfiresanityChecks { get; private set; }

        /// <summary>Tracks whether the "Press Start" check has been earned.</summary>
        public static ItemCountLabel? PressStartCheck { get; private set; }

        /// <summary>Tracks whether the "Slayed the Spire" check has been earned.</summary>
        public static ItemCountLabel? ClearedCheck { get; private set; }

        // ── AP Item labels (right list) ───────────────────────────────────────────

        /// <summary>Tracks the number of Card Rewards received from the multiworld.</summary>
        public static ItemCountLabel? CardRewards { get; private set; }

        /// <summary>Tracks the number of Rare Card Rewards received from the multiworld.</summary>
        public static ItemCountLabel? RareCardRewards { get; private set; }

        /// <summary>Tracks the number of Relic Rewards received from the multiworld.</summary>
        public static ItemCountLabel? RelicRewards { get; private set; }

        /// <summary>Tracks the number of Potion Rewards received from the multiworld.</summary>
        public static ItemCountLabel? PotionRewards { get; private set; }

        /// <summary>Tracks the total Gold received from the multiworld.</summary>
        public static ItemCountLabel? GoldRewards { get; private set; }

        /// <summary>Tracks the number of Progressive Rest rewards received.</summary>
        public static ItemCountLabel? ProgressiveRestLabel { get; private set; }

        /// <summary>Tracks the number of Progressive Smith rewards received.</summary>
        public static ItemCountLabel? ProgressiveSmithLabel { get; private set; }

        #endregion

        #region Constants

        // The game font used throughout the mod for consistency
        private const string FontPath = "res://fonts/kreon_regular.ttf";

        // Font size matching the character info panel's body text
        private const int FontSize = 24;

        // Padding inside the panel (matches the feel of the InfoPanel)
        private const float PanelPadding = 20f;

        // Panel offsets — chosen to align with the game's InfoPanel vertical position.
        private const float PanelLeftOffset = 220f;
        private const float PanelTopOffset  = 16f;

        // Panel Size
        private const float PanelWidth  = 680f;
        private const float PanelHeight = 300f;

        // Semi-transparent dark background
        private static readonly Color PanelBgColor = new Color(0.10f, 0.10f, 0.13f, 0.5f);

        // CanvasLayer rendering order.
        private const int CanvasLayerIndex = 0;

        // Maximum rows per sub-column before wrapping to a new column
        private const int MaxRowsPerColumn = 6;

        // Fixed left-edge offsets (relative to the panel's usable interior) for each list.
        // Using absolute offsets keeps the right list position independent of the left list width.
        private const float ChecksListLeft = 0f;
        private const float ItemsListLeft  = (PanelWidth - (PanelPadding * 2)) / 2f;

        #endregion

        #region Public API

        /// <summary>
        /// Whether the tracker panel is currently present and visible.
        /// </summary>
        public static bool IsVisible => _rootPanel?.Visible ?? false;

        /// <summary>
        /// Injects the tracker panel into the scene tree, creating it if it does not yet exist.
        /// Safe to call multiple times — duplicate injection is ignored.
        /// </summary>
        public static void InjectUI()
        {
            try
            {
                var sceneTree = Engine.GetMainLoop() as SceneTree;
                if (sceneTree == null)
                {
                    LogUtility.Error("[CharTracker] Failed to get SceneTree — cannot inject tracker UI");
                    return;
                }

                var root = sceneTree.Root;
                if (root == null)
                {
                    LogUtility.Error("[CharTracker] Failed to get root node — cannot inject tracker UI");
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
                _canvasLayer.Name = "ArchipelagoCharTrackerLayer";
                _canvasLayer.Layer = CanvasLayerIndex;
                _canvasLayer.AddChild(_rootPanel);
                root.AddChild(_canvasLayer);

                // SetContent() must be called after the node is in the tree — MegaRichTextLabel._Ready()
                // resets font size overrides on tree entry, so we apply ours immediately after.
                SetContent("[gold]Checks Found[/gold]                                 [gold]Received Items[/gold]");

                LogUtility.Success("[CharTracker] Archipelago char-tracker UI injected successfully");
            }
            catch (Exception ex)
            {
                LogUtility.Error($"[CharTracker] Failed to inject tracker UI: {ex.Message}");
            }
        }

        /// <summary>
        /// Removes the tracker panel from the scene tree entirely and resets all node references.
        /// Called when the Character Select screen is closed.
        /// </summary>
        public static void RemoveUI()
        {
            if (_canvasLayer != null && IsInstanceValid(_canvasLayer))
            {
                _canvasLayer.QueueFree();
                _canvasLayer         = null;
                _rootPanel           = null;
                _contentLabel        = null;
                _leftListContainer   = null;
                _rightListContainer  = null;
                _leftCurrentColumn   = null;
                _leftColumnRowCount  = 0;
                _rightCurrentColumn  = null;
                _rightColumnRowCount = 0;

                // Reset all ItemCountLabel references — their Godot nodes are freed with the canvas layer
                CardChecks           = null;
                RareCardChecks       = null;
                RelicChecks          = null;
                FloorsanityChecks    = null;
                PotionsanityChecks   = null;
                GoldsanityChecks     = null;
                CampfiresanityChecks = null;
                PressStartCheck     = null;
                ClearedCheck        = null;
                CardRewards          = null;
                RareCardRewards      = null;
                RelicRewards         = null;
                PotionRewards        = null;
                GoldRewards          = null;
                ProgressiveRestLabel = null;
                ProgressiveSmithLabel = null;
            }
        }

        /// <summary>
        /// Makes the tracker panel visible.
        /// </summary>
        public static void Show()
        {
            if (_rootPanel != null && IsInstanceValid(_rootPanel))
            {
                _rootPanel.Visible = true;
            }
        }

        /// <summary>
        /// Hides the tracker panel without removing it from the tree.
        /// </summary>
        public static void Hide()
        {
            if (_rootPanel != null && IsInstanceValid(_rootPanel))
            {
                _rootPanel.Visible = false;
            }
        }

        /// <summary>
        /// Replaces the content displayed inside the tracker panel.
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

        /// <summary>
        /// Adds an <see cref="ItemCountLabel"/> row to the LEFT (Checks) list.
        /// Automatically wraps into a new sub-column after every <see cref="MaxRowsPerColumn"/> rows.
        /// </summary>
        /// <param name="row">The ItemCountLabel to add.</param>
        public static void AddCheckRow(ItemCountLabel row)
        {
            if (_leftListContainer == null || !IsInstanceValid(_leftListContainer))
            {
                LogUtility.Warn("[CharTracker] Left list container not yet initialized; row will not be added");
                return;
            }
            AddRowToList(row, _leftListContainer, ref _leftCurrentColumn, ref _leftColumnRowCount);
        }

        /// <summary>
        /// Adds an <see cref="ItemCountLabel"/> row to the RIGHT (Items) list.
        /// Automatically wraps into a new sub-column after every <see cref="MaxRowsPerColumn"/> rows.
        /// The right list's position is fixed and independent of the left list's width.
        /// </summary>
        /// <param name="row">The ItemCountLabel to add.</param>
        public static void AddItemRow(ItemCountLabel row)
        {
            if (_rightListContainer == null || !IsInstanceValid(_rightListContainer))
            {
                LogUtility.Warn("[CharTracker] Right list container not yet initialized; row will not be added");
                return;
            }
            AddRowToList(row, _rightListContainer, ref _rightCurrentColumn, ref _rightColumnRowCount);
        }

        #endregion

        #region UI Construction

        /// <summary>
        /// Builds the entire panel hierarchy from scratch (no Godot editor scenes available).
        ///
        /// Layout (left-anchored):
        /// <code>
        ///   Control (root, FullRect)
        ///     └─ PanelContainer  ← semi-transparent background, fixed size, top-left anchor
        ///          └─ VBoxContainer (main content vbox)
        ///               ├─ MegaRichTextLabel  ← header/title
        ///               └─ Control (listsRow, full-width, absolute children)
        ///                    ├─ HBoxContainer (_leftListContainer,  OffsetLeft=ChecksListLeft)
        ///                    │    └─ VBoxContainer columns (max MaxRowsPerColumn rows each)
        ///                    └─ HBoxContainer (_rightListContainer, OffsetLeft=ItemsListLeft)
        ///                         └─ VBoxContainer columns (max MaxRowsPerColumn rows each)
        /// </code>
        /// </summary>
        private static Control CreateUI()
        {
            // ── Root ──────────────────────────────────────────────────────────────────
            // Full-rect so anchors/offsets work relative to the full viewport.
            var root = new Control();
            root.Name = "ArchipelagoCharTrackerUI";
            root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            // Pass all mouse events through so we don't block game input
            root.MouseFilter = Control.MouseFilterEnum.Ignore;

            // ── Panel ─────────────────────────────────────────────────────────────────
            // Anchored to the top-left corner to sit alongside the game's InfoPanel.
            // PanelLeftOffset pushes us ~200 px from the left edge so the panel doesn't
            // overlap the character buttons, mirroring the InfoPanel's horizontal position.
            var panel = new PanelContainer();
            panel.Name = "CharTrackerPanel";
            panel.CustomMinimumSize = new Vector2(PanelWidth, PanelHeight);

            // Position: anchor top-left, then shift right by PanelLeftOffset.
            panel.AnchorLeft   = 0f;
            panel.AnchorRight  = 0f;
            panel.AnchorTop    = 0f;
            panel.AnchorBottom = 0f;

            // OffsetLeft = PanelLeftOffset places the left edge PanelLeftOffset px from the screen's left.
            // OffsetRight = PanelLeftOffset + PanelWidth places the right edge accordingly.
            panel.OffsetLeft   = PanelLeftOffset;
            panel.OffsetRight  = PanelLeftOffset + PanelWidth;
            panel.OffsetTop    = PanelTopOffset;
            panel.OffsetBottom = PanelTopOffset + PanelHeight;

            // Mouse passthrough — we don't need interaction on the tracker panel itself
            panel.MouseFilter = Control.MouseFilterEnum.Ignore;

            // ── Background style ──────────────────────────────────────────────────────
            // Matches the look of the character info panel: semi-transparent, slightly
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

            // ── Main content layout ────────────────────────────────────────────────────
            var mainVbox = new VBoxContainer();
            mainVbox.Name = "MainContentVBox";
            mainVbox.AddThemeConstantOverride("separation", 8);
            mainVbox.SizeFlagsHorizontal = Control.SizeFlags.Fill;
            mainVbox.SizeFlagsVertical   = Control.SizeFlags.Fill;
            panel.AddChild(mainVbox);

            // ── Header label ───────────────────────────────────────────────────────────
            // We use MegaRichTextLabel so BBCode and game text effects work out of the box,
            // consistent with how ArchipelagoNotificationUI renders its messages.
            _contentLabel = new MegaRichTextLabel();
            _contentLabel.Name = "CharTrackerHeaderLabel";
            _contentLabel.SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin;
            // SizeFlagsVertical = 0 means don't expand; let FitContent size it to text height
            _contentLabel.SizeFlagsVertical   = 0;
            _contentLabel.FitContent          = true; // Fit to actual text height
            _contentLabel.AutowrapMode        = TextServer.AutowrapMode.Word;
            _contentLabel.BbcodeEnabled       = true; // Required for MegaRichTextLabel effects
            _contentLabel.MouseFilter         = Control.MouseFilterEnum.Ignore;
            _contentLabel.CustomMinimumSize   = new Vector2(PanelWidth - (PanelPadding * 2), 0);

            // Placeholder text — this will be replaced by SetContent() if needed
            _contentLabel.Text = "[gold]Checks Found[/gold]                                 [gold]Received Items[/gold]";

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
                    LogUtility.Warn($"[CharTracker] Could not load font: {FontPath}");
                }
            }
            catch (Exception ex)
            {
                LogUtility.Warn($"[CharTracker] Failed to load header label font: {ex.Message}");
            }

            /// Font size is intentionally NOT set here — MegaRichTextLabel._Ready() fires when the
            /// node enters the tree and resets font size overrides. SetContent() applies it at runtime
            /// after _Ready() has already fired, matching the pattern in ArchipelagoNotificationUI.
            mainVbox.AddChild(_contentLabel);

            // ── Two-list layout (absolute positioning) ────────────────────────────────
            // We use a plain Control as a full-width row and anchor each list at a fixed
            // OffsetLeft so the right list's position is completely independent of how
            // wide the left list grows. Each list is an HBoxContainer that spawns new
            // VBoxContainer sub-columns automatically once MaxRowsPerColumn is reached.
            var listsRow = new Control();
            listsRow.Name = "ListsRow";
            listsRow.SizeFlagsHorizontal = Control.SizeFlags.Fill;
            listsRow.SizeFlagsVertical   = Control.SizeFlags.Fill;
            listsRow.MouseFilter         = Control.MouseFilterEnum.Ignore;
            mainVbox.AddChild(listsRow);

            // LEFT list — AP Checks, anchored at the left edge of the panel interior
            _leftListContainer = new HBoxContainer();
            _leftListContainer.Name        = "LeftListContainer";
            _leftListContainer.AnchorLeft  = 0f;
            _leftListContainer.AnchorRight = 0f;
            _leftListContainer.AnchorTop   = 0f;
            _leftListContainer.OffsetLeft  = ChecksListLeft;
            _leftListContainer.AddThemeConstantOverride("separation", 4);
            _leftListContainer.SizeFlagsVertical = Control.SizeFlags.Fill;
            _leftListContainer.MouseFilter       = Control.MouseFilterEnum.Ignore;
            listsRow.AddChild(_leftListContainer);

            // RIGHT list — AP Items, anchored at the panel midpoint regardless of left list width
            _rightListContainer = new HBoxContainer();
            _rightListContainer.Name        = "RightListContainer";
            _rightListContainer.AnchorLeft  = 0f;
            _rightListContainer.AnchorRight = 0f;
            _rightListContainer.AnchorTop   = 0f;
            _rightListContainer.OffsetLeft  = ItemsListLeft;
            _rightListContainer.AddThemeConstantOverride("separation", 4);
            _rightListContainer.SizeFlagsVertical = Control.SizeFlags.Fill;
            _rightListContainer.MouseFilter       = Control.MouseFilterEnum.Ignore;
            listsRow.AddChild(_rightListContainer);

            // ── AP Checks ───────────────────────────────────────────────────────
            // Each label is stored as a static property so patches can update its text
            // at runtime via SetText(), e.g. CardChecks?.SetText("(15 / 45)").

            // Card Checks Counter
            CardChecks = new ItemCountLabel("res://images/ui/reward_screen/reward_icon_card.png", "(0 / 0)", tooltipTitle: "Card Checks", tooltipDescription: "The number of AP Checks found that replaced Card Rewards");
            AddCheckRow(CardChecks);

            // Rare Card Checks Counter
            RareCardChecks = new ItemCountLabel("res://images/ui/reward_screen/reward_icon_rare.png", "(0 / 0)", tooltipTitle: "Rare Card Checks", tooltipDescription: "The number of AP Checks found that replaced Rare Card Rewards");
            AddCheckRow(RareCardChecks);

            // Relic Checks Counter
            RelicChecks = new ItemCountLabel("res://images/relics/calling_bell.png", "(0 / 0)", tooltipTitle: "Relic Checks", tooltipDescription: "The number of AP Checks found that replaced Relic Rewards");
            AddCheckRow(RelicChecks);

            // Floorsanity Checks Counter (Note: When the Winged Boots are in main, we should use that relic here instead)
            if(ArchipelagoClient.Settings.Floorsanity)
            {
                FloorsanityChecks = new ItemCountLabel("res://images/relics/planisphere.png", "(0 / 0)", tooltipTitle: "Floorsanity Checks", tooltipDescription: "The number of AP Checks sent for each floor reached");
                AddCheckRow(FloorsanityChecks);
            }

            // Potionsanity Checks Counter
            if(ArchipelagoClient.Settings.PotionSanity)
            {
                PotionsanityChecks = new ItemCountLabel("res://images/potions/skill_potion.png", "(0 / 0)", tooltipTitle: "Potionsanity Checks", tooltipDescription: "The number of AP Checks found that replaced Potion Rewards");
                AddCheckRow(PotionsanityChecks);
            }

            // Goldsanity Checks Counter
            if(ArchipelagoClient.Settings.GoldSanity)
            {
                GoldsanityChecks = new ItemCountLabel("res://images/ui/reward_screen/reward_icon_money.png", "(0 / 0)", tooltipTitle: "Goldsanity Checks", tooltipDescription: "The number of AP Checks found that replaced Gold Rewards");
                AddCheckRow(GoldsanityChecks);
            }

            // Campfiresanity Checks Counter
            if(ArchipelagoClient.Settings.CampfireSanity)
            {
                CampfiresanityChecks = new ItemCountLabel("res://images/ui/run_history/rest_site.png", "(0 / 0)", tooltipTitle: "Campfiresanity Checks", tooltipDescription: "The number of AP Checks found from Rest Sites");
                AddCheckRow(CampfiresanityChecks);
            }

            // Press Start Counter
            PressStartCheck = new ItemCountLabel("res://images/ui/run_history/neow.png", "—", tooltipTitle: "Pressed Start", tooltipDescription: "Whether this character has earned a check by starting a run.");
            AddCheckRow(PressStartCheck);

            // Slayed the Spire Counter
            ClearedCheck = new ItemCountLabel("res://images/relics/pantograph.png", "—", tooltipTitle: "Slayed the Spire", tooltipDescription: "Whether this character has earned a check by completing a run.");
            AddCheckRow(ClearedCheck);

            // ── AP Items ──────────────────────────────────────────────────────

            // Card Rewards Counter
            CardRewards = new ItemCountLabel("res://images/ui/reward_screen/reward_icon_card.png", "0", tooltipTitle: "Card Rewards", tooltipDescription: "The number of Card Rewards received for this character. You can redeem these at the start of each run.");
            AddItemRow(CardRewards);

            // Rare Card Rewards Counter
            RareCardRewards = new ItemCountLabel("res://images/ui/reward_screen/reward_icon_rare.png", "0", tooltipTitle: "Rare Card Rewards", tooltipDescription: "The number of Rare Card Rewards received for this character. You can redeem these at the start of each run.");
            AddItemRow(RareCardRewards);

            // Relics Counter
            RelicRewards = new ItemCountLabel("res://images/relics/circlet.png", "0", tooltipTitle: "Relic Rewards", tooltipDescription: "The number of Relic Rewards received for this character. You can redeem these at the start of each run.");
            AddItemRow(RelicRewards);

            // Potions Counter
            PotionRewards = new ItemCountLabel("res://images/potions/glowwater_potion.png", "0", tooltipTitle: "Potion Rewards", tooltipDescription: "The number of Potion Rewards received for this character. You can redeem these at the start of each run.");
            AddItemRow(PotionRewards);

            // Gold Rewards Total
            GoldRewards = new ItemCountLabel("res://images/ui/reward_screen/reward_icon_money.png", "0", tooltipTitle: "Gold", tooltipDescription: "The total amount of Gold received for this character. You can redeem this at the start of each run.");
            AddItemRow(GoldRewards);

            // Progressive Rest Total
            ProgressiveRestLabel = new ItemCountLabel("res://images/relics/regal_pillow.png", "(0 / 3)", tooltipTitle: "Progressive Rests", tooltipDescription: "The number of Progressive Rest rewards received for this character. The number of these represents the highest Act you can Heal at.");
            AddItemRow(ProgressiveRestLabel);

            // Progressive Smith Total
            ProgressiveSmithLabel = new ItemCountLabel("res://images/relics/whetstone.png", "(0 / 3)", tooltipTitle: "Progressive Smiths", tooltipDescription: "The number of Progressive Smith rewards received for this character. The number of these represents the highest Act you can Upgrade at.");
            AddItemRow(ProgressiveSmithLabel);

            // Start visible — it will be shown/hidden by the patch hooks
            root.Visible = true;

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

        /// <summary>
        /// Adds <paramref name="row"/> to <paramref name="listContainer"/>, automatically
        /// creating a new <see cref="VBoxContainer"/> sub-column whenever
        /// <see cref="MaxRowsPerColumn"/> rows have been placed in the current one.
        /// </summary>
        private static void AddRowToList(
            ItemCountLabel   row,
            HBoxContainer    listContainer,
            ref VBoxContainer? currentColumn,
            ref int            columnRowCount)
        {
            // Spin up a new column if we haven't started one yet, or the current one is full
            if (currentColumn == null || !IsInstanceValid(currentColumn) || columnRowCount >= MaxRowsPerColumn)
            {
                currentColumn = new VBoxContainer();
                currentColumn.AddThemeConstantOverride("separation", 4);
                currentColumn.SizeFlagsVertical = Control.SizeFlags.Fill;
                currentColumn.MouseFilter       = Control.MouseFilterEnum.Ignore;
                listContainer.AddChild(currentColumn);
                columnRowCount = 0;
            }

            currentColumn.AddChild(row.Root);
            columnRowCount++;
        }

        #endregion
    }
}
