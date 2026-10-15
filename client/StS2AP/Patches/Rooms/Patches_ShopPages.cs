using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Merchant;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens.Shops;

namespace StS2AP.Patches
{
    /// <summary>
    /// Splits the merchant shop into independent vanilla and AP-check pages.
    /// </summary>
    public static class Patches_ShopPages
    {
        private const string ApNavIconPath = "res://images/APIcon.png";
        private const string HsvShaderPath = "res://shaders/hsv.gdshader";
        private static readonly Color ApNavArrowColor = new("62D68B");
        private static readonly Color ShopNavArrowColor = new("86B8CC");
        private static Texture2D? _apNavIconTexture;
        private static Shader? _hsvShader;

        private static Control? _toApPageButton;
        private static Control? _toVanillaButton;
        private static bool _isCoordinatingClose;
        private static int _navigationBlockDepth;

        #region Page Spawning

        /// <summary>
        /// When the vanilla merchant page finishes Initialize(), spawns a second copy of
        /// the same scene parked off-screen and binds it to the AP-only inventory.
        /// </summary>
        [HarmonyPatch(typeof(NMerchantInventory), nameof(NMerchantInventory.Initialize))]
        public static class SpawnApPage
        {
            private static bool _isSpawning;

            [HarmonyPostfix]
            public static void Postfix(NMerchantInventory __instance, MerchantInventory inventory, MerchantDialogueSet dialogue)
            {
                // AP_MP: AP shop pages wait for owner-only slots and synchronized purchases.
                if (!MultiplayerSupport.IsFeatureEnabled(MultiplayerFeature.Shops))
                    return;

                if (_isSpawning)
                {
                    return;
                }

                if (!Patches_ShopSanity.TryGetApInventory(inventory, out MerchantInventory apInventory))
                {
                    return;
                }

                if (__instance.HasMeta("StS2AP_ApPageSpawned"))
                {
                    return;
                }
                __instance.SetMeta("StS2AP_ApPageSpawned", true);

                _isSpawning = true;
                NMerchantInventory? spawnedApPage = null;
                Control? spawnedToApButton = null;
                Control? spawnedToVanillaButton = null;
                try
                {
                    ShopPageUtility.Reset();
                    _toApPageButton = null;
                    _toVanillaButton = null;

                    string? scenePath = __instance.SceneFilePath;
                    if (string.IsNullOrEmpty(scenePath))
                    {
                        LogUtility.Error("ShopPages: vanilla merchant inventory has no SceneFilePath, can't duplicate it.");
                        return;
                    }

                    PackedScene? scene = ResourceLoader.Load<PackedScene>(scenePath);
                    if (scene == null)
                    {
                        LogUtility.Error($"ShopPages: failed to (re)load merchant inventory scene at {scenePath}.");
                        return;
                    }

                    NMerchantInventory apPage = scene.Instantiate<NMerchantInventory>();
                    spawnedApPage = apPage;
                    __instance.GetParent().AddChildSafely(apPage);
                    apPage.Initialize(apInventory, dialogue);

                    KeepExitButtonOnscreen(__instance);
                    KeepExitButtonOnscreen(apPage);

                    // Card removal is vanilla-only, so hide the AP page's copy of it.
                    NMerchantCardRemoval? apPageRemoval = apPage.GetAllSlots().OfType<NMerchantCardRemoval>().FirstOrDefault();
                    if (apPageRemoval != null)
                    {
                        apPageRemoval.Visible = false;
                        apPageRemoval.MouseFilter = Control.MouseFilterEnum.Ignore;
                        LogUtility.Info("ShopPages: hid AP page's card removal slot.");
                    }
                    else
                    {
                        LogUtility.Error("ShopPages: couldn't find a card removal slot on the AP page to hide.");
                    }

                    Node commonParent = __instance.GetParent();

                    _toApPageButton = spawnedToApButton = BuildNavButton(__instance, "AP Checks >", () =>
                    {
                        ShopPageUtility.ShowApPage();
                        SyncNavButtonsToFrontPage();
                    });

                    if (_toApPageButton is NBackButton apRealButton)
                    {
                        ConfigureApNavArtwork(apRealButton);
                        FlipButtonArtwork(apRealButton);
                    }
                    commonParent.AddChildSafely(_toApPageButton);
                    _toApPageButton.Visible = false;

                    _toVanillaButton = spawnedToVanillaButton = BuildNavButton(apPage, "< Shop", () =>
                    {
                        ShopPageUtility.ShowVanillaPage();
                        SyncNavButtonsToFrontPage();
                    });
                    if (_toVanillaButton is NBackButton shopRealButton)
                    {
                        TintNavArrow(shopRealButton, ShopNavArrowColor, "shop");
                        TintShopNavBackground(shopRealButton);
                    }
                    commonParent.AddChildSafely(_toVanillaButton);
                    _toVanillaButton.Visible = false;

                    ShopPageUtility.Register(__instance, apPage);

                    foreach (MerchantEntry entry in inventory.AllEntries.Concat(apInventory.AllEntries))
                    {
                        entry.OnMerchantInventoryUpdated();
                    }

                    TaskHelper.RunSafely(ParkApPageOffscreen(__instance, apPage));
                    TaskHelper.RunSafely(PositionEdgeButtonsDeferred(__instance, commonParent, _toApPageButton, _toVanillaButton));
                }
                catch (System.Exception ex)
                {
                    // The AP page is optional presentation. Never let a duplicate-scene or
                    // fake-entry failure abort NMerchantRoom._Ready and strand the run behind
                    // the room-transition blackout.
                    LogUtility.Error(
                        "ShopPages: AP page initialization failed; removing the partial AP "
                            + $"page and continuing with the vanilla shop. {ex}"
                    );

                    ShopPageUtility.Reset();
                    QueueFreeIfValid(spawnedToApButton);
                    QueueFreeIfValid(spawnedToVanillaButton);
                    QueueFreeIfValid(spawnedApPage);
                    _toApPageButton = null;
                    _toVanillaButton = null;
                }
                finally
                {
                    _isSpawning = false;
                }
            }

