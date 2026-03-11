using Godot;
using MegaCrit.Sts2.addons.mega_text;
using StS2AP.Utils;
using System;

namespace StS2AP.UI
{
    /// <summary>
    /// Static class that creates and manages the Archipelago notification UI.
    /// Displays notifications in the upper left corner with a speaker icon and speech bubble.
    /// </summary>
    public static class ArchipelagoNotificationUI
    {
        private static Control? _rootPanel;
        private static CanvasLayer? _canvasLayer;
        private static MegaRichTextLabel? _messageLabel;
        private static TextureRect? _speakerIcon;

        // UI Constants
        private const float IconSize = 64f;
        private const float BubblePadding = 12f;
        private const float CornerOffset = 20f;
        private const float TailWidth = 16f;

        /// <summary>
        /// Whether the UI is currently visible
        /// </summary>
        public static bool IsVisible => _rootPanel?.Visible ?? false;

        /// <summary>
        /// Injects the Archipelago notification UI into the current scene tree.
        /// Should be called when the user successfully connects to the Archipelago server.
        /// </summary>
        public static void InjectUI()
        {
            try
            {
                // Get the scene tree root
                var sceneTree = Engine.GetMainLoop() as SceneTree;
                if (sceneTree == null)
                {
                    LogUtility.Error("Failed to get SceneTree - cannot inject notification UI");
                    return;
                }

                var root = sceneTree.Root;
                if (root == null)
                {
                    LogUtility.Error("Failed to get root node - cannot inject notification UI");
                    return;
                }

                // Don't build the UI if it's already present
                if (_rootPanel != null && IsInstanceValid(_rootPanel))
                {
                    _rootPanel.Visible = true;
                    return;
                }

                // Create the UI
                _rootPanel = CreateUI();

                // Add to the root as a CanvasLayer so it renders on top
                _canvasLayer = new CanvasLayer();
                _canvasLayer.Name = "ArchipelagoNotificationLayer";
                _canvasLayer.Layer = 101; // Above the connection UI layer
                _canvasLayer.AddChild(_rootPanel);
                root.AddChild(_canvasLayer);

                LogUtility.Success("Archipelago notification UI injected successfully");
            }
            catch (Exception ex)
            {
                LogUtility.Error($"Failed to inject Archipelago notification UI: {ex.Message}");
            }
        }

        /// <summary>
        /// Removes the UI from the scene tree
        /// </summary>
        public static void RemoveUI()
        {
            if (_canvasLayer != null && IsInstanceValid(_canvasLayer))
            {
                _canvasLayer.QueueFree();
                _canvasLayer = null;
                _rootPanel = null;
                _messageLabel = null;
                _speakerIcon = null;
            }
        }

        /// <summary>
        /// Shows the notification UI
        /// </summary>
        public static void Show()
        {
            if (_rootPanel != null && IsInstanceValid(_rootPanel))
            {
                _rootPanel.Visible = true;
            }
        }

        /// <summary>
        /// Hides the notification UI
        /// </summary>
        public static void Hide()
        {
            if (_rootPanel != null && IsInstanceValid(_rootPanel))
            {
                _rootPanel.Visible = false;
            }
        }

        /// <summary>
        /// Sets the notification message text
        /// </summary>
        public static void SetMessage(string message)
        {
            if (_messageLabel != null && IsInstanceValid(_messageLabel))
            {
                _messageLabel.Text = message;
            }
        }

        private static bool IsInstanceValid(GodotObject obj)
        {
            return GodotObject.IsInstanceValid(obj);
        }

        private static Control CreateUI()
        {
            // Root container - positioned in upper left
            var root = new Control();
            root.Name = "ArchipelagoNotificationUI";
            root.SetAnchorsPreset(Control.LayoutPreset.TopLeft);
            root.MouseFilter = Control.MouseFilterEnum.Ignore;

            // Main container for the notification (positioned with offset from top-left)
            var notificationContainer = new HBoxContainer();
            notificationContainer.Name = "NotificationContainer";
            notificationContainer.Position = new Vector2(CornerOffset, CornerOffset);
            notificationContainer.AddThemeConstantOverride("separation", 0); // No gap, tail connects them
            root.AddChild(notificationContainer);

            // Speaker icon container (left side)
            var iconContainer = CreateSpeakerIcon();
            notificationContainer.AddChild(iconContainer);

            // Speech bubble with tail (right side)
            var speechBubble = CreateSpeechBubble();
            notificationContainer.AddChild(speechBubble);

            return root;
        }

        /// <summary>
        /// Creates the speaker icon (Neow) on the left side
        /// </summary>
        private static Control CreateSpeakerIcon()
        {
            var container = new Control();
            container.Name = "SpeakerIconContainer";
            container.CustomMinimumSize = new Vector2(IconSize, IconSize);

            // The actual speaker icon (Neow)
            _speakerIcon = new TextureRect();
            _speakerIcon.Name = "SpeakerIcon";
            _speakerIcon.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            _speakerIcon.StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered;
            _speakerIcon.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
            
            // Load the Neow icon
            try
            {
                _speakerIcon.Texture = GD.Load<Texture2D>("res://images/ui/run_history/neow.png");
            }
            catch (Exception ex)
            {
                LogUtility.Warn($"Could not load Neow icon: {ex.Message}");
            }

            container.AddChild(_speakerIcon);

            return container;
        }

