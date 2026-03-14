using Godot;
using MegaCrit.Sts2.Core.Entities.Players;
using StS2AP.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using static StS2AP.Data.ItemTable;
using ItemInfo = Archipelago.MultiClient.Net.Models.ItemInfo;

namespace StS2AP.UI
{
    /// <summary>
    /// Data container for a single reward entry displayed in the reward screen.
    /// Can be created manually or via <see cref="ArchipelagoRewardUI.AddReward(ItemInfo)"/>.
    /// </summary>
    public class ArchipelagoRewardData
    {
        /// <summary>
        /// The ID that this item originated from, used for tracking and marking items as used in the multiworld progress.
        /// </summary>
        public long ItemOriginID { get; set; }

        /// <summary>The primary item name shown in large text on the reward button.</summary>
        public string ItemName { get; set; } = string.Empty;

        /// <summary>The player who sent this item (shown in smaller text below the item name)</summary>
        public string SenderName { get; set; } = string.Empty;

        /// <summary>Resource path to the icon shown on the left of the reward button. Empty = no icon</summary>
        public string IconPath { get; set; } = string.Empty;

        /// <summary>
        /// The async action that grants this item to the player when the button is clicked
        /// Null means the button is display-only (e.g. during a test or if not in a run)
        /// </summary>
        public Func<Task>? GrantAction { get; set; }

        /// <summary>Optional sync callback invoked after the grant completes (e.g. for cleanup)</summary>
        public Action? OnClaimed { get; set; }
    }

    /// <summary>
    /// Static class that creates and manages the Archipelago reward screen UI
    /// Displays a modal reward panel listing items received from the Archipelago server
    /// New rewards can be added live while the screen is already open btw
    /// Mirrors the layout of the game's own NRewardsScreen as closely as possible
    /// </summary>
    public static class ArchipelagoRewardUI
    {
        private static CanvasLayer? _rewardLayer;
        private static Control? _rootPanel;
        private static VBoxContainer? _itemContainer;
        private static Button? _proceedButton;
        private static Tween? _fadeTween;

        // UI resource paths sourced from rewards_screen.tscn
        private const string PanelPath   = "res://images/ui/reward_screen/reward_panel.png";
        private const string BannerPath  = "res://images/ui/reward_screen/reward_banner.png";
        private const string ItemBtnPath = "res://images/ui/reward_screen/reward_item_button.png";
        private const string FontBold    = "res://themes/kreon_bold_glyph_space_two.tres";
        private const string FontRegular = "res://fonts/kreon_regular.ttf";

        // Reward type icons reward_screen set for buttons
        private const string IconGold  = "res://images/ui/reward_screen/reward_icon_money.png";
        private const string IconCard  = "res://images/ui/reward_screen/reward_icon_card.png";
        private const string IconRelic = "res://images/ui/reward_screen/reward_icon_shared_relic.png";

        // Rewards window (Control, center-anchored inside root)
        private const float WindowOffsetLeft   = -264f;
        private const float WindowOffsetTop    = -304f;
        private const float WindowOffsetRight  =  262f;
        private const float WindowOffsetBottom =  336f;

        // Banner (TextureRect, center-top-anchored inside Background)
        private const float BannerOffsetLeft   = -324f;
        private const float BannerOffsetTop    =  -28f;
        private const float BannerOffsetRight  =  328f;
        private const float BannerOffsetBottom =  134f;

        // HeaderLabel offsets (inside Banner, full-rect)
        private const float HeaderOffsetLeft   =  141f;
        private const float HeaderOffsetTop    =   -9f;
        private const float HeaderOffsetRight  = -141f;
        private const float HeaderOffsetBottom =  -32f;

        // RewardContainerMask (TextureRect, center-anchored inside Rewards window, clips children)
        private const float MaskOffsetLeft   = -237f;
        private const float MaskOffsetTop    = -217f;
        private const float MaskOffsetRight  =  237f;
        private const float MaskOffsetBottom =  267f;

        // RewardsContainer (VBoxContainer, absolute position inside mask)
        private const float ContainerLeft  = 36f;
        private const float ContainerTop   = 35f;
        private const float ContainerWidth = 402f; // MaskOffsetRight*2 - ContainerLeft - rightPad(36)