            private static void QueueFreeIfValid(Node? node)
            {
                if (node != null && GodotObject.IsInstanceValid(node))
                {
                    node.QueueFreeSafely();
                }
            }

            private static async Task ParkApPageOffscreen(NMerchantInventory vanillaPage, NMerchantInventory apPage)
            {
                await vanillaPage.ToSignal(vanillaPage.GetTree(), SceneTree.SignalName.ProcessFrame);
                if (!GodotObject.IsInstanceValid(vanillaPage) || !GodotObject.IsInstanceValid(apPage))
                {
                    return;
                }
                apPage.Position = vanillaPage.Position + new Vector2(vanillaPage.Size.X, 0f);
                ShopPageUtility.RecordHomePositions();
            }

            /// <summary>
            /// Waits a frame before reading button.Size / ContentScaleSize, since both are
            /// unreliable during room pre-warm.
            /// </summary>
            private static async Task PositionEdgeButtonsDeferred(NMerchantInventory vanillaPage, Node commonParent, Control? toApButton, Control? toVanillaButton)
            {
                await vanillaPage.ToSignal(vanillaPage.GetTree(), SceneTree.SignalName.ProcessFrame);

                if (toApButton != null && GodotObject.IsInstanceValid(toApButton))
                {
                    PositionEdgeButton(toApButton, commonParent, onRight: true);
                }
                if (toVanillaButton != null && GodotObject.IsInstanceValid(toVanillaButton))
                {
                    PositionEdgeButton(toVanillaButton, commonParent, onRight: false);
                }
            }

            private static Control BuildNavButton(Node searchRoot, string fallbackText, System.Action onPressed)
            {
                NBackButton? template = FindDescendant<NBackButton>(searchRoot);
                if (template == null)
                {
                    LogUtility.Error("ShopPages: couldn't find an existing NBackButton to reuse, falling back to a plain button.");
                }
                else if (string.IsNullOrEmpty(template.SceneFilePath))
                {
                    LogUtility.Error("ShopPages: existing NBackButton has no SceneFilePath, falling back to a plain button.");
                }
                else
                {
                    PackedScene? scene = ResourceLoader.Load<PackedScene>(template.SceneFilePath);
                    if (scene == null)
                    {
                        LogUtility.Error($"ShopPages: failed to load back button scene at {template.SceneFilePath}, falling back to a plain button.");
                    }
                    else
                    {
                        NBackButton realButton = scene.Instantiate<NBackButton>();
                        realButton.Connect(NClickableControl.SignalName.Released, Callable.From<NButton>(_ => onPressed()));

                        LogUtility.Info($"ShopPages: successfully reused NBackButton for '{fallbackText}'.");
                        return realButton;
                    }
                }

                Button fallback = new Button
                {
                    Text = fallbackText,
                    CustomMinimumSize = new Vector2(160f, 60f),
                    Visible = false
                };
                fallback.Pressed += onPressed;
                return fallback;
            }

