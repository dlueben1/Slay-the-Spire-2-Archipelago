using Godot;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;
using Newtonsoft.Json;
using StS2AP.Utils;
using System;

namespace StS2AP.UI
{
    /// <summary>
    /// Static class that creates and manages the Archipelago connection UI overlay.
    /// All UI is created programmatically from C# since we cannot modify Godot assets directly.
    /// </summary>
    public static class ArchipelagoConnectionUI
    {
        private static Control? _rootPanel;
        private static LineEdit? _slotNameInput;
        private static LineEdit? _urlInput;
        private static LineEdit? _passwordInput;
        private static Button? _connectButton;
        private static Button? _closeButton;
        private static Label? _statusLabel;

        /// <summary>
        /// Whether the UI is currently visible
        /// </summary>
        public static bool IsVisible => _rootPanel?.Visible ?? false;

        /// <summary>
        /// Event fired when the Connect button is pressed
        /// </summary>
        public static event Action<string, string, string>? OnConnectPressed;

        /// <summary>
        /// Injects the Archipelago connection UI into the current scene tree.
        /// Should be called when the main menu is ready.
        /// </summary>
        public static void InjectUI()
        {
            try
            {
                // Get the scene tree root
                var sceneTree = Engine.GetMainLoop() as SceneTree;
                if (sceneTree == null)
                {
                    LogUtility.Error("Failed to get SceneTree - cannot inject UI");
                    return;
                }

                var root = sceneTree.Root;
                if (root == null)
                {
                    LogUtility.Error("Failed to get root node - cannot inject UI");
                    return;
                }

                // Don't build the UI if it's already present, just make sure it's visible
                if (_rootPanel != null && IsInstanceValid(_rootPanel))
                {
                    _rootPanel.Visible = true;
                    return;
                }

                // Create the UI
                _rootPanel = CreateUI();

                // Add to the root as a CanvasLayer so it renders on top
                var canvasLayer = new CanvasLayer();
                canvasLayer.Name = "ArchipelagoUILayer";
                canvasLayer.Layer = 100; // High layer to render on top
                canvasLayer.AddChild(_rootPanel);
                root.AddChild(canvasLayer);

                LogUtility.Success("Archipelago connection UI injected successfully");
            }
            catch (Exception ex)
            {
                LogUtility.Error($"Failed to inject Archipelago UI: {ex.Message}");
            }
        }

        /// <summary>
        /// Removes the UI from the scene tree
        /// </summary>
        public static void RemoveUI()
        {
            if (_rootPanel != null && IsInstanceValid(_rootPanel))
            {
                var parent = _rootPanel.GetParent();
                if (parent != null)
                {
                    parent.QueueFree(); // Free the CanvasLayer which contains our UI
                }
                _rootPanel = null;
            }
        }

        /// <summary>
        /// Shows the connection UI
        /// </summary>
        public static void Show()
        {
            if (_rootPanel != null && IsInstanceValid(_rootPanel))
            {
                _rootPanel.Visible = true;
            }
        }

        /// <summary>
        /// Hides the connection UI, returns to the main menu
        /// </summary>
        public static void Hide()
        {
            // Hide the Connection UI
            if (_rootPanel != null && IsInstanceValid(_rootPanel))
            {
                _rootPanel.Visible = false;
            }
        }

        /// <summary>
        /// Updates the status label text
        /// </summary>
        public static void SetStatus(string status)
        {
            if (_statusLabel != null && IsInstanceValid(_statusLabel))
            {
                _statusLabel.Text = status;
            }
        }

        /// <summary>
        /// Enables or disables the connect button
        /// </summary>
        public static void SetConnectButtonEnabled(bool enabled)
        {
            if (_connectButton != null && IsInstanceValid(_connectButton))
            {
                _connectButton.Disabled = !enabled;
            }
        }

        /// <summary>
        /// Enables or disables the close button
        /// </summary>
        public static void SetCloseButtonEnabled(bool enabled)
        {
            if (_closeButton != null && IsInstanceValid(_closeButton))
            {
                _closeButton.Disabled = !enabled;
            }
        }