        // Font sizes
        private const int HeaderFontSize      = 44;
        private const int HeaderFontSizeMin   = 32;
        private const int RewardNameFontSize  = 24;
        private const int RewardSenderFontSize = 16;
        private const float IconSlotSize      = 48f;
        private const float ButtonHeight      = 74f;

        private static int _remainingRewards = 0;

        /// <summary>
        /// Invoked when the reward screen is closed (all rewards dismissed or skipped)
        /// </summary>
        public static Action? OnScreenClosed;

        /// <summary>
        /// whether the reward screen is currently visible.
        /// </summary>
        public static bool IsVisible => _rewardLayer?.Visible ?? false;

        #region Public API

        /// <summary>
        /// Primary entry point called by <see cref="ArchipelagoClient"/> when an item is received
        /// from the Archipelago server Thread-safe defers the UI operation to the main thread
        /// </summary>
        /// <param name="item">The item received from the Archipelago server.</param>
        public static void AddReward(ItemInfo item)
        {
            var data = new ArchipelagoRewardData
            {
                ItemName     = item.ItemDisplayName,
                SenderName   = item.Player.Name,
                IconPath     = GetIconForItem(item),
                GrantAction  = GetGrantAction(item)
            };

            // OnItemReceived fires on a background thread — defer all Godot UI calls to the main thread
            Callable.From(() => AddRewardOnMainThread(data)).CallDeferred();
        }

        public static void ShowTestRewards()
        {
            var testRewards = new List<ArchipelagoRewardData>
            {
                new ArchipelagoRewardData
                {
                    ItemName    = "Relic",
                    SenderName  = "Archipelago",
                    IconPath    = IconRelic,
                    GrantAction = () => GameUtility.GrantRelic()
                },
                new ArchipelagoRewardData
                {
                    ItemName    = "50 Gold",
                    SenderName  = "Archipelago",
                    IconPath    = IconGold,
                    GrantAction = () => GameUtility.GrantGold(50)
                },
                new ArchipelagoRewardData
                {
                    ItemName    = "Card Reward",
                    SenderName  = "TestPlayer",
                    IconPath    = IconCard,
                    GrantAction = () => GameUtility.GrantCardReward(rare: false)
                },
            };
            Callable.From(() => ShowRewards(testRewards)).CallDeferred();
        }

        /// <summary>
        /// Shows the Reward Screen and all of the unused items available to the user in their current run.
        /// </summary>
        public static void ShowRewards()
        {
            // Ignore if current player is null
            if (GameUtility.CurrentPlayer == null) return;

            // Get Unused items from the Multiworld for our current character
            var availableItems = ArchipelagoClient.Progress.AllReceivedItems
                                .Where(item => !ArchipelagoClient.Progress.UsedItems.Contains(item.LocationId) && item.GetStSCharID() == GameUtility.CurrentCharacterID);
            
            // Prepare them for the UI
            var rewardDataList = availableItems.Select(item => new ArchipelagoRewardData
            {
                ItemOriginID = item.LocationId,
                ItemName    = item.ItemDisplayName,
                SenderName  = item.Player.Name,
                IconPath    = GetIconForItem(item),
                GrantAction = GetGrantAction(item),
            }).ToList();

            rewardDataList.ForEach(item => item.OnClaimed = () =>
            {
                // Mark the item as used in the Multiworld so it doesn't show up again if we reopen the screen
                ArchipelagoClient.Progress.UsedItems.Add(item.ItemOriginID);
            });

            // Show the UI with these rewards
            ShowRewards(rewardDataList);
        }