            private static void KeepExitButtonOnscreen(NMerchantInventory page)
            {
                NBackButton? exitButton = FindDescendant<NBackButton>(page);
                if (exitButton != null)
                {
                    exitButton.TopLevel = true;
                }
            }

            private static T? FindDescendant<T>(Node root) where T : class
            {
                foreach (Node child in root.GetChildren())
                {
                    if (child is T match)
                    {
                        return match;
                    }
                    T? found = FindDescendant<T>(child);
                    if (found != null)
                    {
                        return found;
                    }
                }
                return null;
            }

            private static void PositionEdgeButton(Control button, Node commonParent, bool onRight)
            {
                // Measure commonParent directly rather than the window, since the merchant
                // panel isn't guaranteed to span the full window.
                Vector2 windowSize = commonParent is Control commonControl && commonControl.Size.X > 0f && commonControl.Size.Y > 0f
                    ? commonControl.Size
                    : button.GetWindow()?.ContentScaleSize ?? new Vector2I(1920, 1080);

                Vector2 size = button.Size;
                if (size.X <= 0f || size.Y <= 0f)
                {
                    // Matches back_button.tscn's baked rect, not a rough guess.
                    size = new Vector2(200f, 110f);
                }

                const float edgeMargin = 30f;
                float xPos = onRight ? windowSize.X - size.X - edgeMargin : edgeMargin;
                float yPos = (windowSize.Y / 2f) - (size.Y / 2f);

                Vector2 targetShowPos = new Vector2(xPos, yPos);
                Vector2 hideOffset = onRight ? new Vector2(250f, 0f) : new Vector2(-250f, 0f);

                if (button is NBackButton backButton)
                {
                    // Override the internal show/hide positions so the button doesn't
                    // reset to its vanilla bottom-left spot.
                    backButton._showPos = targetShowPos;
                    backButton._hidePos = targetShowPos + hideOffset;

                    // _Ready() already started a hide tween toward the old _hidePos; kill
                    // it now that we own positioning, or it'll fight the line below.
                    if (backButton._moveTween is Tween staleTween)
                    {
                        staleTween.Kill();
                    }

                    backButton.GlobalPosition = targetShowPos + hideOffset;
                }
                else
                {
                    button.CustomMinimumSize = size;
                    button.Size = size;
                    button.GlobalPosition = targetShowPos;
                }
            }

            private static void FlipButtonArtwork(NBackButton button)
            {
                foreach (string childName in new[] { "Outline", "Image" })
                {
                    if (button.GetNodeOrNull(childName) is Control child)
                    {
                        float pivotX = (button.Size.X + child.OffsetRight - child.OffsetLeft) / 2f;
                        float pivotY = (button.Size.Y + child.OffsetBottom - child.OffsetTop) / 2f;
                        child.PivotOffset = new Vector2(pivotX, pivotY);
                        child.Scale = new Vector2(-1f, child.Scale.Y);
                    }
                    else
                    {
                        LogUtility.Error($"ShopPages: couldn't find '{childName}' on the AP-checks button to flip, it may render pointing the wrong way.");
                    }
                }
            }

