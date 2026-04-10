using Godot;
using MegaCrit.Sts2.addons.mega_text;
using StS2AP.Utils;
using System;

namespace StS2AP.UI.Components
{
    /// <summary>
    /// A reusable UI component that displays a small icon on the left and a
    /// count/label string on the right, e.g. "(10 / 700)" or "42".
    ///
    /// Layout:
    /// <code>
    ///   HBoxContainer
    ///     ├─ TextureRect  ← icon loaded from a res:// path
    ///     └─ MegaRichTextLabel  ← BBCode count text
    /// </code>
    ///
    /// Typical usage — build a vertical list in a parent VBoxContainer:
    /// <code>
    ///   var row = new ItemCountLabel("res://images/ui/gold.png", "[gold]50[/gold]");
    ///   vbox.AddChild(row.Root);
    /// </code>
    /// </summary>
    public class ItemCountLabel
    {
        #region Node References

        /// <summary>
        /// The root <see cref="HBoxContainer"/> node. Add this to your parent container.
        /// </summary>
        public HBoxContainer Root { get; }

        /// <summary>The icon displayed on the left side of the row.</summary>
        private readonly TextureRect _icon;

        /// <summary>The text label displayed on the right side of the row.</summary>
        private readonly MegaRichTextLabel _label;

        #endregion

        #region Constants

        // Square size of the icon — kept small so rows stack tightly in a list
        private const float IconSize = 30f;

        // Gap between the icon and the text
        private const int IconTextSpacing = 6;

        // Font used for the label text, consistent with the rest of the mod UI
        private const string FontPath = "res://fonts/kreon_regular.ttf";

        // Default font size — callers can override via SetFontSize()
        private const int DefaultFontSize = 20;

        #endregion

        #region Constructor

        /// <summary>
        /// Creates a new <see cref="ItemCountLabel"/> row.
        /// </summary>
        /// <param name="iconPath">
        ///   A <c>res://</c> path to the icon texture, e.g.
        ///   <c>"res://images/ui/items/gold.png"</c>.
        ///   If the texture cannot be loaded a warning is logged and the icon slot is left empty.
        /// </param>
        /// <param name="text">
        ///   Initial BBCode text for the count label, e.g. <c>"(10 / 700)"</c> or
        ///   <c>"[gold]50 Gold[/gold]"</c>.  May be changed later via <see cref="SetText"/>.
        /// </param>
        /// <param name="fontSize">
        ///   Optional font size override.  Defaults to <see cref="DefaultFontSize"/>.
        /// </param>
        public ItemCountLabel(string iconPath, string text, int fontSize = DefaultFontSize)
        {
            // ── Root row container ────────────────────────────────────────────────────
            Root = new HBoxContainer();
            Root.Name = "ItemCountLabel";
            Root.AddThemeConstantOverride("separation", IconTextSpacing);
            // Pass mouse events through — this is a display-only component
            Root.MouseFilter = Control.MouseFilterEnum.Ignore;

            // ── Icon ─────────────────────────────────────────────────────────────────
            _icon = new TextureRect();
            _icon.Name = "ItemIcon";
            _icon.CustomMinimumSize = new Vector2(IconSize, IconSize);
            _icon.StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered;
            _icon.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
            // Align the icon vertically in the centre of the row
            _icon.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
            _icon.MouseFilter = Control.MouseFilterEnum.Ignore;

            try
            {
                var texture = GD.Load<Texture2D>(iconPath);
                if (texture != null)
                {
                    _icon.Texture = texture;
                }
                else
                {
                    LogUtility.Warn($"[ItemCountLabel] Could not load icon texture: {iconPath}");
                }
            }
            catch (Exception ex)
            {
                LogUtility.Warn($"[ItemCountLabel] Failed to load icon texture '{iconPath}': {ex.Message}");
            }

            Root.AddChild(_icon);

            // ── Label ─────────────────────────────────────────────────────────────────
            // FitContent = true so the row height is driven by the text rather than
            // collapsing to zero (same fix applied in ArchipelagoCharTrackerUI).
            _label = new MegaRichTextLabel();
            _label.Name = "ItemCountText";
            _label.FitContent = true;
            _label.BbcodeEnabled = true; // Required for BBCode and game text effects
            _label.AutowrapMode = TextServer.AutowrapMode.Off; // Single-line counts don't need wrapping
            _label.SizeFlagsHorizontal = Control.SizeFlags.Fill;
            _label.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
            _label.MouseFilter = Control.MouseFilterEnum.Ignore;

            // Apply the game font, consistent with ArchipelagoNotificationUI and ArchipelagoCharTrackerUI
            try
            {
                var font = GD.Load<Font>(FontPath);
                if (font != null)
                {
                    _label.AddThemeFontOverride("normal_font", font);
                }
                else
                {
                    LogUtility.Warn($"[ItemCountLabel] Could not load font: {FontPath}");
                }
            }
            catch (Exception ex)
            {
                LogUtility.Warn($"[ItemCountLabel] Failed to load label font: {ex.Message}");
            }

            _label.AddThemeFontSizeOverride("normal_font_size", fontSize);
            _label.Text = text;

            Root.AddChild(_label);
        }

        #endregion

        #region Public API

        /// <summary>
        /// Replaces the count text displayed next to the icon.
        /// Supports full BBCode (e.g. <c>"[gold](10 / 700)[/gold]"</c>).
        /// </summary>
        /// <param name="bbcodeText">New BBCode-formatted string to display.</param>
        public void SetText(string bbcodeText)
        {
            if (GodotObject.IsInstanceValid(_label))
            {
                // Re-apply font size — the game can reset theme overrides unexpectedly
                _label.RemoveThemeFontSizeOverride("normal_font_size");
                _label.AddThemeFontSizeOverride("normal_font_size", DefaultFontSize);

                _label.Text = bbcodeText;
            }
        }

        /// <summary>
        /// Swaps the icon texture at runtime, e.g. when the represented item type changes.
        /// </summary>
        /// <param name="iconPath">New <c>res://</c> path to the replacement texture.</param>
        public void SetIcon(string iconPath)
        {
            if (!GodotObject.IsInstanceValid(_icon)) return;

            try
            {
                var texture = GD.Load<Texture2D>(iconPath);
                if (texture != null)
                {
                    _icon.Texture = texture;
                }
                else
                {
                    LogUtility.Warn($"[ItemCountLabel] Could not load replacement icon: {iconPath}");
                }
            }
            catch (Exception ex)
            {
                LogUtility.Warn($"[ItemCountLabel] Failed to swap icon '{iconPath}': {ex.Message}");
            }
        }

        /// <summary>
        /// Overrides the font size on the label.
        /// Useful when embedding this component at different visual scales.
        /// </summary>
        /// <param name="size">Font size in points.</param>
        public void SetFontSize(int size)
        {
            if (GodotObject.IsInstanceValid(_label))
            {
                _label.RemoveThemeFontSizeOverride("normal_font_size");
                _label.AddThemeFontSizeOverride("normal_font_size", size);
            }
        }

        #endregion
    }
}