        /// <summary>
        /// Shows the reward screen with a list of pre-built reward data objects.
        /// Replaces any currently displayed rewards.
        /// </summary>
        /// <param name="rewards">The reward entries to display.</param>
        public static void ShowRewards(List<ArchipelagoRewardData> rewards)
        {
            try
            {
                if (_rewardLayer == null || !IsInstanceValid(_rewardLayer))
                    CreateUI();

                if (_itemContainer == null || !IsInstanceValid(_itemContainer))
                {
                    LogUtility.Error("Reward item container is null after UI creation — cannot show rewards");
                    return;
                }

                // Clear any previously displayed reward buttons
                foreach (var child in _itemContainer.GetChildren())
                    child.QueueFree();

                _remainingRewards = 0;

                foreach (var data in rewards)
                    AppendRewardButton(data);

                ShowWithAnimation();
                LogUtility.Success($"Archipelago reward screen shown with {rewards.Count} reward(s)");
            }
            catch (Exception ex)
            {
                LogUtility.Error($"Failed to show reward screen: {ex.Message}");
            }
        }

        /// <summary>
        /// Shows the reward screen with a simple list of reward text strings
        /// Icons are automatically inferred from the text content
        /// </summary>
        /// <param name="rewardTexts">Plain text descriptions of the rewards to display</param>
        public static void ShowRewards(List<string> rewardTexts)
        {
            var dataList = rewardTexts
                .Select(t => new ArchipelagoRewardData
                {
                    ItemName   = t,
                    SenderName = string.Empty,
                    IconPath   = GetAutoIcon(t)
                })
                .ToList();
            ShowRewards(dataList);
        }

        /// <summary>
        /// Hides the reward screen with a fade out animation and fires <see cref="OnScreenClosed"/>.
        /// </summary>
        public static void Hide()
        {
            if (_rewardLayer == null || !IsInstanceValid(_rewardLayer))
                return;

            // Fade out the rewards window, then hide the layer
            if (_rootPanel != null && IsInstanceValid(_rootPanel))
            {
                _fadeTween?.Kill();
                _fadeTween = _rootPanel.CreateTween();
                _fadeTween.TweenProperty(_rootPanel, "modulate:a", 0f, 0.25);
                _fadeTween.TweenCallback(Callable.From(() =>
                {
                    if (_rewardLayer != null && IsInstanceValid(_rewardLayer))
                        _rewardLayer.Visible = false;
                    if (_rootPanel != null && IsInstanceValid(_rootPanel))
                        _rootPanel.Modulate = new Color(1f, 1f, 1f, 1f);
                }));
            }
            else
            {
                _rewardLayer.Visible = false;
            }

            OnScreenClosed?.Invoke();
        }

        /// <summary>
        /// Removes the reward UI from the scene tree entirely and frees resources
        /// </summary>
        public static void RemoveUI()
        {
            _fadeTween?.Kill();
            _fadeTween = null;

            if (_rewardLayer != null && IsInstanceValid(_rewardLayer))
                _rewardLayer.QueueFree();

            _rewardLayer      = null;
            _rootPanel        = null;
            _itemContainer    = null;
            _proceedButton    = null;
            _remainingRewards = 0;
        }

        #endregion

        #region Internal Reward Adding

        /// <summary>
        /// Adds a single reward to the screen on the main thread.
        /// If the screen is not yet open it will be created and shown.
        /// If it is already open the button is appended live.
        /// </summary>
        private static void AddRewardOnMainThread(ArchipelagoRewardData data)
        {
            try
            {
                if (_rewardLayer == null || !IsInstanceValid(_rewardLayer))
                    CreateUI();

                if (_itemContainer == null || !IsInstanceValid(_itemContainer))
                {
                    LogUtility.Error("Reward item container is null — cannot add reward");
                    return;
                }

                AppendRewardButton(data);

                if (!IsVisible)
                    ShowWithAnimation();

                LogUtility.Success($"Reward added to screen: {data.ItemName} (from {data.SenderName})");
            }
            catch (Exception ex)
            {
                LogUtility.Error($"Failed to add reward on main thread: {ex.Message}");
            }
        }

        /// <summary>
        /// Appends a single reward button to the item container and increments the remaining count.
        /// </summary>
        private static void AppendRewardButton(ArchipelagoRewardData data)
        {
            if (_itemContainer == null || !IsInstanceValid(_itemContainer)) return;

            _itemContainer.AddChild(CreateRewardButton(data));
            _remainingRewards++;
            UpdateProceedButton();
        }