        private static bool IsInstanceValid(GodotObject obj)
        {
            return GodotObject.IsInstanceValid(obj);
        }

        private static Control CreateUI()
        {
            // Main container - semi-transparent panel that covers the screen
            var root = new Control();
            root.Name = "ArchipelagoConnectionUI";
            root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            root.MouseFilter = Control.MouseFilterEnum.Stop;

            // Dark overlay background
            var overlay = new ColorRect();
            overlay.Name = "Overlay";
            overlay.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            overlay.Color = new Color(0, 0, 0, 0.7f);
            overlay.MouseFilter = Control.MouseFilterEnum.Stop;
            root.AddChild(overlay);

            // Center panel container
            var centerContainer = new CenterContainer();
            centerContainer.Name = "CenterContainer";
            centerContainer.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            centerContainer.MouseFilter = Control.MouseFilterEnum.Ignore;
            root.AddChild(centerContainer);

            // Main panel
            var panel = new PanelContainer();
            panel.Name = "MainPanel";
            panel.CustomMinimumSize = new Vector2(400, 300);

            // Create a StyleBoxFlat for the panel background
            var panelStyle = new StyleBoxFlat();
            panelStyle.BgColor = new Color(0.1f, 0.1f, 0.15f, 0.95f);
            panelStyle.BorderColor = new Color(0.8f, 0.6f, 0.2f, 1f);
            panelStyle.SetBorderWidthAll(2);
            panelStyle.SetCornerRadiusAll(8);
            panelStyle.SetContentMarginAll(20);
            panel.AddThemeStyleboxOverride("panel", panelStyle);
            centerContainer.AddChild(panel);

            // Vertical layout for content
            var vbox = new VBoxContainer();
            vbox.Name = "ContentVBox";
            vbox.AddThemeConstantOverride("separation", 15);
            panel.AddChild(vbox);

            // Title
            var title = new Label();
            title.Name = "Title";
            title.Text = "Archipelago Connection";
            title.HorizontalAlignment = HorizontalAlignment.Center;
            title.AddThemeFontSizeOverride("font_size", 24);
            title.AddThemeColorOverride("font_color", new Color(0.9f, 0.75f, 0.3f)); // Gold color
            vbox.AddChild(title);

            // Separator
            var separator = new HSeparator();
            separator.AddThemeConstantOverride("separation", 10);
            vbox.AddChild(separator);

            var cachedData = ConnectionData.Load();

            // Slot Name field
            vbox.AddChild(CreateLabeledInput("Slot Name:", "Enter your slot name...", out _slotNameInput));
            _slotNameInput!.Text = cachedData.SlotName;

            // URL field
            vbox.AddChild(CreateLabeledInput("Server URL:", "archipelago.gg:38281", out _urlInput));
            _urlInput!.Text = cachedData.Connection; // Default value

            // Password field
            vbox.AddChild(CreateLabeledInput("Password:", "(optional)", out _passwordInput, isSecret: true));
            _passwordInput!.Text = cachedData.Password;

            // Spacer
            var spacer = new Control();
            spacer.CustomMinimumSize = new Vector2(0, 10);
            vbox.AddChild(spacer);

            // Button container
            var buttonContainer = new HBoxContainer();
            buttonContainer.Name = "ButtonContainer";
            buttonContainer.Alignment = BoxContainer.AlignmentMode.Center;
            buttonContainer.AddThemeConstantOverride("separation", 20);
            vbox.AddChild(buttonContainer);

            // Close button
            _closeButton = CreateStyledButton("Close", new Color(0.5f, 0.5f, 0.5f));
            _closeButton.Pressed += OnCloseButtonPressed;
            buttonContainer.AddChild(_closeButton);

            // Connect button
            _connectButton = CreateStyledButton("Connect", new Color(0.2f, 0.6f, 0.3f));
            _connectButton.Pressed += OnConnectButtonPressed;
            buttonContainer.AddChild(_connectButton);

            // Status label
            _statusLabel = new Label();
            _statusLabel.Name = "StatusLabel";
            _statusLabel.Text = "";
            _statusLabel.HorizontalAlignment = HorizontalAlignment.Center;
            _statusLabel.AddThemeFontSizeOverride("font_size", 14);
            _statusLabel.AddThemeColorOverride("font_color", new Color(0.7f, 0.7f, 0.7f));
            vbox.AddChild(_statusLabel);

            return root;
        }

