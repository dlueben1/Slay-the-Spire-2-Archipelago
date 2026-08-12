using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.ControllerInput;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.HoverTips;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Rewards;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Nodes.Screens.Capstones;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using MegaCrit.Sts2.Core.Nodes.Screens.ScreenContext;
using MegaCrit.Sts2.Core.Runs;
using StS2AP.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using static StS2AP.Data.ItemTable;
using ItemInfo = Archipelago.MultiClient.Net.Models.ItemInfo;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using StS2AP.Models;
using System.Reflection;

namespace StS2AP.UI
{
    public partial class APRewardScreenNode : Control, IOverlayScreen
    {
        private bool _hotkeysRegistered;
        private bool _blocksUnderlyingHotkeys;

        public Button? DefaultFocus { get; set; }
        public NetScreenType ScreenType => NetScreenType.Rewards; 
        public bool UseSharedBackstop => true; 
        public Control? DefaultFocusedControl => DefaultFocus; 

        public void AfterOverlayOpened() { }

        public void AfterOverlayClosed()
        {
            UnregisterHotkeys();
            QueueFree();
        }

        public void AfterOverlayShown()
        {
            Visible = true;
            RegisterHotkeys();

            // ActiveScreenContext updates after NOverlayStack invokes this callback.
            // Defer focus so the overlay's recursive focus behavior is enabled first.
            Callable.From(() => DefaultFocus?.GrabFocus()).CallDeferred();
        }

        public void AfterOverlayHidden()
        {
            // Let the native screen above AP own input. In particular, map and deck
            // should temporarily hide a card picker exactly as they do elsewhere.
            UnregisterHotkeys();
            Visible = false;
        }

        internal void UnregisterHotkeys()
        {
            var hotkeyManager = NHotkeyManager.Instance;
            if (_hotkeysRegistered)
            {
                hotkeyManager?.RemoveHotkeyPressedBinding(MegaInput.cancel, ArchipelagoRewardUI.Hide);
                hotkeyManager?.RemoveHotkeyPressedBinding(MegaInput.pauseAndBack, ArchipelagoRewardUI.Hide);
                hotkeyManager?.RemoveHotkeyReleasedBinding(MegaInput.viewMap, ArchipelagoRewardUI.CloseToMap);
                hotkeyManager?.RemoveHotkeyReleasedBinding(MegaInput.viewDeckAndTabLeft, ArchipelagoRewardUI.CloseToDeck);
                _hotkeysRegistered = false;
            }

            if (_blocksUnderlyingHotkeys)
            {
                hotkeyManager?.RemoveBlockingScreen(this);
                _blocksUnderlyingHotkeys = false;
            }
        }

        private void RegisterHotkeys()
        {
            var hotkeyManager = NHotkeyManager.Instance;
            if (hotkeyManager == null)
            {
                return;
            }

            // Block underlying room/top-bar shortcuts, then add only the
            // actions the AP reward screen intentionally supports on top.
            if (!_blocksUnderlyingHotkeys)
            {
                hotkeyManager.AddBlockingScreen(this);
                _blocksUnderlyingHotkeys = true;
            }

            if (_hotkeysRegistered)
            {
                return;
            }

            hotkeyManager.PushHotkeyPressedBinding(MegaInput.cancel, ArchipelagoRewardUI.Hide);
            hotkeyManager.PushHotkeyPressedBinding(MegaInput.pauseAndBack, ArchipelagoRewardUI.Hide);
            hotkeyManager.PushHotkeyReleasedBinding(MegaInput.viewMap, ArchipelagoRewardUI.CloseToMap);
            hotkeyManager.PushHotkeyReleasedBinding(MegaInput.viewDeckAndTabLeft, ArchipelagoRewardUI.CloseToDeck);
            _hotkeysRegistered = true;
        }
    }
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

        /// <summary>
        /// The index of the item received from the multiworld
        /// </summary>
        public int Index { get; set; }

        /// <summary>The primary item name shown in large text on the reward button.</summary>
        public string ItemName { get; set; } = string.Empty;

        /// <summary>The player who sent this item (shown in smaller text below the item name)</summary>
        public string SenderName { get; set; } = string.Empty;

        /// <summary>
        /// Where the item was found, shown in small text on the right side of the button.
        /// </summary>
        public string FoundLocation { get; set; } = string.Empty;

        /// <summary>Resource path to the icon shown on the left of the reward button. Empty = no icon</summary>
        public string IconPath { get; set; } = string.Empty;