        /// <summary>
        /// Makes the reward layer visible and plays the fade in animation on the rewards window.
        /// </summary>
        private static void ShowWithAnimation()
        {
            if (_rewardLayer == null || !IsInstanceValid(_rewardLayer)) return;

            _rewardLayer.Visible = true;

            if (_rootPanel != null && IsInstanceValid(_rootPanel))
            {
                _fadeTween?.Kill();
                _rootPanel.Modulate = new Color(1f, 1f, 1f, 0f);
                _fadeTween = _rootPanel.CreateTween();
                _fadeTween.TweenProperty(_rootPanel, "modulate", new Color(1f, 1f, 1f, 1f), 0.3);
            }
        }

        #endregion

        #region UI Construction

        /// <summary>
        /// Builds the full reward screen UI from scratch and injects it into the scene root
        /// node types (NRewardButton, NProceedButton, NScrollbar, etc.)
        /// </summary>
        private static void CreateUI()
        {
            try
            {
                var sceneTree = Engine.GetMainLoop() as SceneTree;
                if (sceneTree?.Root == null)
                {
                    LogUtility.Error("Failed to get SceneTree root — cannot create reward UI");
                    return;
                }

                var root = sceneTree.Root;

                // CanvasLayer sits above the game at layer 110
                _rewardLayer = new CanvasLayer { Name = "APRewardLayer", Layer = 110 };
                root.AddChild(_rewardLayer);

                // Full-screen root panel (blocks input to the game while open)
                _rootPanel = new Control { Name = "APRewardsScreen" };
                _rootPanel.SetAnchorsPreset(Control.LayoutPreset.FullRect);
                _rootPanel.MouseFilter = Control.MouseFilterEnum.Stop;
                _rewardLayer.AddChild(_rootPanel);

                // Dark semi-transparent backdrop
                var overlay = new ColorRect
                {
                    Color       = new Color(0f, 0f, 0f, 0.7f),
                    MouseFilter = Control.MouseFilterEnum.Stop
                };
                overlay.SetAnchorsPreset(Control.LayoutPreset.FullRect);
                _rootPanel.AddChild(overlay);

                // Rewards window
                var rewardsWindow = new Control { Name = "Rewards" };
                rewardsWindow.SetAnchorsPreset(Control.LayoutPreset.Center);
                rewardsWindow.OffsetLeft   = WindowOffsetLeft;
                rewardsWindow.OffsetTop    = WindowOffsetTop;
                rewardsWindow.OffsetRight  = WindowOffsetRight;
                rewardsWindow.OffsetBottom = WindowOffsetBottom;
                _rootPanel.AddChild(rewardsWindow);

                // Background panel
                var bg = new TextureRect { ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize };
                bg.SetAnchorsPreset(Control.LayoutPreset.FullRect);
                try { bg.Texture = GD.Load<Texture2D>(PanelPath); }
                catch (Exception ex) { LogUtility.Warn($"Could not load reward panel texture: {ex.Message}"); }
                rewardsWindow.AddChild(bg);

                // Banner
                var banner = new TextureRect
                {
                    ExpandMode  = TextureRect.ExpandModeEnum.IgnoreSize,
                    StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered
                };
                banner.SetAnchorsPreset(Control.LayoutPreset.CenterTop);
                banner.OffsetLeft   = BannerOffsetLeft;
                banner.OffsetTop    = BannerOffsetTop;
                banner.OffsetRight  = BannerOffsetRight;
                banner.OffsetBottom = BannerOffsetBottom;
                try { banner.Texture = GD.Load<Texture2D>(BannerPath); }
                catch (Exception ex) { LogUtility.Warn($"Could not load reward banner texture: {ex.Message}"); }
                bg.AddChild(banner);

                // Header label
                var header = CreateHeaderLabel();
                header.SetAnchorsPreset(Control.LayoutPreset.FullRect);
                header.OffsetLeft   = HeaderOffsetLeft;
                header.OffsetTop    = HeaderOffsetTop;
                header.OffsetRight  = HeaderOffsetRight;
                header.OffsetBottom = HeaderOffsetBottom;
                banner.AddChild(header);

                // Reward container mask
                // IMPORTANT NOTE: This is a sibling of Background (child of Rewards), *NOT* a child of Background.
                var mask = new TextureRect
                {
                    ExpandMode   = TextureRect.ExpandModeEnum.IgnoreSize,
                    FlipH        = true,
                    FlipV        = true,
                    ClipChildren = CanvasItem.ClipChildrenMode.Only,
                    ClipContents = true
                };
                mask.SetAnchorsPreset(Control.LayoutPreset.Center);
                mask.OffsetLeft   = MaskOffsetLeft;
                mask.OffsetTop    = MaskOffsetTop;
                mask.OffsetRight  = MaskOffsetRight;
                mask.OffsetBottom = MaskOffsetBottom;
                try { mask.Texture = GD.Load<Texture2D>(PanelPath); }
                catch (Exception ex) { LogUtility.Warn($"Could not load reward mask texture: {ex.Message}"); }
                rewardsWindow.AddChild(mask);

                // Rewards container 
                _itemContainer = new VBoxContainer { Name = "APRewardsContainer" };
                _itemContainer.Position          = new Vector2(ContainerLeft, ContainerTop);
                _itemContainer.CustomMinimumSize = new Vector2(ContainerWidth, 0);
                _itemContainer.AddThemeConstantOverride("separation", 10);
                mask.AddChild(_itemContainer);

                // Proceed / Skip button
                _proceedButton = CreateProceedButton();
                _rootPanel.AddChild(_proceedButton);

                LogUtility.Success("Archipelago reward UI created successfully");
            }
            catch (Exception ex)
            {
                LogUtility.Error($"Failed to create reward UI: {ex.Message}");
            }
        }