        private static Control CreateLabeledInput(string labelText, string placeholder, out LineEdit lineEdit, bool isSecret = false)
        {
            var container = new VBoxContainer();
            container.AddThemeConstantOverride("separation", 5);

            // Label
            var label = new Label();
            label.Text = labelText;
            label.AddThemeFontSizeOverride("font_size", 16);
            label.AddThemeColorOverride("font_color", new Color(0.85f, 0.85f, 0.85f));
            container.AddChild(label);

            // Input field
            lineEdit = new LineEdit();
            lineEdit.PlaceholderText = placeholder;
            lineEdit.CustomMinimumSize = new Vector2(350, 35);
            lineEdit.Secret = isSecret;

            // Style the input
            var inputStyle = new StyleBoxFlat();
            inputStyle.BgColor = new Color(0.15f, 0.15f, 0.2f, 1f);
            inputStyle.BorderColor = new Color(0.4f, 0.4f, 0.5f, 1f);
            inputStyle.SetBorderWidthAll(1);
            inputStyle.SetCornerRadiusAll(4);
            inputStyle.SetContentMarginAll(8);
            lineEdit.AddThemeStyleboxOverride("normal", inputStyle);

            var inputStyleFocus = new StyleBoxFlat();
            inputStyleFocus.BgColor = new Color(0.18f, 0.18f, 0.25f, 1f);
            inputStyleFocus.BorderColor = new Color(0.8f, 0.6f, 0.2f, 1f); // Gold focus
            inputStyleFocus.SetBorderWidthAll(2);
            inputStyleFocus.SetCornerRadiusAll(4);
            inputStyleFocus.SetContentMarginAll(8);
            lineEdit.AddThemeStyleboxOverride("focus", inputStyleFocus);

            lineEdit.AddThemeColorOverride("font_color", new Color(1f, 1f, 1f));
            lineEdit.AddThemeColorOverride("font_placeholder_color", new Color(0.5f, 0.5f, 0.5f));
            lineEdit.AddThemeFontSizeOverride("font_size", 16);

            container.AddChild(lineEdit);

            return container;
        }

        private static Button CreateStyledButton(string text, Color baseColor)
        {
            var button = new Button();
            button.Text = text;
            button.CustomMinimumSize = new Vector2(120, 40);

            // Normal style
            var normalStyle = new StyleBoxFlat();
            normalStyle.BgColor = baseColor;
            normalStyle.SetCornerRadiusAll(6);
            normalStyle.SetContentMarginAll(10);
            button.AddThemeStyleboxOverride("normal", normalStyle);

            // Hover style
            var hoverStyle = new StyleBoxFlat();
            hoverStyle.BgColor = baseColor.Lightened(0.2f);
            hoverStyle.SetCornerRadiusAll(6);
            hoverStyle.SetContentMarginAll(10);
            button.AddThemeStyleboxOverride("hover", hoverStyle);

            // Pressed style
            var pressedStyle = new StyleBoxFlat();
            pressedStyle.BgColor = baseColor.Darkened(0.2f);
            pressedStyle.SetCornerRadiusAll(6);
            pressedStyle.SetContentMarginAll(10);
            button.AddThemeStyleboxOverride("pressed", pressedStyle);

            // Disabled style
            var disabledStyle = new StyleBoxFlat();
            disabledStyle.BgColor = new Color(0.3f, 0.3f, 0.3f, 0.5f);
            disabledStyle.SetCornerRadiusAll(6);
            disabledStyle.SetContentMarginAll(10);
            button.AddThemeStyleboxOverride("disabled", disabledStyle);

            button.AddThemeColorOverride("font_color", new Color(1f, 1f, 1f));
            button.AddThemeColorOverride("font_hover_color", new Color(1f, 1f, 1f));
            button.AddThemeColorOverride("font_pressed_color", new Color(0.9f, 0.9f, 0.9f));
            button.AddThemeColorOverride("font_disabled_color", new Color(0.5f, 0.5f, 0.5f));
            button.AddThemeFontSizeOverride("font_size", 16);

            return button;
        }