        /// <summary>
        /// Creates the speech bubble with tail pointing to the speaker
        /// </summary>
        private static Control CreateSpeechBubble()
        {
            // Container that holds both the tail and the bubble
            var bubbleContainer = new HBoxContainer();
            bubbleContainer.Name = "SpeechBubbleContainer";
            bubbleContainer.AddThemeConstantOverride("separation", 0);

            // Dialogue tail (pointing left toward the speaker)
            var tail = CreateDialogueTail();
            bubbleContainer.AddChild(tail);

            // Main bubble panel
            var bubble = new PanelContainer();
            bubble.Name = "Bubble";
            bubble.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;

            // Get viewport width to calculate 25% width
            var viewport = Engine.GetMainLoop() as SceneTree;
            float maxBubbleWidth = 400f; // Default fallback
            if (viewport?.Root != null)
            {
                maxBubbleWidth = viewport.Root.GetViewport().GetVisibleRect().Size.X * 0.25f;
            }

            bubble.CustomMinimumSize = new Vector2(100, IconSize); // Minimum size, height matches icon

            // Style the bubble like NAncientDialogueLine
            var bubbleStyle = new StyleBoxFlat();
            bubbleStyle.BgColor = new Color(0.18f, 0.15f, 0.25f, 0.95f); // Dark purple background
            bubbleStyle.BorderColor = new Color(0.6f, 0.5f, 0.8f, 1f); // Purple border
            bubbleStyle.SetBorderWidthAll(2);
            bubbleStyle.SetCornerRadiusAll(8);
            bubbleStyle.ContentMarginLeft = BubblePadding;
            bubbleStyle.ContentMarginRight = BubblePadding;
            bubbleStyle.ContentMarginTop = BubblePadding;
            bubbleStyle.ContentMarginBottom = BubblePadding;
            bubble.AddThemeStyleboxOverride("panel", bubbleStyle);

            // Container for centering the text vertically
            var textContainer = new CenterContainer();
            textContainer.Name = "TextContainer";
            textContainer.SizeFlagsHorizontal = Control.SizeFlags.Fill;
            textContainer.SizeFlagsVertical = Control.SizeFlags.Fill;
            bubble.AddChild(textContainer);

            // Message label using MegaRichTextLabel
            _messageLabel = new MegaRichTextLabel();
            _messageLabel.Name = "NotificationLabel";
            _messageLabel.CustomMinimumSize = new Vector2(maxBubbleWidth - (BubblePadding * 2) - TailWidth, 0);
            _messageLabel.SizeFlagsHorizontal = Control.SizeFlags.Fill;
            _messageLabel.FitContent = true; // Allow height to grow with content
            _messageLabel.AutowrapMode = TextServer.AutowrapMode.Word; // Word wrap for long text
            
            // Set the font override for MegaRichTextLabel (required to avoid Godot engine bug)
            try
            {
                var font = GD.Load<Font>("res://fonts/kreon_regular.ttf");
                if (font != null)
                {
                    _messageLabel.AddThemeFontOverride("font", font);
                    _messageLabel.AddThemeFontSizeOverride("font_size", 16);
                }
                else
                {
                    LogUtility.Warn("Could not load font res://fonts/kreon_regular.ttf");
                }
            }
            catch (Exception ex)
            {
                LogUtility.Warn($"Failed to load notification label font: {ex.Message}");
            }

            _messageLabel.Text = "[sine]Test notification message![/sine]"; // Default test text
            textContainer.AddChild(_messageLabel);

            bubbleContainer.AddChild(bubble);

            return bubbleContainer;
        }

        /// <summary>
        /// Creates the dialogue tail that points toward the speaker icon
        /// </summary>
        private static Control CreateDialogueTail()
        {
            // Use a custom drawing control for the tail triangle
            var tailContainer = new Control();
            tailContainer.Name = "DialogueTailLeft";
            tailContainer.CustomMinimumSize = new Vector2(TailWidth, IconSize);

            // We'll use a ColorRect with a custom shape via a Polygon2D
            var tail = new Polygon2D();
            tail.Name = "TailPolygon";
            
            // Triangle pointing left
            // Points: top-right, bottom-right, middle-left (pointing to speaker)
            float midY = IconSize / 2;
            tail.Polygon = new Vector2[]
            {
                new Vector2(TailWidth, midY - 10), // Top right
                new Vector2(TailWidth, midY + 10), // Bottom right  
                new Vector2(0, midY)               // Point (left, center)
            };
            tail.Color = new Color(0.18f, 0.15f, 0.25f, 0.95f); // Match bubble background
            tailContainer.AddChild(tail);

            // Add a border line for the tail
            var tailBorder = new Line2D();
            tailBorder.Name = "TailBorder";
            tailBorder.Points = new Vector2[]
            {
                new Vector2(TailWidth, midY - 10),
                new Vector2(0, midY),
                new Vector2(TailWidth, midY + 10)
            };
            tailBorder.Width = 2;
            tailBorder.DefaultColor = new Color(0.6f, 0.5f, 0.8f, 1f); // Match bubble border
            tailContainer.AddChild(tailBorder);

            return tailContainer;
        }
    }
}