            /// <summary>
            /// Keeps the native arrow, hover outline, focus handling, and slide animation while
            /// making the AP destination unmistakable with a green arrow and AP flower badge.
            /// </summary>
            private static void ConfigureApNavArtwork(NBackButton button)
            {
                if (!TintNavArrow(button, ApNavArrowColor, "AP checks"))
                {
                    return;
                }

                _apNavIconTexture ??= ResourceLoader.Load<Texture2D>(
                    ApNavIconPath,
                    null,
                    ResourceLoader.CacheMode.Reuse);
                if (_apNavIconTexture == null)
                {
                    LogUtility.Error($"ShopPages: failed to load AP navigation icon at {ApNavIconPath}.");
                    return;
                }

                Vector2 buttonSize = button.Size;
                if (buttonSize.X <= 0f || buttonSize.Y <= 0f)
                {
                    // Matches back_button.tscn's baked rect while the scene is still
                    // waiting for its first layout pass.
                    buttonSize = new Vector2(200f, 110f);
                }

                float iconSize = Mathf.Min(44f, buttonSize.Y * 0.4f);
                var apIcon = new TextureRect
                {
                    Name = "ArchipelagoShopNavIcon",
                    Texture = _apNavIconTexture,
                    // Mirror the badge to the plaque's right side, clear of the
                    // native directional arrow.
                    Position = new Vector2(
                        buttonSize.X - iconSize - (buttonSize.X * 0.01f),
                        ((buttonSize.Y - iconSize) / 2f) - 4f),
                    Size = new Vector2(iconSize, iconSize),
                    ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                    StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                    MouseFilter = Control.MouseFilterEnum.Ignore,
                };
                button.AddChildSafely(apIcon);
            }

            private static bool TintNavArrow(NBackButton button, Color tint, string destination)
            {
                if (button.GetNodeOrNull<CanvasItem>("Image/Icon") is not CanvasItem arrow)
                {
                    LogUtility.Error($"ShopPages: couldn't find 'Image/Icon' on the {destination} navigation button, so its arrow tint was not applied.");
                    return false;
                }

                arrow.SelfModulate = tint;
                return true;
            }

            private static void TintShopNavBackground(NBackButton button)
            {
                if (button.GetNodeOrNull<TextureRect>("Image") is not TextureRect background)
                {
                    LogUtility.Error("ShopPages: couldn't find 'Image' on the shop navigation button, so its background was not recolored.");
                    return;
                }

                _hsvShader ??= ResourceLoader.Load<Shader>(
                    HsvShaderPath,
                    null,
                    ResourceLoader.CacheMode.Reuse);
                if (_hsvShader == null)
                {
                    LogUtility.Error($"ShopPages: failed to load the navigation background shader at {HsvShaderPath}.");
                    return;
                }

                var material = new ShaderMaterial
                {
                    Shader = _hsvShader,
                };
                // Shift the native red plaque toward blue while retaining its painted
                // texture, shadows, hover outline, and cream arrow artwork.
                material.SetShaderParameter("h", 0.34f);
                material.SetShaderParameter("s", 0.8f);
                material.SetShaderParameter("v", 0.9f);
                background.Material = material;
            }
        }

        #endregion

        #region Open with vanilla

        [HarmonyPatch(typeof(NMerchantRoom), nameof(NMerchantRoom.OpenInventory))]
        public static class OpenApPageAlongsideVanilla
        {
            [HarmonyPostfix]
            public static void Postfix()
            {
                // AP_MP: AP shop presentation follows the synchronized purchase gate.
                if (!MultiplayerSupport.IsFeatureEnabled(MultiplayerFeature.Shops))
                    return;

                NMerchantInventory? apPage = ShopPageUtility.ApPageInstance;
                if (ShopPageUtility.HasPages && apPage != null && GodotObject.IsInstanceValid(apPage) && !apPage.IsOpen)
                {
                    apPage.Open();
                }
            }
        }

        /// <summary>
        /// Prevents NBackButton's OnWindowChange from resetting show/hide positions back to bottom-left
        /// </summary>
        [HarmonyPatch(typeof(NBackButton), "OnWindowChange")]
        public static class SuppressBackButtonWindowChange
        {
            [HarmonyPrefix]
            public static bool Prefix(NBackButton __instance)
            {
                if (ReferenceEquals(__instance, _toApPageButton) || ReferenceEquals(__instance, _toVanillaButton))
                {
                    return false; // Skip execution for custom edge buttons.
                }
                return true;
            }
        }

        /// <summary>
        /// fixing esc button softlock
        /// </summary>
        [HarmonyPatch(typeof(NBackButton), "get_Hotkeys")]
        public static class SuppressHotkeysForCustomNavButtons
        {
            [HarmonyPostfix]
            public static void Postfix(NBackButton __instance, ref string[] __result)
            {
                if (ReferenceEquals(__instance, _toApPageButton) || ReferenceEquals(__instance, _toVanillaButton))
                {
                    __result = System.Array.Empty<string>();
                }
            }
        }

