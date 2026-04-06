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
    /// Displays a reconnecting status icon in the top bar to the left of the Archipelago button.
    /// Visible only when <see cref="ArchipelagoClient.State"/> is <see cref="ConnectionState.Reconnecting"/>.
    /// </summary>
    public static class ArchipelagoReconnectStatusUI
    {
        #region Node References

        private static TextureRect? _icon;

        #endregion

        #region Constants

        /// <summary>Placeholder URI for the reconnect status icon. Replace with the real asset path later.</summary>
        private const string IconPath = "res://images/ui/disconnected2.png";

        private const float TooltipOffsetY = 20f;

        #endregion

        #region Tooltip

        private static readonly HoverTip _hoverTip = new HoverTip(
            new LocString("static_hover_tips", "AP_RECONNECT.title"),
            new LocString("static_hover_tips", "AP_RECONNECT.description"));

        #endregion

        #region Public API

        /// <summary>
        /// Injects the reconnect status icon into the top bar, immediately before the Archipelago button.
        /// Called via the <see cref="ReconnectStatusInjectionPatch"/> Harmony postfix.
        /// </summary>
        public static void InjectIcon(NTopBar topBar)
        {
            try
            {
                // Find the Archipelago button to use as position anchor
                var apButton = FindChildByName(topBar, "ArchipelagoButton");
                if (apButton == null)
                {
                    LogUtility.Info("[AP Reconnect] ArchipelagoButton not found yet — skipping injection.");
                    return;
                }

                var container = apButton.GetParent() as Container;
                if (container == null)
                {
                    LogUtility.Error("[AP Reconnect] ArchipelagoButton parent is not a Container.");
                    return;
                }

                // Guard against duplicate injection
                if (_icon != null && GodotObject.IsInstanceValid(_icon) && _icon.GetParent() == container)
                    return;

                _icon = CreateIcon();
                container.AddChild(_icon);
                container.MoveChild(_icon, Math.Max(0, apButton.GetIndex()));

                // Start hidden; visibility is driven by connection state
                UpdateVisibility();

                // Listen for connection state changes to toggle visibility
                ArchipelagoClient.ConnectionStateChanged += OnConnectionStateChanged;

                LogUtility.Success("Archipelago Reconnect status icon injected successfully!");
            }
            catch (Exception ex)
            {
                LogUtility.Error($"Failed to inject Reconnect status icon: {ex.Message}");
            }
        }

        /// <summary>
        /// Updates the icon visibility based on the current connection state.
        /// </summary>
        public static void UpdateVisibility()
        {
            if (_icon == null || !GodotObject.IsInstanceValid(_icon)) return;
            _icon.Visible = ArchipelagoClient.State == ConnectionState.Reconnecting;
        }

        #endregion

        #region UI Construction

        private static TextureRect CreateIcon()
        {
            var rect = new TextureRect
            {
                Name = "ArchipelagoReconnectIcon",
                CustomMinimumSize = new Vector2(50, 50),
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                MouseFilter = Control.MouseFilterEnum.Stop // allow hover events
            };

            // Load the reconnect icon texture
            var tex = GD.Load<Texture2D>(IconPath);
            if (tex != null)
            {
                rect.Texture = tex;
            }
            else
            {
                LogUtility.Warn($"[AP Reconnect] Could not load icon: {IconPath}");
            }

            // Wire hover events for tooltip
            rect.MouseEntered += OnIconHovered;
            rect.MouseExited += OnIconUnhovered;

            return rect;
        }

        #endregion

        #region Event Handlers

        private static void OnConnectionStateChanged(ConnectionState state)
        {
            // Visibility must be updated on the main thread
            if (_icon != null && GodotObject.IsInstanceValid(_icon))
            {
                Callable.From(UpdateVisibility).CallDeferred();
            }
        }

        private static void OnIconHovered()
        {
            if (_icon == null) return;

            try
            {
                int attempt = ArchipelagoClient.ReconnectAttempt;
                var tipSet = NHoverTipSet.CreateAndShow(_icon, _hoverTip);
                tipSet.GlobalPosition = _icon.GlobalPosition + new Vector2(
                    _icon.Size.X - tipSet.Size.X,
                    _icon.Size.Y + TooltipOffsetY);
            }
            catch (Exception ex)
            {
                LogUtility.Error($"[AP Reconnect] Failed to show tooltip: {ex.Message}");
            }
        }

        private static void OnIconUnhovered()
        {
            if (_icon == null) return;

            try
            {
                NHoverTipSet.Remove(_icon);
            }
            catch (Exception ex)
            {
                LogUtility.Error($"[AP Reconnect] Failed to hide tooltip: {ex.Message}");
            }
        }

        #endregion

        #region Helpers

        private static Node? FindChildByName(Node parent, string name)
        {
            foreach (var child in parent.GetChildren())
            {
                if (child.Name == name) return child;
                var result = FindChildByName(child, name);
                if (result != null) return result;
            }
            return null;
        }

        #endregion
    }

    /// <summary>
    /// Harmony postfix on <see cref="NTopBar._Ready"/> that injects the reconnect status icon.
    /// Runs after <see cref="TopBarInjectionPatch"/> so the Archipelago button exists as an anchor.
    /// </summary>
    [HarmonyPatch(typeof(NTopBar), "_Ready")]
    public static class ReconnectStatusInjectionPatch
    {
        [HarmonyPostfix]
        public static void Postfix(NTopBar __instance)
        {
            // Defer so we run after all _Ready postfixes (including TopBarInjectionPatch)
            // have finished creating the Archipelago button we anchor to.
            Callable.From(() => ArchipelagoReconnectStatusUI.InjectIcon(__instance)).CallDeferred();
        }
    }
}