        /// <summary>
        /// The async action that grants this item to the player when the button is clicked.
        /// Returns true if the reward should be removed from the menu, false if it should stay
        /// (e.g. a card reward that was skipped). Null means the button is display-only.
        /// </summary>
        public Func<Task<bool>>? GrantAction { get; set; }

        /// <summary>
        /// Relics linked to one AP item. When present, the UI renders a single grouped reward
        /// and consumes the AP item only after one of these relics is granted.
        /// </summary>
        public IReadOnlyList<RelicModel>? LinkedRelicChoices { get; set; }

        /// <summary>Whether linked relic choices should use the Ancient-specific button tint.</summary>
        public bool UseAncientRelicStyle { get; set; }

        /// <summary>The relic whose native hover tips should be shown for this reward row.</summary>
        public RelicModel? TooltipRelic { get; set; }

        /// <summary>The potion whose native hover tips should be shown for this reward row.</summary>
        public PotionModel? TooltipPotion { get; set; }

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
        private enum ReturnDestination
        {
            Room,
            Map,
            Deck,
        }

        private static APRewardScreenNode? _rootPanel;
        private static VBoxContainer? _itemContainer;
        private static Button? _proceedButton;
        private static Tween? _fadeTween;
        private static bool _isClosing;
        private static Texture2D? _linkedRewardChainTexture;
        private static bool _linkedRewardChainTextureResolved;
        private static readonly PropertyInfo? ChainImagePathProperty =
            AccessTools.Property(typeof(NLinkedRewardSet), "ChainImagePath");
        private static ReturnDestination _returnDestination;

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
        
        // Linked relic choices mirror the base game's compact NLinkedRewardSet layout:
        // buttons remain close together while the chain renders over both entries.
        private const float LinkedChoiceSeparation = 3f;
        private const float LinkedChoiceChainWidth = 104f;
        private const float LinkedChoiceChainHeight = 88f;
        private const int LinkedChoiceTextBottomBias = 8;

        // Ancient Relics related settings
        private static readonly Color AncientButtonNormalColor = new(0.78f, 0.48f, 0.95f);
        private static readonly Color AncientButtonHoverColor = new(0.95f, 0.62f, 1f);
        private static readonly Color AncientButtonPressedColor = new(0.65f, 0.34f, 0.82f);
        private static readonly Color AncientButtonDisabledColor = new(0.45f, 0.30f, 0.55f, 0.8f);

        private static int _remainingRewards = 0;

        /// <summary>
        /// Invoked when the reward screen is closed (all rewards dismissed or skipped)
        /// </summary>
        public static Action? OnScreenClosed;

        /// <summary>
        /// Whether the UI is open or not. 
        /// Note: This is different from IsVisible, which can be false if the UI is hidden temporarily by another overlay.
        /// </summary>
        public static bool IsOpen => _rootPanel != null && IsInstanceValid(_rootPanel) && _rootPanel.IsInsideTree();

        /// <summary>
        /// True when AP itself is the visible top overlay. A native picker above AP
        /// makes AP button/hotkey presses a no-op until that picker is resolved.
        /// </summary>
        internal static bool IsActive =>
            IsOpen && ActiveScreenContext.Instance.IsCurrent(_rootPanel);

        /// <summary>
        /// Toggles AP rewards only when AP is closed or is itself the active overlay.
        /// Removing AP from beneath an awaiting native picker would strand its task.
        /// </summary>
        public static void Toggle()
        {
            if (!IsOpen)
            {
                ShowRewards();
                return;
            }

            if (IsActive)
            {
                Hide();
            }
            else
            {
                // This case occurs like when you open a card reward within the AP menu
                // In this case we do nothing so the user must either pick a reward or skip.
                LogUtility.Debug("Ignoring AP reward toggle while a nested overlay is active");
            }
        }

        #region Public API

        /// <summary>
        /// Primary entry point called by <see cref="ArchipelagoClient"/> when an item is received
        /// from the Archipelago server Thread-safe defers the UI operation to the main thread
        /// </summary>
        /// <param name="item">The item received from the Archipelago server.</param>
        [Obsolete("I don't think this is used anywhere, and if it needs to be, we need to update this logic")]
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