        /// <summary>
        /// Shows whichever nav button matches the page currently in front (per
        /// ShopPageUtility.IsApPageFront) and hides the other. Keyed off IsApPageFront
        /// rather than which page's Open() just fired, since both pages call Open()
        /// independently and that turned into a race.
        /// </summary>
        private static void SyncNavButtonsToFrontPage()
        {
            if (!ShopPageUtility.HasPages)
            {
                return;
            }

            if (_navigationBlockDepth > 0)
            {
                DisableAndHideNavButton(_toApPageButton);
                DisableAndHideNavButton(_toVanillaButton);
                return;
            }

            NMerchantInventory? vanillaPage = ShopPageUtility.VanillaPageInstance;
            NMerchantInventory? apPage = ShopPageUtility.ApPageInstance;
            if (vanillaPage == null
                || apPage == null
                || !GodotObject.IsInstanceValid(vanillaPage)
                || !GodotObject.IsInstanceValid(apPage)
                || !vanillaPage.IsOpen
                || !apPage.IsOpen)
            {
                DisableAndHideNavButton(_toApPageButton);
                DisableAndHideNavButton(_toVanillaButton);
                return;
            }

            bool isApPageFront = ShopPageUtility.IsApPageFront;
            Control? buttonToEnable = isApPageFront ? _toVanillaButton : _toApPageButton;
            Control? buttonToDisable = isApPageFront ? _toApPageButton : _toVanillaButton;

            if (buttonToEnable != null && GodotObject.IsInstanceValid(buttonToEnable))
            {
                EnableNavButton(buttonToEnable);
                buttonToEnable.GetParent()?.MoveChild(buttonToEnable, -1);
            }

            DisableAndHideNavButton(buttonToDisable);
        }

        internal static void BeginNavigationBlock()
        {
            _navigationBlockDepth++;
            DisableAndHideNavButton(_toApPageButton);
            DisableAndHideNavButton(_toVanillaButton);
        }

        internal static void EndNavigationBlock()
        {
            if (_navigationBlockDepth > 0)
            {
                _navigationBlockDepth--;
            }

            if (_navigationBlockDepth > 0)
            {
                return;
            }

            NMerchantInventory? vanillaPage = ShopPageUtility.VanillaPageInstance;
            NMerchantInventory? apPage = ShopPageUtility.ApPageInstance;
            if (vanillaPage != null
                && apPage != null
                && GodotObject.IsInstanceValid(vanillaPage)
                && GodotObject.IsInstanceValid(apPage)
                && vanillaPage.IsOpen
                && !apPage.IsOpen)
            {
                apPage.Open();
            }

            SyncNavButtonsToFrontPage();
        }

        private static void EnableNavButton(Control button)
        {
            button.Visible = true;
            button.MouseFilter = Control.MouseFilterEnum.Stop;

            if (button is NBackButton realButton)
            {
                realButton.Enable();
            }
            else if (button is Button fallbackButton)
            {
                fallbackButton.Disabled = false;
            }
        }

        private static void DisableAndHideNavButton(Control? button)
        {
            if (button == null || !GodotObject.IsInstanceValid(button))
            {
                return;
            }

            if (button is NBackButton realButton)
            {
                realButton.Disable();
            }
            else if (button is Button fallbackButton)
            {
                fallbackButton.Disabled = true;
            }

            button.MouseFilter = Control.MouseFilterEnum.Ignore;
            button.Visible = false;
        }

        /// <summary>
        /// When the merchant inventory page opens, play the native slide-in animation for the corresponding button.
        /// </summary>
        [HarmonyPatch(typeof(NMerchantInventory), nameof(NMerchantInventory.Open))]
        public static class AnimateNavButtonsOnOpen
        {
            [HarmonyPostfix]
            public static void Postfix(NMerchantInventory __instance)
            {
                SyncNavButtonsToFrontPage();
            }
        }