        /// <summary>
        /// Fires when "Connect" is pressed, handles connecting to Archipelago
        /// </summary>
        private static void OnConnectButtonPressed()
        {
            var slotName = _slotNameInput?.Text ?? "";
            var url = _urlInput?.Text ?? "";
            var password = _passwordInput?.Text ?? "";

            // Validates the Slot Name
            if (string.IsNullOrWhiteSpace(slotName))
            {
                SetStatus("Please enter a slot name");
                return;
            }

            // Validates a URL is present
            if (string.IsNullOrWhiteSpace(url))
            {
                SetStatus("Please enter a server URL");
                return;
            }

            // Begin Connecting
            LogUtility.Info($"Connect pressed - Slot: {slotName}, URL: {url}");
            SetStatus("Connecting...");
            SetConnectButtonEnabled(false);
            SetCloseButtonEnabled(false);
            ArchipelagoClient.ServerAddress = url;
            ArchipelagoClient.ServerPassword = password;
            ArchipelagoClient.PlayerName = slotName;
            ArchipelagoClient.ConnectionStateChanged += OnConnectionResult;
            ArchipelagoClient.Connect();

            var connectionData = new ConnectionData()
            {
                SlotName = slotName,
                Connection = url,
                Password = password,
            };
            connectionData.Save();


            // Fire the event for external handling
            OnConnectPressed?.Invoke(slotName, url, password);
        }

        /// <summary>
        /// Fires on when a connection attempt to Archipelago completes
        /// </summary>
        private static void OnConnectionResult(object? sender, ResultEventArgs e)
        {
            // Did we connect?
            if (e.Value)
            {
                // Set status
                SetStatus("Connected successfully!");

                // Enter the game
                var _charSelectScreen = MenuUtility.SubmenuStack.GetSubmenuType<NCharacterSelectScreen>();
                _charSelectScreen?.InitializeSingleplayer();
                MenuUtility.SubmenuStack.Push(_charSelectScreen);

                // Hide the connection UI
                Hide();
            }
            // We failed to connect
            else
            {
                SetStatus("Failed to connect. Please check your details and try again.");
                SetConnectButtonEnabled(true);
                SetCloseButtonEnabled(true);
            }
        }

        /// <summary>
        /// Fires when the Close button is pressed
        /// </summary>
        private static void OnCloseButtonPressed()
        {
            // Hide the connection UI
            Hide();

            // Pop the submenu stack to return to the main menu
            MenuUtility.SubmenuStack?.Pop();
        }

        private class ConnectionData
        {
            private static readonly string CONNECTION_FILE = "user://ap.connection";
            public string SlotName { get; set; } = "Player1";
            public string Connection { get; set; } = "archipelago.gg:38281";
            public string Password { get; set; } = "";


            public static ConnectionData Load()
            {
                if (!Godot.FileAccess.FileExists(CONNECTION_FILE))
                {
                    return new ConnectionData();
                }

                try
                {
                    using var data = Godot.FileAccess.Open(CONNECTION_FILE, Godot.FileAccess.ModeFlags.Read);

                    var rawJson = data.GetAsText();
                    var connectionData = JsonConvert.DeserializeObject<ConnectionData>(rawJson);
                    return connectionData;
                }
                catch(Exception ex)
                {
                    LogUtility.Error($"Failed to read connection data from disk {ex.Message}");
                    return new ConnectionData();
                }
            }

            public void Save()
            {
                try
                {
                    using var handle = Godot.FileAccess.Open(CONNECTION_FILE, Godot.FileAccess.ModeFlags.Write);

                    var connectionData = JsonConvert.SerializeObject(this);
                    handle.StoreLine(connectionData);
                }
                catch(Exception ex)
                {
                    LogUtility.Error($"Failed to save connection data to disk {ex.Message}");
                }
            }
        }
    }
}
