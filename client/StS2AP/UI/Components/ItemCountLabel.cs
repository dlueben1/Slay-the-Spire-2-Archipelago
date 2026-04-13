using Godot;
using MegaCrit.Sts2.addons.mega_text;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Nodes.HoverTips;
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

        /// <summary>
        /// The hover tooltip shown when the player mouses over this row.
        /// Null if no tooltip was provided — in that case mouse events pass through.
        /// </summary>
        private readonly HoverTip? _hoverTip;

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
        /// <param name="tooltipTitle">
        ///   Optional tooltip title shown on hover. Pass <c>null</c> to disable the tooltip entirely.
        /// </param>
        /// <param name="tooltipDescription">
        ///   Optional tooltip description shown on hover. Pass <c>null</c> to disable the tooltip entirely.
        /// </param>
        public ItemCountLabel(string iconPath, string text, int fontSize = DefaultFontSize, string? tooltipTitle = null, string? tooltipDescription = null)
        {
            // ── Root row container ────────────────────────────────────────────────────
            Root = new HBoxContainer();
            Root.Name = "ItemCountLabel";
            Root.AddThemeConstantOverride("separation", IconTextSpacing);

            // If a tooltip is provided we need to receive mouse events; otherwise pass through.
            if (tooltipTitle != null && tooltipDescription != null)
            {
                Root.MouseFilter = Control.MouseFilterEnum.Stop;

                // Register the plain strings into a runtime loc table so HoverTip can look them up.
                // The key is made unique per instance so multiple rows don't collide.
                string tableKey = $"item_count_label_{tooltipTitle.GetHashCode():x}";
                TextUtility.RegisterLocTableAtRuntime(tableKey, new System.Collections.Generic.Dictionary<string, string>
                {
                    { "title",       tooltipTitle       },
                    { "description", tooltipDescription }
                });

                _hoverTip = new HoverTip(
                    new LocString(tableKey, "title"),
                    new LocString(tableKey, "description"));

                // Show the tooltip when the mouse enters the row.
                // Position it at the top center of the screen in a fixed position.
                Root.MouseEntered += () =>
                {
                    try
                    {
                        var tipSet = NHoverTipSet.CreateAndShow(Root, _hoverTip);
                        
                        // Get the viewport size to calculate screen center
                        var viewportSize = Root.GetViewportRect().Size;
                        
                        // Position at top center of screen with a small margin from the top
                        const float topMargin = 50f;
                        const float offsetFromPanel = 140f;
                        tipSet.GlobalPosition = new Vector2(
                            offsetFromPanel + ((viewportSize.X - tipSet.Size.X) / 2f),  // Centered horizontally, then shifted from the progress panel
                            topMargin);                              // Fixed distance from top
                    }
                    catch (Exception ex)
                    {
                        LogUtility.Warn($"[ItemCountLabel] Failed to show tooltip: {ex.Message}");
                    }
                };

                // Remove the tooltip when the mouse leaves the row
                Root.MouseExited += () =>
                {
                    try
                    {
                        NHoverTipSet.Remove(Root);
                    }
                    catch (Exception ex)
                    {
                        LogUtility.Warn($"[ItemCountLabel] Failed to hide tooltip: {ex.Message}");
                    }
                };
            }
            else
            {
                // Pass mouse events through — this is a display-only component with no tooltip
                Root.MouseFilter = Control.MouseFilterEnum.Ignore;
            }

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