        /// <summary>
        /// Treats the two inventory nodes as one screen. Closing either page closes its
        /// still-open peer, then restores the canonical vanilla-front positions.
        /// </summary>
        [HarmonyPatch(typeof(NMerchantInventory), "Close")]
        public static class CoordinatePageClose
        {
            [HarmonyPostfix]
            public static void Postfix(NMerchantInventory __instance)
            {
                if (_isCoordinatingClose || !TryGetPeerPage(__instance, out NMerchantInventory? peerPage))
                {
                    return;
                }

                _isCoordinatingClose = true;
                try
                {
                    if (peerPage != null && GodotObject.IsInstanceValid(peerPage) && peerPage.IsOpen)
                    {
                        peerPage.Close();
                    }
                }
                catch (System.Exception ex)
                {
                    LogUtility.Error($"ShopPages: failed to close the peer inventory page. {ex}");
                }
                finally
                {
                    DisableAndHideNavButton(_toApPageButton);
                    DisableAndHideNavButton(_toVanillaButton);
                    ShopPageUtility.ResetToVanillaPage();
                    _isCoordinatingClose = false;
                }
            }

            private static bool TryGetPeerPage(NMerchantInventory instance, out NMerchantInventory? peerPage)
            {
                if (ReferenceEquals(instance, ShopPageUtility.VanillaPageInstance))
                {
                    peerPage = ShopPageUtility.ApPageInstance;
                    return true;
                }

                if (ReferenceEquals(instance, ShopPageUtility.ApPageInstance))
                {
                    peerPage = ShopPageUtility.VanillaPageInstance;
                    return true;
                }

                peerPage = null;
                return false;
            }
        }

        #endregion

        #region Per-Slot Visibility Filter

        /// <summary>
        /// Merchant slots route both controller FocusEntered and hitbox MouseEntered
        /// through the same OnFocus method. With both shop pages open, focus restoration
        /// can deliver both signals before an unfocus, and the base method then tries to
        /// add the same owner to NHoverTipSet's active dictionary twice.
        /// </summary>
        [HarmonyPatch(typeof(NMerchantSlot), "OnFocus")]
        public static class SuppressDuplicateSlotFocus
        {
            [HarmonyPrefix]
            public static bool Prefix(NMerchantSlot __instance)
            {
                if (!ShopPageUtility.HasPages
                    || __instance._merchantRug is not NMerchantInventory rug
                    || (!ReferenceEquals(rug, ShopPageUtility.VanillaPageInstance)
                        && !ReferenceEquals(rug, ShopPageUtility.ApPageInstance)))
                {
                    return true;
                }

                return !__instance._isHovered;
            }
        }

        private static void ApplyPageFilter(NMerchantSlot slot)
        {
            if (!ShopPageUtility.HasPages)
            {
                return;
            }
            if (!slot.Entry.IsStocked)
            {
                return;
            }
            if (slot._merchantRug is not NMerchantInventory rug)
            {
                return;
            }

            bool onApPage = ReferenceEquals(rug, ShopPageUtility.ApPageInstance);
            bool isApSlot = Patches_ShopSanity.IsApSlot(slot.Entry);
            bool belongsOnThisPage = isApSlot == onApPage;

            slot.Visible = belongsOnThisPage;
            slot.MouseFilter = belongsOnThisPage ? Control.MouseFilterEnum.Stop : Control.MouseFilterEnum.Ignore;
        }

        [HarmonyPatch(typeof(NMerchantCard), "UpdateVisual")]
        public static class CardPageFilter
        {
            [HarmonyPostfix]
            public static void Postfix(NMerchantCard __instance) => ApplyPageFilter(__instance);
        }

        [HarmonyPatch(typeof(NMerchantRelic), "UpdateVisual")]
        public static class RelicPageFilter
        {
            [HarmonyPostfix]
            public static void Postfix(NMerchantRelic __instance) => ApplyPageFilter(__instance);
        }

        [HarmonyPatch(typeof(NMerchantPotion), "UpdateVisual")]
        public static class PotionPageFilter
        {
            [HarmonyPostfix]
            public static void Postfix(NMerchantPotion __instance) => ApplyPageFilter(__instance);
        }

        #endregion

        #region Duplicate-Reaction Guard

        [HarmonyPatch(typeof(NMerchantInventory), "OnPurchaseCompleted")]
        public static class SuppressInactivePageReaction
        {
            [HarmonyPrefix]
            public static bool Prefix(NMerchantInventory __instance)
            {
                if (!ShopPageUtility.HasPages)
                {
                    return true;
                }
                bool isApPageInstance = ReferenceEquals(__instance, ShopPageUtility.ApPageInstance);
                return isApPageInstance == ShopPageUtility.IsApPageFront;
            }
        }

        #endregion
    }
}