        /// <summary>
        /// Creates the "Archipelago Loot!" banner header label.
        /// This caused me unnecassary issues whilst making it for some reason
        /// </summary>
        private static Label CreateHeaderLabel()
        {
            var header = new Label
            {
                Name                = "APRewardHeader",
                Text                = "Archipelago Loot!",
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment   = VerticalAlignment.Center,
                AutowrapMode        = TextServer.AutowrapMode.Off,
                SizeFlagsHorizontal = Control.SizeFlags.Fill,
                SizeFlagsVertical   = Control.SizeFlags.Fill
            };

            try
            {
                var font = GD.Load<Font>(FontBold);
                if (font != null)
                    header.AddThemeFontOverride("font", font);
                else
                    LogUtility.Warn($"Could not load header font: {FontBold}");
            }
            catch (Exception ex)
            {
                LogUtility.Warn($"Failed to load reward header font: {ex.Message}");
            }

            header.AddThemeFontSizeOverride("font_size", HeaderFontSize);
            header.AddThemeColorOverride("font_color",         new Color(1f,       0.9647f,  0.8863f, 1f));
            header.AddThemeColorOverride("font_shadow_color",  new Color(0f,       0f,       0f,      0.1255f));
            header.AddThemeColorOverride("font_outline_color", new Color(0.2902f,  0.2353f,  0.1647f, 0.7529f));
            header.AddThemeConstantOverride("shadow_offset_x", 6);
            header.AddThemeConstantOverride("shadow_offset_y", 5);
            header.AddThemeConstantOverride("outline_size",    16);

            return header;
        }

        /// <summary>
        /// Creates the styled Skip/Proceed button anchored to the bottom-right of the screen, side note: I forgot to add an image to it remind me later.
        /// </summary>
        private static Button CreateProceedButton()
        {
            var btn = new Button
            {
                Name              = "APProceedButton",
                Text              = "Skip",
                CustomMinimumSize = new Vector2(220, 60)
            };
            btn.SetAnchorsPreset(Control.LayoutPreset.BottomRight);
            btn.Position = new Vector2(-260, -100);
            btn.Pressed += Hide;
            return btn;
        }

