using Godot;
using MegaCrit.Sts2.addons.mega_text;
using MegaCrit.Sts2.Core.Nodes.Debug;
using StS2AP.Utils;
using System;
using System.Threading;

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
        private static System.Threading.Timer? _displayTimer;
        private static Tween? _fadeTween;

        // UI Constants
        private const float IconSize = 64f;
        private const float BubblePadding = 12f;
        private const float LeftOffset = 16f;
        private const float TopOffset = 154f;
        private const int FontSize = 20;
        private const float TailWidth = 16f;

        /// <summary>
        /// Whether the UI is currently visible
        /// </summary>
        public static bool IsVisible => _rootPanel?.Visible ?? false;

        #region UI Injection

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
            if (_fadeTween != null)
            {
                _fadeTween.Kill();
                _fadeTween = null;
            }

            if (_displayTimer != null)
            {
                _displayTimer.Dispose();
                _displayTimer = null;
            }

            if (_canvasLayer != null && IsInstanceValid(_canvasLayer))
            {
                _canvasLayer.QueueFree();
                _canvasLayer = null;
                _rootPanel = null;
                _messageLabel = null;
                _speakerIcon = null;
            }
        }

        #endregion

        /// <summary>
        /// Shows the notification UI by dequeuing the next message and displaying it with a fade-in animation
        /// </summary>
        public static void ShowMessage()
        {
            if (_rootPanel == null || !IsInstanceValid(_rootPanel))
                return;

            // Dequeue and display the next notification
            var notification = NotificationUtility.DequeueNotification();
            if (notification == null) return;

            // Set the message text
            SetMessage(notification.Message);
            NotificationUtility.WriteToDevConsole(notification.Message);

            // Cancel any existing fade tween
            if (_fadeTween != null)
            {
                _fadeTween.Kill();
            }

            // Fade in
            _rootPanel.Modulate = new Color(1, 1, 1, 0); // Start transparent
            _rootPanel.Visible = true;
            _fadeTween = _rootPanel.CreateTween();
            _fadeTween.TweenProperty(_rootPanel, "modulate", new Color(1, 1, 1, 1), 0.3);

            ResetTimer(notification.DisplayDuration);

        }

        public static void ResetTimer(double timeout)
        {
            // Dispose of previous timer if it exists
            _displayTimer?.Dispose();

            // Create a one-time timer to display the message for the specified duration
            _displayTimer = new System.Threading.Timer(
                OnDisplayTimerTimeout,
                null,
                TimeSpan.FromSeconds(timeout),
                Timeout.InfiniteTimeSpan);
        }

        /// <summary>
        /// Called when the display timer times out
        /// </summary>
        private static void OnDisplayTimerTimeout(object? state)
        {
            // Check if there are more notifications to display
            if (NotificationUtility.GetQueueCount() > 0)
            {
                if(NotificationUtility.DevConsoleVisible())
                {
                    // Don't want to render messages while the dev console is open
                    ResetTimer(3.0);
                    return;
                }
                // Show the next notification
                Callable.From(ShowMessage).CallDeferred(); // FIX WILL DO A BETTER COMMENT LATER
            }
            else
            {
                // No more notifications, hide the UI
                Callable.From(Hide).CallDeferred(); // FIX WILL DO A BETTER COMMENT LATER
            }
        }

        /// <summary>
        /// Hides the notification UI with a fade-out animation
        /// </summary>
        public static void Hide()
        {
            if (_rootPanel == null || !IsInstanceValid(_rootPanel))
                return;

            // Stop the display timer if it exists
            _displayTimer?.Dispose();
            _displayTimer = null;

            // Cancel any existing fade tween
            if (_fadeTween != null)
            {
                _fadeTween.Kill();
            }

            // Fade out
            _fadeTween = _rootPanel.CreateTween();
            _fadeTween.TweenProperty(_rootPanel, "modulate", new Color(1, 1, 1, 0), 0.3);
            _fadeTween.TweenCallback(Callable.From(() =>
            {
                if (_rootPanel != null && IsInstanceValid(_rootPanel))
                {
                    _rootPanel.Visible = false;
                }
            }));
        }

        /// <summary>
        /// Sets the notification message text
        /// </summary>
        public static void SetMessage(string message)
        {
            if (_messageLabel != null && IsInstanceValid(_messageLabel))
            {
                // Odd bug-fix: We need to set the font manually EVERY time or it will randomly change
                _messageLabel.RemoveThemeFontSizeOverride("normal_font_size");
                _messageLabel.AddThemeFontSizeOverride("normal_font_size", FontSize);

                // Set the message text
                _messageLabel.Text = message;
            }
        }

        /// <summary>
        /// Checks if a GodotObject instance is valid (not null and not freed)
        /// </summary>
        /// <param name="obj">The GodotObject instance to check</param>
        /// <returns>True if the instance is valid, false otherwise</returns>
        private static bool IsInstanceValid(GodotObject obj)
        {
            return GodotObject.IsInstanceValid(obj);
        }

        /// <summary>
        /// Builds the UI from scratch, since we don't have the Godot editor
        /// </summary>
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
            notificationContainer.Position = new Vector2(LeftOffset, TopOffset);
            notificationContainer.AddThemeConstantOverride("separation", 0); // No gap, tail connects them
            root.AddChild(notificationContainer);

            // Speaker icon container (left side)
            var iconContainer = CreateSpeakerIcon();
            notificationContainer.AddChild(iconContainer);

            // Speech bubble with tail (right side)
            var speechBubble = CreateSpeechBubble();
            notificationContainer.AddChild(speechBubble);

            // Start hidden until we have a message to show
            root.Visible = false;

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
            bubbleContainer.Name = "ArchipelagoSpeechBubbleContainer";
            bubbleContainer.AddThemeConstantOverride("separation", 0);

            // Dialogue tail (pointing left toward the speaker)
            var tail = CreateDialogueTail();
            bubbleContainer.AddChild(tail);

            // Main bubble panel
            var bubble = new PanelContainer();
            bubble.Name = "ArchipelagoBubble";
            bubble.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;

            // Get viewport width to calculate 25% width
            var viewport = Engine.GetMainLoop() as SceneTree;
            float maxBubbleWidth = 400f; // Default fallback
            if (viewport?.Root != null)
            {
                maxBubbleWidth = viewport.Root.GetViewport().GetVisibleRect().Size.X * 0.25f;
            }

            bubble.CustomMinimumSize = new Vector2(100, IconSize);

            // Style the bubble like NAncientDialogueLine
            var bubbleStyle = new StyleBoxFlat();
            bubbleStyle.BgColor = new Color(0.18f, 0.15f, 0.25f, 0.95f);
            bubbleStyle.SetBorderWidthAll(0);
            bubbleStyle.SetCornerRadiusAll(8);
            bubbleStyle.ContentMarginLeft = BubblePadding;
            bubbleStyle.ContentMarginRight = BubblePadding;
            bubbleStyle.ContentMarginTop = BubblePadding;
            bubbleStyle.ContentMarginBottom = BubblePadding;
            bubble.AddThemeStyleboxOverride("panel", bubbleStyle);

            // Container for centering the text vertically
            var textContainer = new CenterContainer();
            textContainer.Name = "ArchipelagoTextContainer";
            textContainer.SizeFlagsHorizontal = Control.SizeFlags.Fill;
            textContainer.SizeFlagsVertical = Control.SizeFlags.Fill;
            bubble.AddChild(textContainer);

            // Message label using MegaRichTextLabel (the in-game rich text label with effects support)
            _messageLabel = new MegaRichTextLabel();
            _messageLabel.Name = "ArchipelagoNotificationLabel";
            _messageLabel.CustomMinimumSize = new Vector2(maxBubbleWidth - (BubblePadding * 2) - TailWidth, 0);
            _messageLabel.SizeFlagsHorizontal = Control.SizeFlags.Fill;
            _messageLabel.FitContent = true; // Allows height to grow with content
            _messageLabel.AutowrapMode = TextServer.AutowrapMode.Word; // Word wrap for long text
            _messageLabel.BbcodeEnabled = true; // BBCode must be enabled for MegaRichTextLabel effects (e.g. [sine]) to work

            /// MegaRichTextLabel._Ready() calls AssertThemeFontOverride with ThemeConstants.RichTextLabel.normalFont,
            /// which is the "normal_font" theme property on RichTextLabel.
            /// 
            /// Please note: The terminal still complains that we didn't set a "Theme Font", but there won't be a problem
            /// since we apply it right away.
            try
            {
                var font = GD.Load<Font>("res://fonts/kreon_regular.ttf");
                if (font != null)
                {
                    _messageLabel.AddThemeFontOverride("normal_font", font);
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

            // Attach everything together
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

            return tailContainer;
        }
    }
}