        [Obsolete("Just used for testing, we may want to delete this soon.")]
        public static void ShowTestRewards()
        {
            var testRewards = new List<ArchipelagoRewardData>
            {
                new ArchipelagoRewardData
                {
                    ItemName    = "Relic",
                    SenderName  = "Archipelago",
                    IconPath    = IconRelic,
                    GrantAction = async () => { await GameUtility.GrantRelic(); return true; }
                },
                new ArchipelagoRewardData
                {
                    ItemName    = "50 Gold",
                    SenderName  = "Archipelago",
                    IconPath    = IconGold,
                    GrantAction = async () => { await GameUtility.GrantGold(50); return true; }
                },
                new ArchipelagoRewardData
                {
                    ItemName    = "Card Reward",
                    SenderName  = "TestPlayer",
                    IconPath    = IconCard,
                    GrantAction = () => GameUtility.GrantCardReward(index: -1, rare: false)
                },
            };
            Callable.From(() => ShowRewards(testRewards)).CallDeferred();
        }

        /// <summary>
        /// Overload of `ShowRewards()` that assumes we want to show all available items.
        /// Ends up calling `ShowRewards()` with a pre-built list of `ArchipelagoRewardData` objects based on the player's current multiworld progress.
        /// </summary>
        public static void ShowRewards()
        {
            // Ignore if current player is null
            var currentPlayer = GameUtility.CurrentPlayer;
            if (currentPlayer == null) return;


            // Get Unused items from the Multiworld for our current character
            var availableItems = ArchipelagoClient.Progress.AllReceivedItems
                                .Where(i => !ArchipelagoClient.Progress.UsedItems.Contains(i.Index) && i.Item.GetCharacterOffset() == GameUtility.CurrentCharacterID);
            
            // Prepare them for the UI
            var rewardDataList = availableItems.Where(i => i.Item.GetCharacterSpecificItemID().CanBePickedUp()).Select(i =>
            {
                var data = new ArchipelagoRewardData
                {
                    Index = i.Index,
                    ItemOriginID = i.Item.LocationId,
                    ItemName    = i.Item.ItemDisplayName,
                    SenderName  = i.Item.Player.Name,
                    FoundLocation = i.Item.LocationDisplayName,
                    IconPath    = GetIconForItem(i.Item),
                    GrantAction = GetGrantAction(i.Item),
                };

                // Relic items received from AP offer a stable, persisted choice. This does not
                // affect relic rewards created by the base game or other mods.
                var rawId = i.Item.GetCharacterSpecificItemID();
                if (rawId == APItem.Relic)
                {
                    var choiceCount = ArchipelagoClient.Settings?.RelicChoiceCount ?? 1;
                    var choices = ArchipelagoClient.Progress.GetOrAssignRelicChoices(i.Index, currentPlayer, choiceCount);
                    if (choices.Count > 0)
                    {
                        data.ItemName = "Choose a Relic";
                        data.LinkedRelicChoices = choices;
                    }
                    else
                    {
                        // Fail closed: do not consume the AP item if its choices could not be built.
                        data.ItemName = "Relic Choice Unavailable";
                        data.GrantAction = () => Task.FromResult(false);
                    }
                }

                if (rawId == APItem.ProgressiveAncient)
                {
                    var choices = ArchipelagoClient.Progress.GetOrAssignAncientRelicChoices(i.Index, currentPlayer);
                    if (choices.Count == AncientRelicPool.ChoiceCount)
                    {
                        data.ItemName = "Choose an Ancient Relic";
                        data.LinkedRelicChoices = choices;
                        data.UseAncientRelicStyle = true;
                    }
                    else
                    {
                        // Fail closed: do not consume the AP item if its choice pool could not be built.
                        data.ItemName = "Ancient Relic Choice Unavailable";
                        data.GrantAction = () => Task.FromResult(false);
                    }
                }

                // For card reward items, use the cached GrantAction so skipping preserves the reward
                if (rawId == APItem.CardReward || rawId == APItem.RareCardReward)
                {
                    bool isRare = rawId == APItem.RareCardReward;
                    int itemIndex = i.Index;
                    data.GrantAction = async () => await GameUtility.GrantCardReward(itemIndex, rare: isRare);
                }

                if(rawId == APItem.Potion)
                {
                    var potion = ArchipelagoClient.Progress.GetOrAssignPotion(i.Index, currentPlayer);
                    if(potion != null)
                    {
                        // Potion assignments stay canonical for persistence/granting. Use an
                        // owner-bound mutable copy so dynamic tooltip variables resolve safely.
                        var tooltipPotion = potion.ToMutable();
                        tooltipPotion.Owner = currentPlayer;

                        data.ItemName = potion.Title.GetRawText();
                        data.IconPath = potion.ImagePath;
                        data.TooltipPotion = tooltipPotion;
                        data.GrantAction = async () => { return await GameUtility.GrantPotion(potion); };
                    }
                }

                return data;
            }).ToList();

            rewardDataList.ForEach(item => item.OnClaimed = () =>
            {
                // Mark the item as used in the Multiworld so it doesn't show up again if we reopen the screen
                ArchipelagoClient.Progress.UsedItems.Add(item.Index);
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
                if (_rootPanel == null || !IsInstanceValid(_rootPanel))
                {
                    CreateUI();
                }

                if (_itemContainer == null || !IsInstanceValid(_itemContainer))
                {
                    LogUtility.Error("Reward item container is null after UI creation — cannot show rewards");
                    return;
                }

                // Clear any previously displayed reward buttons
                foreach (var child in _itemContainer.GetChildren())
                    child.QueueFree();

                _remainingRewards = 0;

                // Inject a reward for any remaining gold (if applicable)
                ArchipelagoGoldOffer offer = ArchipelagoClient.Progress.PrepareGoldOffer();

                if (offer.GrantedAmount > 0)
                {
                    rewards.Insert(0, new ArchipelagoRewardData
                    {
                        ItemName = $"{offer.GrantedAmount} Gold",
                        SenderName = "",
                        IconPath = IconGold,
                        GrantAction = async () =>
                        {
                            var amountToGrant = ArchipelagoClient.Progress.ConsumeGoldOffer(offer);
                            
                            await GameUtility.GrantGold(amountToGrant);
                            return true;
                        }
                    });
                }

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
            LogUtility.Debug("Reward UI Hide() called");

            if (_isClosing || _rootPanel == null || !IsInstanceValid(_rootPanel))
                return;

            _isClosing = true;

            // Fade out the rewards window, then hide the layer
            if (_rootPanel != null && IsInstanceValid(_rootPanel))
            {
                _fadeTween?.Kill();
                _fadeTween = _rootPanel.CreateTween();
                _fadeTween.TweenProperty(_rootPanel, "modulate:a", 0f, 0.25);
                _fadeTween.TweenCallback(Callable.From(() =>
                {
                    if (_rootPanel != null && IsInstanceValid(_rootPanel))
                        NOverlayStack.Instance?.Remove(_rootPanel);

                    var destination = _returnDestination;
                    _rootPanel = null;
                    _isClosing = false;
                    _returnDestination = ReturnDestination.Room;
                    OnScreenClosed?.Invoke();
                    RestoreDestination(destination);
                }));
            }
        }

        /// <summary>
        /// Makes the map the last requested destination and closes AP. Used by
        /// AP's hotkey and by the map-open compatibility patch.
        /// </summary>
        internal static void CloseToMap()
        {
            _returnDestination = ReturnDestination.Map;
            Hide();
        }

        /// <summary>
        /// Makes a fresh deck screen the last requested destination and closes AP.
        /// </summary>
        internal static void CloseToDeck()
        {
            _returnDestination = ReturnDestination.Deck;
            Hide();
        }

        /// <summary>
        /// Removes the reward UI from the scene tree entirely and frees resources
        /// </summary>
        public static void RemoveUI()
        {
            LogUtility.Debug("Reward UI RemoveUI() called");
            
            _fadeTween?.Kill();
            _fadeTween = null;
            (_rootPanel as APRewardScreenNode)?.UnregisterHotkeys();

            if (_rootPanel != null && IsInstanceValid(_rootPanel))
                _rootPanel.QueueFree();

            _rootPanel        = null;
            _itemContainer    = null;
            _proceedButton    = null;
            _remainingRewards = 0;
            _isClosing        = false;
            _returnDestination = ReturnDestination.Room;
        }

        #endregion

        #region Navigation Coordination

        /// <summary>
        /// Returns to a room before AP is pushed. Map and capstone screens outrank
        /// overlays in ActiveScreenContext, so leaving either open would make AP or
        /// its nested native card picker visible but unable to receive input.
        /// </summary>
        private static void PrepareForOpen()
        {
            _returnDestination = ReturnDestination.Room;

            var capstoneContainer = NCapstoneContainer.Instance;
            var currentCapstone = capstoneContainer?.CurrentCapstoneScreen;
            if (currentCapstone is NDeckViewScreen)
            {
                _returnDestination = ReturnDestination.Deck;
            }

            if (currentCapstone != null)
            {
                capstoneContainer!.Close();
            }

            var mapScreen = NMapScreen.Instance;
            if (mapScreen?.IsOpen != true)
            {
                return;
            }

            // Only remember the map when it was the active high-priority screen.
            // A deck over a map restores just a fresh deck; unsupported capstones
            // intentionally return to the room.
            if (currentCapstone == null)
            {
                _returnDestination = ReturnDestination.Map;
            }

            mapScreen.Close(animateOut: false);
        }

        private static void RestoreDestination(ReturnDestination destination)
        {
            switch (destination)
            {
                case ReturnDestination.Map:
                    NMapScreen.Instance?.Open(isOpenedFromTopBar: true);
                    break;
                case ReturnDestination.Deck:
                    var player = GameUtility.CurrentPlayer;
                    if (player != null)
                    {
                        NDeckViewScreen.ShowScreen(player);
                        NRun.Instance?.GlobalUi.TopBar.Deck.ToggleAnimState();
                    }
                    break;
                case ReturnDestination.Room:
                default:
                    break;
            }
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
                if (_rootPanel == null || !IsInstanceValid(_rootPanel))
                    CreateUI();

                if (_itemContainer == null || !IsInstanceValid(_itemContainer))
                {
                    LogUtility.Error("Reward item container is null — cannot add reward");
                    return;
                }

                AppendRewardButton(data);

                if (!IsOpen)
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

            var rewardControl = data.LinkedRelicChoices?.Count > 0
                ? CreateRelicChoiceGroup(data)
                : CreateRewardButton(data);
            _itemContainer.AddChild(rewardControl);
            _remainingRewards++;
            UpdateProceedButton();
        }

        /// <summary>
        /// Makes the reward layer visible and plays the fade in animation on the rewards window.
        /// </summary>
        private static void ShowWithAnimation()
        {
            if (_rootPanel == null || !IsInstanceValid(_rootPanel)) return;

            _rootPanel.Visible = true;

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

                PrepareForOpen();

                // Full-screen root panel (blocks input to the game while open)
                _isClosing = false;
                _rootPanel = new APRewardScreenNode { Name = "APRewardsScreen" };
                _rootPanel.SetAnchorsPreset(Control.LayoutPreset.FullRect);
                _rootPanel.MouseFilter = Control.MouseFilterEnum.Stop;

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

                // ScrollContainer fills the mask area so rewards can be scrolled when there are too many to display at once
                var scrollContainer = new ScrollContainer { Name = "APRewardsScroll" };
                scrollContainer.SetAnchorsPreset(Control.LayoutPreset.FullRect);
                // Small inset so the scroll content doesn't sit flush against the mask edge
                scrollContainer.OffsetLeft   = ContainerLeft;
                scrollContainer.OffsetTop    = ContainerTop;
                scrollContainer.OffsetRight  = -ContainerLeft;
                scrollContainer.OffsetBottom = -ContainerLeft;
                scrollContainer.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;
                scrollContainer.VerticalScrollMode   = ScrollContainer.ScrollMode.Auto;
                mask.AddChild(scrollContainer);

                // Rewards container sits inside the scroll container
                _itemContainer = new VBoxContainer { Name = "APRewardsContainer" };
                _itemContainer.CustomMinimumSize = new Vector2(ContainerWidth, 0);
                _itemContainer.SizeFlagsHorizontal = Control.SizeFlags.Fill;
                _itemContainer.AddThemeConstantOverride("separation", 10);
                scrollContainer.AddChild(_itemContainer);

                // Proceed / Skip button
                _proceedButton = CreateProceedButton();
                _rootPanel.AddChild(_proceedButton);

                _rootPanel.DefaultFocus = _proceedButton;
                NOverlayStack.Instance?.Push(_rootPanel);

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
        /// Creates visually linked relic buttons which collectively represent one AP item.
        /// </summary>
        private static Control CreateRelicChoiceGroup(ArchipelagoRewardData data)
        {
            // this is probably infinitely more complicated than it needs to be...
            // so im sorry if this hurts your brain or can be made simpler
            // i took inspiration from decompiled code of a similar mod that does this
            var choices = data.LinkedRelicChoices;
            if (choices == null || choices.Count == 0)
                return CreateRewardButton(data);

            var chainTexture = GetLinkedRewardChainTexture();
            var group = new Control
            {
                Name = $"RelicChoice_{data.Index}",
                CustomMinimumSize = new Vector2(0, choices.Count * ButtonHeight + (choices.Count - 1) * LinkedChoiceSeparation),
                SizeFlagsHorizontal = Control.SizeFlags.Fill
            };
            var buttonContainer = new VBoxContainer();
            buttonContainer.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            buttonContainer.AddThemeConstantOverride("separation", (int)LinkedChoiceSeparation);
            group.AddChild(buttonContainer);

            var buttons = new List<Button>(choices.Count);
            var resolving = false;

            void ResolveChoice(RelicModel relic)
            {
                if (resolving)
                    return;

                resolving = true;
                foreach (var button in buttons)
                    button.Disabled = true;

                GameUtility.TryGrantRelic(relic).ContinueWith(task =>
                {
                    var granted = task.Status == TaskStatus.RanToCompletion && task.Result;
                    var failure = task.Exception?.InnerException?.Message ?? task.Exception?.Message;

                    Callable.From(() =>
                    {
                        if (granted)
                            data.OnClaimed?.Invoke();

                        if (!GodotObject.IsInstanceValid(group))
                            return;

                        if (!granted)
                        {
                            if (!string.IsNullOrEmpty(failure))
                                LogUtility.Error($"Relic choice failed for '{relic.Id}': {failure}");

                            resolving = false;
                            foreach (var button in buttons.Where(button => GodotObject.IsInstanceValid(button)))
                                button.Disabled = false;
                            return;
                        }

                        group.QueueFree();
                        _remainingRewards--;
                        UpdateProceedButton();
                        if (_remainingRewards <= 0)
                            Hide();
                    }).CallDeferred();
                });
            }

            for (var index = 0; index < choices.Count; index++)
            {
                var relic = choices[index];
                var choiceData = new ArchipelagoRewardData
                {
                    Index = data.Index,
                    ItemOriginID = data.ItemOriginID,
                    ItemName = relic.Title.GetRawText(),
                    SenderName = data.SenderName,
                    FoundLocation = data.FoundLocation,
                    IconPath = relic.IconPath,
                    TooltipRelic = relic
                };

                var button = CreateRewardButton(
                    choiceData,
                    _ => ResolveChoice(relic),
                    data.UseAncientRelicStyle,
                    isLinkedChoice: true
                );
                buttons.Add(button);
                buttonContainer.AddChild(button);
            }

            if (chainTexture != null)
            {
                for (var index = 0; index < choices.Count - 1; index++)
                {
                    var chainCenterY = (index + 1) * ButtonHeight
                        + index * LinkedChoiceSeparation
                        + LinkedChoiceSeparation / 2f;
                    var chain = new TextureRect
                    {
                        Texture = chainTexture,
                        ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                        StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                        // Keep the chain in the reward screen's normal canvas order. Giving it
                        // a positive ZIndex lets it render above the separately managed pause menu.
                        MouseFilter = Control.MouseFilterEnum.Ignore
                    };
                    chain.SetAnchorsPreset(Control.LayoutPreset.CenterTop);
                    chain.OffsetLeft = -LinkedChoiceChainWidth / 2f;
                    chain.OffsetTop = chainCenterY - LinkedChoiceChainHeight / 2f;
                    chain.OffsetRight = LinkedChoiceChainWidth / 2f;
                    chain.OffsetBottom = chainCenterY + LinkedChoiceChainHeight / 2f;
                    group.AddChild(chain);
                }
            }

            return group;
        }

        private static Texture2D? GetLinkedRewardChainTexture()
        {
            if (_linkedRewardChainTextureResolved)
                return _linkedRewardChainTexture;

            _linkedRewardChainTextureResolved = true;
            try
            {
                var chainPath = ChainImagePathProperty?.GetValue(null) as string;
                if (!string.IsNullOrWhiteSpace(chainPath))
                    _linkedRewardChainTexture = PreloadManager.Cache.GetCompressedTexture2D(chainPath);
                else
                    LogUtility.Warn("Native linked-reward chain asset path was unavailable; using spacing-only relic choices");
            }
            catch (Exception ex)
            {
                LogUtility.Warn($"Failed to load native linked-reward chain asset: {ex.Message}");
            }

            return _linkedRewardChainTexture;
        }

        /// <summary>
        /// Creates a single reward row button for the given reward data.
        /// The button shows an icon on the left, the item name prominently,
        /// and the sender's name in smaller text below.
        /// </summary>
        /// <param name="data">The reward entry to represent.</param>
        /// <param name="customPressed">Optional group-owned click handler.</param>
        /// <param name="isAncientChoice">Whether to apply the Ancient-specific button tint.</param>
        /// <param name="isLinkedChoice">Whether this button belongs to an overlaid chain group.</param>
        private static Button CreateRewardButton(
            ArchipelagoRewardData data,
            Action<Button>? customPressed = null,
            bool isAncientChoice = false,
            bool isLinkedChoice = false)
        {
            var btn = new Button { CustomMinimumSize = new Vector2(0, ButtonHeight) };
            var owningPanel = _rootPanel;

            // Apply the in-game reward button texture as the button style
            try
            {
                var buttonTexture = GD.Load<Texture2D>(ItemBtnPath);
                var normalColor = isAncientChoice ? AncientButtonNormalColor : Colors.White;
                var hoverColor = isAncientChoice ? AncientButtonHoverColor : Colors.White;
                var pressedColor = isAncientChoice ? AncientButtonPressedColor : Colors.White;
                var disabledColor = isAncientChoice ? AncientButtonDisabledColor : Colors.White;

                StyleBoxTexture CreateButtonStyle(Color color) => new()
                {
                    Texture = buttonTexture,
                    ModulateColor = color
                };

                var normalStyle = CreateButtonStyle(normalColor);
                btn.AddThemeStyleboxOverride("normal",  normalStyle);
                btn.AddThemeStyleboxOverride("hover",   CreateButtonStyle(hoverColor));
                btn.AddThemeStyleboxOverride("pressed", CreateButtonStyle(pressedColor));
                btn.AddThemeStyleboxOverride("focus",   normalStyle);
                btn.AddThemeStyleboxOverride("disabled", CreateButtonStyle(disabledColor));
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
            var vbox = new VBoxContainer();
            vbox.AddThemeConstantOverride("separation", 2);

            if (isLinkedChoice)
            {
                // Move both text lines slightly upward so the larger chain can overlap
                // the button edges without obscuring the source line.
                var textMargin = new MarginContainer
                {
                    SizeFlagsHorizontal = Control.SizeFlags.Fill,
                    SizeFlagsVertical = Control.SizeFlags.ShrinkCenter
                };
                textMargin.AddThemeConstantOverride("margin_bottom", LinkedChoiceTextBottomBias);
                textMargin.AddChild(vbox);
                hbox.AddChild(textMargin);
            }
            else
            {
                vbox.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
                hbox.AddChild(vbox);
            }

            // Item name label
            var nameLabel = CreateTextLabel(data.ItemName, RewardNameFontSize, new Color(1f, 0.965f, 0.886f));
            vbox.AddChild(nameLabel);

            // Sender name label (only shown if we have a sender)
            if (!string.IsNullOrEmpty(data.SenderName))
            {
                var senderLabel = CreateTextLabel($"from {data.SenderName} ({data.FoundLocation})", RewardSenderFontSize, new Color(0.7f, 0.85f, 1f));
                vbox.AddChild(senderLabel);
            }

            if (data.TooltipRelic is { } tooltipRelic)
            {
                AttachModelHoverTips(
                    btn,
                    () => tooltipRelic.HoverTips,
                    $"relic '{tooltipRelic.Id}'"
                );
            }
            else if (data.TooltipPotion is { } tooltipPotion)
            {
                AttachModelHoverTips(
                    btn,
                    () => tooltipPotion.HoverTips,
                    $"potion '{tooltipPotion.Id}'"
                );
            }

            if (customPressed != null)
            {
                btn.Pressed += () => customPressed(btn);
                return btn;
            }

            // Grant the item and dismiss the button on click
            btn.Pressed += () =>
            {
                // Disable button immediately to prevent double-clicking while the async grant runs
                btn.Disabled = true;

                if (data.GrantAction != null)
                {
                    var task = data.GrantAction.Invoke();
                    task.ContinueWith(t =>
                    {
                        if (t.Exception != null)
                        {
                            LogUtility.Error($"Grant failed for '{data.ItemName}': {t.Exception.InnerException?.Message ?? t.Exception.Message}");
                            // Re-enable the button on failure so the player can try again
                            Callable.From(() =>
                            {
                                if (GodotObject.IsInstanceValid(btn))
                                    btn.Disabled = false;
                            }).CallDeferred();
                            return;
                        }

                        bool shouldRemove = t.Result;
                        Callable.From(() =>
                        {
                            if (shouldRemove)
                            {
                                // Reward consumption is authoritative even if this particular
                                // menu instance was closed or rebuilt while the picker was open.
                                data.OnClaimed?.Invoke();

                                if (!GodotObject.IsInstanceValid(btn) ||
                                    owningPanel == null ||
                                    !GodotObject.IsInstanceValid(owningPanel) ||
                                    !ReferenceEquals(_rootPanel, owningPanel))
                                {
                                    return;
                                }

                                btn.QueueFree();
                                _remainingRewards--;
                                UpdateProceedButton();
                                if (_remainingRewards <= 0)
                                    Hide();
                            }
                            else
                            {
                                // Reward was skipped — re-enable the button so the player can try again
                                if (GodotObject.IsInstanceValid(btn))
                                    btn.Disabled = false;
                            }
                        }).CallDeferred();
                    });
                }
                else
                {
                    // No grant action (display-only) — just dismiss
                    data.OnClaimed?.Invoke();
                    btn.QueueFree();
                    _remainingRewards--;
                    UpdateProceedButton();
                    // Auto-hide once all rewards are dismissed
                    if (_remainingRewards <= 0)
                        Hide();
                }
            };

            return btn;
        }

        /// <summary>
        /// Shows a model's native description and any extra hover tips while a reward button is
        /// hovered or keyboard-focused.
        /// </summary>
        private static void AttachModelHoverTips(
            Button button,
            Func<IEnumerable<IHoverTip>> hoverTipsFactory,
            string diagnosticSubject)
        {
            var isHovered = false;
            var isFocused = false;
            var isTooltipVisible = false;

            void ShowTooltip()
            {
                if (isTooltipVisible)
                    return;

                try
                {
                    var tipSet = NHoverTipSet.CreateAndShow(button, hoverTipsFactory(), HoverTipAlignment.Left);
                    isTooltipVisible = tipSet != null;
                }
                catch (Exception ex)
                {
                    LogUtility.Warn($"Failed to show tooltip for {diagnosticSubject}: {ex.Message}");
                }
            }

            void HideTooltip()
            {
                if (!isTooltipVisible)
                    return;

                try
                {
                    NHoverTipSet.Remove(button);
                }
                catch (Exception ex)
                {
                    LogUtility.Warn($"Failed to hide tooltip for {diagnosticSubject}: {ex.Message}");
                }
                finally
                {
                    isTooltipVisible = false;
                }
            }

            void RefreshTooltip()
            {
                if (isHovered || isFocused)
                    ShowTooltip();
                else
                    HideTooltip();
            }

            button.MouseEntered += () =>
            {
                isHovered = true;
                RefreshTooltip();
            };
            button.MouseExited += () =>
            {
                isHovered = false;
                RefreshTooltip();
            };
            button.FocusEntered += () =>
            {
                isFocused = true;
                RefreshTooltip();
            };
            button.FocusExited += () =>
            {
                isFocused = false;
                RefreshTooltip();
            };
            button.Pressed += HideTooltip;
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
        private static Func<Task<bool>>? GetGrantAction(ItemInfo item)
        {
            switch (item.GetCharacterSpecificItemID())
            {
                case APItem.OneGold:      return async () => { await GameUtility.GrantGold(1); return true; };
                case APItem.FiveGold:     return async () => { await GameUtility.GrantGold(5); return true; };
                case APItem.CombatGold:   return async () => { await GameUtility.GrantGold(15); return true; };
                case APItem.EliteGold:    return async () => { await GameUtility.GrantGold(40); return true; };
                case APItem.BossGold:     return async () => { await GameUtility.GrantGold(100); return true; };
                case APItem.Relic:        return async () => { await GameUtility.GrantRelic(); return true; };
                // Ancient choices require the received-item index and are built in ShowRewards().
                // Keep the obsolete AddReward(ItemInfo) path from consuming one as display-only.
                case APItem.ProgressiveAncient: return () => Task.FromResult(false);
                    // Need to do potion lookup before granting; see ShowRewards
                case APItem.Potion:       return async () => {  return false; };
                default:
                    // Card rewards are handled in ShowRewards() where the index is available
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
            switch (item.GetCharacterSpecificItemID())
            {
                case APItem.OneGold:
                case APItem.FiveGold:
                case APItem.BossGold:
                case APItem.CombatGold:
                case APItem.EliteGold:
                    return IconGold;

                case APItem.CardReward:
                case APItem.RareCardReward:
                    return IconCard;

                case APItem.Relic:
                case APItem.ProgressiveAncient:
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
