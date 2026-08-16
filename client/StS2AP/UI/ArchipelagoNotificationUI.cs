using System;
using System.Reflection;
using System.Threading;
using Godot;
using MegaCrit.Sts2.addons.mega_text;
using MegaCrit.Sts2.Core.Nodes.Debug;
using StS2AP.Models;
using StS2AP.Utils;
using STS2RitsuLib;
using static StS2AP.Utils.NotificationUtility;

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
        private static HBoxContainer? _notificationContainer;
        private static PanelContainer? _bubblePanel;
        private static MegaRichTextLabel? _messageLabel;
        private static TextureRect? _speakerIcon;
        private static System.Threading.Timer? _displayTimer;
        private static Tween? _fadeTween;
        private static SceneTree? _sceneTree;

        // UI Constants
        private const float IconSize = 64f;
        private const float BubblePadding = 12f;
        private const float LeftOffset = 16f;
        private const float TopOffset = 154f;
        private const int FontSize = 24;
        private const float TailWidth = 16f;
        private const float BubbleWidth = 480f;

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
                    StartProcessing(sceneTree);
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

                // This UI is attached to the scene-tree root and outlives NRun, so it is
                // also the appropriate owner for processing notifications in every scene.
                StartProcessing(sceneTree);

                // Load the desired ancient as the announcer icon
                UpdateSpeakerIcon();

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
            StopProcessing();

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
                _notificationContainer = null;
                _bubblePanel = null;
                _messageLabel = null;
                _speakerIcon = null;
            }
        }

        #endregion

        private static void StartProcessing(SceneTree sceneTree)
        {
            if (_sceneTree == sceneTree)
            {
                return;
            }

            StopProcessing();
            _sceneTree = sceneTree;
            _sceneTree.ProcessFrame += CheckAndHandleNotification;
        }

        private static void StopProcessing()
        {
            if (_sceneTree == null)
            {
                return;
            }

            _sceneTree.ProcessFrame -= CheckAndHandleNotification;
            _sceneTree = null;
        }

        #region Speaker Icon Management

        /// <summary>
        /// Updates the speaker icon based on the current announcer setting.
        /// Can be called at any time - will handle cases where UI is not yet injected.
        /// </summary>
        public static void UpdateSpeakerIcon()
        {
            LogUtility.Info($"AP: Updating speaker icon based on current announcer setting");

            // If the UI hasn't been injected yet, there's nothing to update
            if (_speakerIcon == null || !IsInstanceValid(_speakerIcon))
            {
                LogUtility.Error($"AP: FAILED TO RUN!");
                return;
            }

            try
            {
                // Get the current announcer setting
                var store = RitsuLibFramework.GetDataStore(ModEntry.ModId);
                var settings = store.Get<ClientSettings>("apsettings");
                var announcer = settings.Announcer?.ToLower() ?? "neow";

                LogUtility.Info($"ANNOUNCER: {announcer}");

                // Load the appropriate icon texture
                var iconPath = $"res://images/ui/run_history/{announcer}.png";
                _speakerIcon.Texture = GD.Load<Texture2D>(iconPath);

                LogUtility.Info($"AP: Updated notification speaker icon to: {announcer}");
            }
            catch (Exception ex)
            {
                LogUtility.Warn(
                    $"Failed to update speaker icon: {ex.Message}. Falling back to Neow."
                );

                // Fallback to Neow if something goes wrong
                try
                {
                    _speakerIcon.Texture = GD.Load<Texture2D>(
                        "res://images/ui/run_history/neow.png"
                    );
                }
                catch (Exception fallbackEx)
                {
                    LogUtility.Error($"Failed to load fallback Neow icon: {fallbackEx.Message}");
                }
            }
            LogUtility.Info($"AP: Finished updating speaker icon");
        }

        #endregion

        /// <summary>
        /// Shows the notification UI by dequeuing the next message and displaying it with a fade-in animation
        /// </summary>
        public static void ShowMessage(ArchipelagoNotification notification)
        {
            // Set the message text
            SetMessage(notification.Message);

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
                Timeout.InfiniteTimeSpan
            );
        }

        /// <summary>
        /// Called when the display timer times out
        /// </summary>
        private static void OnDisplayTimerTimeout(object? state)
        {
            Callable.From(Hide).CallDeferred(); // FIX WILL DO A BETTER COMMENT LATER
        }

        /// <summary>
        /// Checks for notifications to process from the queues.
        /// </summary>
        public static void CheckAndHandleNotification()
        {
            if (_rootPanel == null || !IsInstanceValid(_rootPanel))
                return;
            if (!IsVisible)
            {
                var notif = NotificationUtility.DequeueNotification();
                if (notif != null)
                {
                    ShowMessage(notif);
                }
            }

            var devNotification = NotificationUtility.DequeueDevNotification();
            if (devNotification != null)
            {
                WriteToDevConsole(devNotification.Message);
            }
        }

        /// <summary>
        /// Writes to the dev console from the main-thread notification processor.
        /// </summary>
        /// <param name="msg"></param>
        private static void WriteToDevConsole(string msg)
        {
            RichTextLabel? outputBuffer = GetDevConsoleBuffer();
            if (outputBuffer != null)
            {
                outputBuffer.Text = outputBuffer.Text + msg + "\n";
            }
        }

        /// <summary>
        /// Obtains the output buffer in the dev console using reflection, if the dev console exists.
        /// </summary>
        /// <returns></returns>
        private static RichTextLabel? GetDevConsoleBuffer()
        {
            try
            {
                var console = NDevConsole.Instance;
                if (console == null)
                {
                    return null;
                }

                var outputBufferInfo = console
                    .GetType()
                    .GetField("_outputBuffer", BindingFlags.Instance | BindingFlags.NonPublic);
                if (outputBufferInfo == null)
                {
                    return null;
                }
                return (RichTextLabel?)outputBufferInfo.GetValue(console);
            }
            catch (Exception ex)
            {
                // Can throw if the dev console isn't instantiated.
                LogUtility.Debug("No dev console" + ex.Message);
            }
            return null;
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
            _fadeTween.TweenCallback(
                Callable.From(() =>
                {
                    if (_rootPanel != null && IsInstanceValid(_rootPanel))
                    {
                        _rootPanel.Visible = false;
                    }
                })
            );
        }

        /// <summary>
        /// Sets the notification message text
        /// </summary>
        public static void SetMessage(string message)
        {
            if (_messageLabel == null || !IsInstanceValid(_messageLabel))
            {
                return;
            }

            _messageLabel.Text = message;
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
            // Keep the root pinned to the known-good top-left layout. The child
            // container owns the notification's fixed screen-space offset.
            var root = new Control();
            root.Name = "ArchipelagoNotificationUI";
            root.SetAnchorsPreset(Control.LayoutPreset.TopLeft);
            root.MouseFilter = Control.MouseFilterEnum.Ignore;

            // Main container for the notification (positioned with offset from top-left)
            _notificationContainer = new HBoxContainer();
            _notificationContainer.Name = "NotificationContainer";
            _notificationContainer.SetAnchorsPreset(Control.LayoutPreset.TopLeft);
            _notificationContainer.Position = new Vector2(LeftOffset, TopOffset);
            _notificationContainer.MouseFilter = Control.MouseFilterEnum.Ignore;
            _notificationContainer.AddThemeConstantOverride("separation", 0); // No gap, tail connects them
            root.AddChild(_notificationContainer);

            // Speaker icon container (left side)
            var iconContainer = CreateSpeakerIcon();
            _notificationContainer.AddChild(iconContainer);

            // Speech bubble with tail (right side)
            var speechBubble = CreateSpeechBubble();
            _notificationContainer.AddChild(speechBubble);

            // Start hidden until we have a message to show
            root.Visible = false;

            return root;
        }

        /// <summary>
        /// Creates the speaker icon on the left side of the notification.
        /// It will be set to an Ancient of the user's preference when the UI is injected or
        /// the user updates their settings.
        /// </summary>
        private static Control CreateSpeakerIcon()
        {
            // Build the Control
            var container = new Control();
            container.Name = "SpeakerIconContainer";
            container.CustomMinimumSize = new Vector2(IconSize, IconSize);
            container.SizeFlagsVertical = Control.SizeFlags.ShrinkBegin;
            container.MouseFilter = Control.MouseFilterEnum.Ignore;

            // Configure the Control
            _speakerIcon = new TextureRect();
            _speakerIcon.Name = "SpeakerIcon";
            _speakerIcon.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            _speakerIcon.StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered;
            _speakerIcon.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
            _speakerIcon.MouseFilter = Control.MouseFilterEnum.Ignore;
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
            bubbleContainer.SizeFlagsVertical = Control.SizeFlags.ShrinkBegin;
            bubbleContainer.MouseFilter = Control.MouseFilterEnum.Ignore;

            // Dialogue tail (pointing left toward the speaker)
            var tail = CreateDialogueTail();
            bubbleContainer.AddChild(tail);

            // Main bubble panel
            _bubblePanel = new PanelContainer();
            _bubblePanel.Name = "ArchipelagoBubble";
            _bubblePanel.SizeFlagsVertical = Control.SizeFlags.ShrinkBegin;
            _bubblePanel.CustomMinimumSize = new Vector2(BubbleWidth, IconSize);
            _bubblePanel.MouseFilter = Control.MouseFilterEnum.Ignore;

            // Style the bubble like NAncientDialogueLine
            var bubbleStyle = new StyleBoxFlat();
            bubbleStyle.BgColor = new Color(0.18f, 0.15f, 0.25f, 0.95f);
            bubbleStyle.SetBorderWidthAll(0);
            bubbleStyle.SetCornerRadiusAll(8);
            bubbleStyle.ContentMarginLeft = BubblePadding;
            bubbleStyle.ContentMarginRight = BubblePadding;
            bubbleStyle.ContentMarginTop = BubblePadding;
            bubbleStyle.ContentMarginBottom = BubblePadding;
            _bubblePanel.AddThemeStyleboxOverride("panel", bubbleStyle);

            // Container for centering the text vertically
            var textContainer = new CenterContainer();
            textContainer.Name = "ArchipelagoTextContainer";
            textContainer.SizeFlagsHorizontal = Control.SizeFlags.Fill;
            textContainer.SizeFlagsVertical = Control.SizeFlags.Fill;
            textContainer.MouseFilter = Control.MouseFilterEnum.Ignore;
            _bubblePanel.AddChild(textContainer);

            // Message label using MegaRichTextLabel (the in-game rich text label with effects support)
            // MegaRichTextLabel defaults to AutoSizeEnabled=true so explicitly set it to false.
            // Notifications instead use a fixed font size and let FitContent grow only their height.
            // MegaCrit's spaghetti code forces AutoSizeEnabled to be false only if you try assign it AFTER
            // FitContent is true, very annoying.
            _messageLabel = new MegaRichTextLabel
            {
                Name = "ArchipelagoNotificationLabel",
                CustomMinimumSize = new Vector2(BubbleWidth - BubblePadding * 2, 0),
                SizeFlagsHorizontal = Control.SizeFlags.Fill,
                AutoSizeEnabled = false,
                FitContent = true,
                AutowrapMode = TextServer.AutowrapMode.Word,
                BbcodeEnabled = true,
                MouseFilter = Control.MouseFilterEnum.Ignore,
            };
            _messageLabel.AddThemeFontSizeOverride("normal_font_size", FontSize);

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
            bubbleContainer.AddChild(_bubblePanel);

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
            tailContainer.SizeFlagsVertical = Control.SizeFlags.ShrinkBegin;
            tailContainer.MouseFilter = Control.MouseFilterEnum.Ignore;

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
                new Vector2(0, midY), // Point (left, center)
            };
            tail.Color = new Color(0.18f, 0.15f, 0.25f, 0.95f); // Match bubble background
            tailContainer.AddChild(tail);

            return tailContainer;
        }
    }
}