        /// <summary>
        /// Creates a single reward row button for the given reward data.
        /// The button shows an icon on the left, the item name prominently,
        /// and the sender's name in smaller text below.
        /// </summary>
        /// <param name="data">The reward entry to represent.</param>
        private static Button CreateRewardButton(ArchipelagoRewardData data)
        {
            var btn = new Button { CustomMinimumSize = new Vector2(0, ButtonHeight) };

            // Apply the in-game reward button texture as the button style
            try
            {
                var normalStyle = new StyleBoxTexture { Texture = GD.Load<Texture2D>(ItemBtnPath) };
                var hoverStyle  = new StyleBoxTexture { Texture = GD.Load<Texture2D>(ItemBtnPath) };
                btn.AddThemeStyleboxOverride("normal",  normalStyle);
                btn.AddThemeStyleboxOverride("hover",   hoverStyle);
                btn.AddThemeStyleboxOverride("pressed", normalStyle);
                btn.AddThemeStyleboxOverride("focus",   normalStyle);
            }
            catch (Exception ex)
            {
                LogUtility.Warn($"Could not load reward button texture: {ex.Message}");
            }

            // Row layout: [icon] [vbox: item name / sender name]
            var hbox = new HBoxContainer
            {
                Alignment = BoxContainer.AlignmentMode.Begin
            };
            hbox.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            hbox.OffsetLeft = 12;
            hbox.OffsetRight = -12;
            hbox.AddThemeConstantOverride("separation", 10);
            btn.AddChild(hbox);

            // Optional icon slot
            if (!string.IsNullOrEmpty(data.IconPath))
            {
                try
                {
                    var icon = new TextureRect
                    {
                        Texture           = GD.Load<Texture2D>(data.IconPath),
                        CustomMinimumSize = new Vector2(IconSlotSize, IconSlotSize),
                        ExpandMode        = TextureRect.ExpandModeEnum.IgnoreSize,
                        StretchMode       = TextureRect.StretchModeEnum.KeepAspectCentered,
                        SizeFlagsVertical = Control.SizeFlags.ShrinkCenter
                    };
                    hbox.AddChild(icon);
                }
                catch (Exception ex)
                {
                    LogUtility.Warn($"Could not load reward icon '{data.IconPath}': {ex.Message}");
                }
            }

            // Text column: item name (large) + sender (small)
            var vbox = new VBoxContainer { SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
            vbox.AddThemeConstantOverride("separation", 2);
            hbox.AddChild(vbox);

            // Item name label
            var nameLabel = CreateTextLabel(data.ItemName, RewardNameFontSize, new Color(1f, 0.965f, 0.886f));
            vbox.AddChild(nameLabel);

            // Sender name label (only shown if we have a sender)
            if (!string.IsNullOrEmpty(data.SenderName))
            {
                var senderLabel = CreateTextLabel($"from {data.SenderName}", RewardSenderFontSize, new Color(0.7f, 0.85f, 1f));
                vbox.AddChild(senderLabel);
            }

            // Grant the item and dismiss the button on click
            btn.Pressed += () =>
            {
                // Disable button immediately to prevent double-clicking while the async grant runs
                btn.Disabled = true;

                if (data.GrantAction != null)
                {
                    // Fire the async grant and log any failure — we don't await here since
                    // btn.Pressed is a sync signal handler, but the grant runs on the main thread
                    var task = data.GrantAction.Invoke();
                    task.ContinueWith(t =>
                    {
                        if (t.Exception != null)
                            LogUtility.Error($"Grant failed for '{data.ItemName}': {t.Exception.InnerException?.Message ?? t.Exception.Message}");
                    }, System.Threading.Tasks.TaskContinuationOptions.OnlyOnFaulted);
                }

                data.OnClaimed?.Invoke();
                btn.QueueFree();
                _remainingRewards--;
                UpdateProceedButton();

                // Auto-hide once all rewards are dismissed
                if (_remainingRewards <= 0)
                    Hide();
            };

            return btn;
        }

        /// <summary>
        /// Creates a single-line text label using the game's regular font.
        /// Uses a plain <see cref="Label"/> for reliable rendering on procedurally-built node trees.
        /// </summary>
        /// <param name="text">The text to display.</param>
        /// <param name="fontSize">The font size override.</param>
        /// <param name="color">The font color override.</param>
        private static Label CreateTextLabel(string text, int fontSize, Color color)
        {
            var label = new Label
            {
                Text                = text,
                VerticalAlignment   = VerticalAlignment.Center,
                AutowrapMode        = TextServer.AutowrapMode.Off,
                SizeFlagsHorizontal = Control.SizeFlags.Fill
            };

            try
            {
                var font = GD.Load<Font>(FontRegular);
                if (font != null)
                    label.AddThemeFontOverride("font", font);
                else
                    LogUtility.Warn($"Could not load reward label font: {FontRegular}");
            }
            catch (Exception ex)
            {
                LogUtility.Warn($"Failed to load reward label font: {ex.Message}");
            }

            label.AddThemeFontSizeOverride("font_size", fontSize);
            label.AddThemeColorOverride("font_color", color);

            return label;
        }

        #endregion

        #region Helpers

        /// <summary>
        /// Updates the proceed/skip button text depending on how many rewards remain unclaimed, this is kinda unnecassary but why not
        /// </summary>
        private static void UpdateProceedButton()
        {
            if (_proceedButton != null && IsInstanceValid(_proceedButton))
                _proceedButton.Text = _remainingRewards > 0 ? "Skip" : "Proceed";
        }

        /// <summary>
        /// Maps an <see cref="ItemInfo"/> to the async action that grants it to the player
        /// Returns null for item types with no in-run grant (e.g. Unlock, handled separately)
        /// </summary>
        /// <param name="item">The received Archipelago item.</param>
        /// <returns>An async grant action, or null if not applicable.</returns>
        private static Func<Task>? GetGrantAction(ItemInfo item)
        {
            switch (item.GetRawItemID())
            {
                case APItem.OneGold:      return () => GameUtility.GrantGold(1);
                case APItem.FiveGold:     return () => GameUtility.GrantGold(5);
                case APItem._15Gold:      return () => GameUtility.GrantGold(15);
                case APItem._30Gold:      return () => GameUtility.GrantGold(30);
                case APItem.BossGold:     return () => GameUtility.GrantGold(100);
                case APItem.Relic:        return () => GameUtility.GrantRelic();
                case APItem.Potion:       return () => GameUtility.GrantPotion();
                case APItem.CardReward:   return () => GameUtility.GrantCardReward(rare: false);
                case APItem.RareCardReward: return () => GameUtility.GrantCardReward(rare: true);
                default:
                    // Unlock is handled by GameUtility.UnlockCharacter in ArchipelagoClient.ProcessItem
                    // Progressive items (rest, shop slots, etc.) have not been yet implemented
                    return null;
            }
        }

        /// <summary>
        /// Maps an <see cref="ItemInfo"/> received from Archipelago to the appropriate
        /// reward screen icon resource path, based on the item's <see cref="APItem"/> ID.
        /// </summary>
        /// <param name="item">The received Archipelago item.</param>
        /// <returns>A resource path string, or <see cref="string.Empty"/> if no icon is available.</returns>
        private static string GetIconForItem(ItemInfo item)
        {
            switch (item.GetRawItemID())
            {
                case APItem.OneGold:
                case APItem.FiveGold:
                case APItem.BossGold:
                case APItem._15Gold:
                case APItem._30Gold:
                    return IconGold;

                case APItem.CardReward:
                case APItem.RareCardReward:
                    return IconCard;

                case APItem.Relic:
                case APItem.BossRelic:
                    return IconRelic;

                default:
                    return string.Empty;
            }
        }

        /// <summary>
        /// Infers an icon path from a plain reward text string
        /// Used by the <see cref="ShowRewards(List{string})"/> overload.
        /// </summary>
        /// <param name="text">The reward description text.</param>
        /// <returns>A resource path string, or <see cref="string.Empty"/> if no match found.</returns>
        private static string GetAutoIcon(string text)
        {
            string lower = text.ToLower();
            if (lower.Contains("gold")   || lower.Contains("money"))    return IconGold;
            if (lower.Contains("card"))                                  return IconCard;
            if (lower.Contains("relic")  || lower.Contains("shuriken")) return IconRelic;
            return string.Empty;
        }

        /// <summary>
        /// Checks if a GodotObject instance is valid (not null and not freed)
        /// </summary>
        /// <param name="obj">The GodotObject instance to check.</param>
        /// <returns>True if the instance is valid, false otherwise.</returns>
        private static bool IsInstanceValid(GodotObject obj)
        {
            return GodotObject.IsInstanceValid(obj);
        }

        #endregion
    }
}
