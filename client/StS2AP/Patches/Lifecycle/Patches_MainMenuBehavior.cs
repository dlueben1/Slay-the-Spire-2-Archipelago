using System.Diagnostics;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Game.Lobby;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Multiplayer;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.addons.mega_text;
using StS2AP.UI;
using StS2AP.Utils;

namespace StS2AP.Patches
{
    /// <summary>
    /// Patches for <see cref="NMainMenu"/> and all of its related submenus.
    /// Used primarily to reconfigure the UI for Archipelago, as well as
    /// injecting our custom Archipelago Connection UI.
    /// </summary>
    public static class Patches_MainMenuBehavior
    {
        private static NMainMenuTextButton? _singleplayerButton;
        private static NMainMenuTextButton? _connectButton;
        private static Label? _connectionStatusLabel;

        public static void RefreshConnectionPresentation()
        {
            if (_connectionStatusLabel != null
                && GodotObject.IsInstanceValid(_connectionStatusLabel))
            {
                _connectionStatusLabel.Text = ArchipelagoClient.State switch
                {
                    ConnectionState.Connected =>
                        $"Archipelago: Connected to {ArchipelagoClient.ServerAddress} "
                            + $"with name {ArchipelagoClient.PlayerName}",
                    ConnectionState.Connecting => "Archipelago: Connecting...",
                    ConnectionState.Reconnecting => "Archipelago: Reconnecting...",
                    _ => "Archipelago: Not connected (multiplayer guest available)",
                };
            }

            bool canStartSingleplayer = ArchipelagoClient.IsConnected;
            if (_singleplayerButton != null && GodotObject.IsInstanceValid(_singleplayerButton))
            {
                if (canStartSingleplayer)
                    _singleplayerButton.Enable();
                else
                    _singleplayerButton.Disable();
            }

            if (_connectButton == null || !GodotObject.IsInstanceValid(_connectButton))
                return;

            _connectButton.Enable();

            if (_connectButton.label != null)
            {
                _connectButton.label.Text = ArchipelagoClient.State switch
                {
                    ConnectionState.Connected => "Disconnect from Archipelago",
                    ConnectionState.Connecting => "Cancel Archipelago Connection",
                    ConnectionState.Reconnecting => "Cancel Archipelago Reconnect",
                    _ when ArchipelagoClient.HasSlotConnection => "Disconnect from Archipelago",
                    _ => "Connect to Archipelago",
                };
            }
        }

        private static void OnConnectionStateChanged(ConnectionState _) =>
            RefreshConnectionPresentation();

        private static void OnConnectButtonPressed()
        {
            if (!ArchipelagoClient.CanLeaveSlot)
                return;
            if (!ArchipelagoClient.HasSlotConnection)
            {
                MultiplayerSupport.ClearPendingPlaySelection();
                ArchipelagoConnectionUI.InjectUI();
                ArchipelagoNotificationUI.InjectUI();
                return;
            }

            // Bind the confirmation to this session; a late confirmation must not disconnect
            // a replacement session after an automatic reconnect or another menu action.
            var session = ArchipelagoClient.Session;
            var body = new LocString("main_menu_ui", "AP_DISCONNECT.body");
            body.Add("slot", ArchipelagoClient.PlayerName ?? "");
            var popup = new ConfirmPopup
            {
                Header = new LocString("main_menu_ui", "AP_DISCONNECT.header"),
                Body = body,
                ButtonPressed = confirmed =>
                {
                    if (confirmed && ReferenceEquals(session, ArchipelagoClient.Session))
                        ArchipelagoClient.TryLeaveSlot();
                },
            };
            popup.Show();
        }

        #region Clone Target References

        // The path that StS2 stores the main menu buttons in
        private const string MainMenuButtonsPath = "MainMenuTextButtons";

        // The subpath to the "Single Player" button, which we rename to "AP Singleplayer"
        private const string SingleplayerButtonPath = MainMenuButtonsPath + "/SingleplayerButton";

        private const string MultiplayerButtonPath = MainMenuButtonsPath + "/MultiplayerButton";

        private const string ConnectButtonName = "ArchipelagoConnectButton";
        private const string ConnectButtonPath = MainMenuButtonsPath + "/" + ConnectButtonName;

        private const string ConnectionStatusName = "ArchipelagoConnectionStatus";

        // The subpath to the "Settings" button, which we will clone many times
        private const string SettingsButtonPath = MainMenuButtonsPath + "/SettingsButton";

        // The new name & path of our injected Archipelago Settings button, which is a clone of the vanilla Settings button
        private const string ArchipelagoSettingsButtonName = "ArchipelagoSettingsButton";
        private const string ArchipelagoSettingsButtonPath =
            MainMenuButtonsPath + "/" + ArchipelagoSettingsButtonName;

        // The new name & path of our injected "Install APWorld" button, which is a clone of the vanilla Settings button
        private const string InstallWorldButtonName = "InstallAPWorldButton";
        private const string InstallWorldButtonPath =
            MainMenuButtonsPath + "/" + InstallWorldButtonName;

        // The new name & path of our injected "Visit Website" button, which is a clone of the vanilla Settings button
        private const string WebsiteButtonName = "VisitWebsiteButton";
        private const string WebsiteButtonPath = MainMenuButtonsPath + "/" + WebsiteButtonName;

        #endregion

        #region Main Menu Patches

        // Backing out of Host/Join or a lobby must release the pending multiplayer intent.
        // Otherwise settings still see a multiplayer scope after the player returns home.
        [HarmonyPatch(typeof(NMainMenu), "OnSubmenuStackChanged")]
        private static class ClearPendingPlaySelectionAtHome
        {
            [HarmonyPostfix]
            private static void Postfix(NMainMenu __instance)
            {
                if (!__instance.SubmenuStack.SubmenusOpen
                    && !RunManager.Instance.IsInProgress
                    && !GameUtility.IsInRun && !MultiplayerSupport.IsRealMultiplayerRun
                    && !MultiplayerSupport.TryGetObservedStartLobby(out _))
                {
                    MultiplayerSupport.ClearPendingPlaySelection();
                }
            }
        }

        /// <summary>
        /// Delays beta StS2's developer fast-multiplayer action until this process's
        /// AP slot has connected and prepared. Ordinary fastmp invocations remain native.
        /// </summary>
        [HarmonyPatch(typeof(NMainMenu), "CheckCommandLineArgs")]
        public static class DelayApFastMultiplayerUntilReady
        {
            [HarmonyPrefix]
            public static bool Prefix() => !ApFastMpLaunchController.TryBeginFromCommandLine();
        }

        /// <summary>
        /// Changes the main menu UI for the Archipelago Mod.
        /// This includes hiding, renaming, and injecting menu options.
        ///
        /// Shout out to BaseLib for pioneering the injection of main menu options,
        /// I inspired a lot of the changes here off of their work. Thank you!
        /// </summary>
        [HarmonyPatch(typeof(NMainMenu), nameof(NMainMenu._Ready), [])]
        public static class ReconfigureMainMenu
        {
            /// <summary>
            /// Injects custom menu buttons before the vanilla _Ready() method
            /// collects and configures the main-menu button list.
            /// </summary>
            [HarmonyPrefix]
            public static void Prefix(NMainMenu __instance)
            {
                InjectMainMenuButtons(__instance);
            }

            /// <summary>
            /// Applies Archipelago's main-menu visibility and text changes
            /// after the vanilla menu has finished initializing.
            /// </summary>
            [HarmonyPostfix]
            public static void Postfix(NMainMenu __instance)
            {
                // Grab reference to the menu stack
                MenuUtility.SubmenuStack = __instance.SubmenuStack;
                MenuUtility.MainMenu = __instance;

                // Grab the single player button that we will refactor into "Archipelago"
                var singleplayerButton = __instance.GetNode<NMainMenuTextButton>(
                    SingleplayerButtonPath
                );
                _singleplayerButton = singleplayerButton;

                var connectButton = __instance.GetNodeOrNull<NMainMenuTextButton>(
                    ConnectButtonPath
                );
                _connectButton = connectButton;
                _connectionStatusLabel = __instance.GetNodeOrNull<Label>(ConnectionStatusName);

                // Grab the custom Archipelago settings button
                var archipelagoSettingsButton = __instance.GetNodeOrNull<NMainMenuTextButton>(
                    ArchipelagoSettingsButtonPath
                );

                // Grab the custom Install APWorld button
                var installWorldButton = __instance.GetNodeOrNull<NMainMenuTextButton>(
                    InstallWorldButtonPath
                );

                // Grab the custom Visit Website button
                var visitWebsiteButton = __instance.GetNodeOrNull<NMainMenuTextButton>(
                    WebsiteButtonPath
                );

                // Grab the original settings button
                var settingsButton = __instance.GetNode<NMainMenuTextButton>(SettingsButtonPath);

                // Grab references to all the buttons we shouldn't have
                var continueButton = __instance.GetNode<NMainMenuTextButton>(
                    MainMenuButtonsPath + "/ContinueButton"
                );
                var multiplayerButton = __instance.GetNode<NMainMenuTextButton>(
                    MultiplayerButtonPath
                );
                var abandonRunButton = __instance.GetNode<NMainMenuTextButton>(
                    MainMenuButtonsPath + "/AbandonRunButton"
                );
                var compendiumButton = __instance.GetNode<NMainMenuTextButton>(
                    MainMenuButtonsPath + "/CompendiumButton"
                );
                var timelineButton = __instance.GetNode<NMainMenuTextButton>(
                    MainMenuButtonsPath + "/TimelineButton"
                );
                var openProfileScreenButton = __instance.GetNode<NOpenProfileScreenButton>(
                    "%ChangeProfileButton"
                );

                // Tweak the visibility of all Main Menu buttons for the overhaul
                singleplayerButton.Visible = true;
                multiplayerButton.Visible = true;
                continueButton.Visible = false;
                abandonRunButton.Visible = false;
                compendiumButton.Visible = false;
                timelineButton.Visible = false;
                openProfileScreenButton.Visible = false;

                // Some buttons need this additional Enable()/Disable() call I'm honestly still not sure why this worked
                multiplayerButton.Enable();
                timelineButton.Disable();
                compendiumButton.Disable();

                // Change the name of "Single Player" for Archipelago
                singleplayerButton!.label!.Text = "AP Singleplayer";

                if (connectButton?.label != null)
                    connectButton.Visible = true;

                ArchipelagoClient.ConnectionStateChanged -= OnConnectionStateChanged;
                ArchipelagoClient.ConnectionStateChanged += OnConnectionStateChanged;
                RefreshConnectionPresentation();

                multiplayerButton!.label!.Text = "AP Multiplayer";

                // Change the name of "Settings" to "Game Settings" to avoid confusion with the injected Archipelago Settings button
                settingsButton!.label!.Text = "Game Settings";

                /// Configure the injected settings button after its label
                /// reference has been initialized by the vanilla _Ready method.
                if (archipelagoSettingsButton?.label != null)
                {
                    archipelagoSettingsButton.Visible = true;
                    archipelagoSettingsButton.Enable();
                    archipelagoSettingsButton.label.Text = "Archipelago Settings";
                }

                /// Configure the injected Install APWorld button after its label
                /// reference has been initialized by the vanilla _Ready method.
                if (installWorldButton?.label != null)
                {
                    installWorldButton.Visible = true;
                    installWorldButton.Enable();
                    installWorldButton.label.Text = "Install APWorld";
                }

                /// Configure the injected Visit Website button after its label
                /// reference has been initialized by the vanilla _Ready method.
                if (visitWebsiteButton?.label != null)
                {
                    visitWebsiteButton.Visible = true;
                    visitWebsiteButton.Enable();
                    visitWebsiteButton.label.Text = "Mod Website & YAML Builder";
                }
            }
        }

        /// <summary>
        /// Creates the Archipelago main-menu buttons and places the primary actions in the
        /// order Connect, AP Singleplayer, AP Multiplayer.
        ///
        /// Duplicating a vanilla button preserves the game's styling,
        /// sounds, animations, and controller behavior.
        /// </summary>
        private static void InjectMainMenuButtons(NMainMenu mainMenu)
        {
            // Avoid injecting a duplicate if _Ready is ever called again.
            if (mainMenu.GetNodeOrNull<NMainMenuTextButton>(ArchipelagoSettingsButtonPath) != null)
            {
                return;
            }

            // Grab references to the buttons we need to manipulate
            var singleplayerButton = mainMenu.GetNode<NMainMenuTextButton>(SingleplayerButtonPath);
            var multiplayerButton = mainMenu.GetNode<NMainMenuTextButton>(MultiplayerButtonPath);
            var settingsButton = mainMenu.GetNode<NMainMenuTextButton>(SettingsButtonPath);
            Node buttonContainer = singleplayerButton.GetParent();
            int singleplayerIndex = singleplayerButton.GetIndex();

            var connectButton = (NMainMenuTextButton)settingsButton.Duplicate();
            connectButton.Name = ConnectButtonName;
            connectButton.Connect(
                NClickableControl.SignalName.Released,
                Callable.From<NButton>(_ => OnConnectButtonPressed())
            );
            singleplayerButton.AddSibling(connectButton);
            buttonContainer.MoveChild(connectButton, singleplayerIndex);
            connectButton.CustomMinimumSize = new Vector2(
                300f,
                connectButton.CustomMinimumSize.Y
            );

            // Create the new Archipelago Settings button by duplicating the vanilla Settings button
            var archipelagoSettingsButton = (NMainMenuTextButton)settingsButton.Duplicate();
            archipelagoSettingsButton.Name = ArchipelagoSettingsButtonName;
            archipelagoSettingsButton.Connect(
                NClickableControl.SignalName.Released,
                Callable.From<NButton>(_ =>
                {
                    MenuUtility.OpenArchipelagoSettings();
                })
            );
            multiplayerButton.AddSibling(archipelagoSettingsButton);
            archipelagoSettingsButton.CustomMinimumSize = new Vector2(
                300f,
                archipelagoSettingsButton.CustomMinimumSize.Y
            );

            // Create an "Install APWorld" button
            var installButton = (NMainMenuTextButton)settingsButton.Duplicate();
            installButton.Name = InstallWorldButtonName;
            installButton.Connect(
                NClickableControl.SignalName.Released,
                Callable.From<NButton>(_ =>
                {
                    // Show a dialog that speedbumps the user and ensures they want to install this
                    var popup = new ConfirmPopup();
                    popup.Header = new LocString("main_menu_ui", "INSTALL_APWORLD.header");
                    popup.Body = new LocString("main_menu_ui", "INSTALL_APWORLD.body");
                    popup.ButtonPressed = (yesPressed) =>
                    {
                        if (yesPressed)
                        {
                            try
                            {
                                // Shared bundle files live at the registered mod root, even
                                // when this assembly was loaded from lib/<game-version>.
                                var mod = ModManager.GetLoadedMods()
                                    .Single(mod => mod.manifest?.id == ModEntry.ModId);
                                var apWorldPath = Path.Combine(mod.path, "spire2.apworld");
                                LogUtility.Info($"Launching APWorld installer: {apWorldPath}");
                                Process.Start(
                                    new ProcessStartInfo
                                    {
                                        FileName = apWorldPath,
                                        UseShellExecute = true,
                                    }
                                );
                            }
                            catch (Exception ex)
                            {
                                LogUtility.Error(
                                    $"Failed to launch APWorld installer: {ex.Message}\n{ex.StackTrace}"
                                );
                            }
                        }
                    };
                    popup.Show();
                })
            );
            settingsButton.AddSibling(installButton);
            settingsButton.CustomMinimumSize = new Vector2(300f, installButton.CustomMinimumSize.Y);

            // Create a "Website & YAML Builder" button
            var websiteButton = (NMainMenuTextButton)settingsButton.Duplicate();
            websiteButton.Name = WebsiteButtonName;
            websiteButton.Connect(
                NClickableControl.SignalName.Released,
                Callable.From<NButton>(_ =>
                {
                    // Go to the website
                    var url = "https://sts2ap.net";
                    Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
                })
            );
            installButton.AddSibling(websiteButton);
            installButton.CustomMinimumSize = new Vector2(300f, websiteButton.CustomMinimumSize.Y);

            // Adjust button focusing
            var selfNodePath = new NodePath(".");
            connectButton.FocusNeighborLeft = selfNodePath;
            connectButton.FocusNeighborRight = selfNodePath;
            archipelagoSettingsButton.FocusNeighborLeft = selfNodePath;
            archipelagoSettingsButton.FocusNeighborRight = selfNodePath;
            installButton.FocusNeighborLeft = selfNodePath;
            installButton.FocusNeighborRight = selfNodePath;

            var connectionStatus = new Label
            {
                Name = ConnectionStatusName,
                MouseFilter = Control.MouseFilterEnum.Ignore,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
            };
            connectionStatus.SetAnchorsPreset(Control.LayoutPreset.BottomLeft);
            connectionStatus.OffsetLeft = 32f;
            connectionStatus.OffsetTop = -72f;
            connectionStatus.OffsetRight = 900f;
            connectionStatus.OffsetBottom = -24f;
            connectionStatus.AddThemeFontSizeOverride("font_size", 18);
            connectionStatus.AddThemeColorOverride("font_color", new Color(0.85f, 0.9f, 1f));
            mainMenu.AddChild(connectionStatus);
        }

        /// <summary>
        /// Overrides the behavior of the Single Player "Sub Menu"
        /// </summary>
        [HarmonyPatch(typeof(NMainMenu), nameof(NMainMenu.OpenSingleplayerSubmenu), [])]
        public static class InjectAPMenu
        {
            [HarmonyPostfix]
            public static void Postfix(NSingleplayerSubmenu __result)
            {
                MultiplayerSupport.SelectDestination(ApPlayDestination.Singleplayer);
                Patches_ItemProcessor.ProcessDeferredItemsForSingleplayer();

                // Hide the actual sub-menu options
                var standardButton = __result.GetNode<NSubmenuButton>("StandardButton");
                var dailyButton = __result.GetNode<NSubmenuButton>("DailyButton");
                var customButton = __result.GetNode<NSubmenuButton>("CustomRunButton");
                var backButton = __result.GetNode<NBackButton>("BackButton");

                standardButton.Visible = false;
                dailyButton.Visible = false;
                customButton.Visible = false;
                backButton.Visible = false;

                // If we are connected, dive directly into the game
                if (ArchipelagoClient.IsConnected)
                {
                    MenuUtility.OpenCharacterSelect();
                }
                else
                {
                    NotificationUtility.ShowRawText(
                        "Connect to Archipelago from the main menu before starting AP Singleplayer."
                    );
                }
            }
        }

        /// <summary>
        /// Injects the custom Archipelago logo
        /// </summary>
        [HarmonyPatch(typeof(NMainMenuBg), nameof(NMainMenuBg.MethodName._Ready))]
        public static class InjectAPLogo
        {
            public static void Postfix(NMainMenuBg __instance)
            {
                var customLogoRect = new TextureRect();
                customLogoRect.Texture = GD.Load<Texture2D>(
                    // This is a stable path, defined in the `.png.import` file but I'm open to better ways to do this
                    "res://.godot/imported/archipelalogo.png-2f6acf8679de2a385a685cdb2750bebf.ctex"
                );
                customLogoRect.ExpandMode = TextureRect.ExpandModeEnum.FitWidth;
                customLogoRect.StretchMode = TextureRect.StretchModeEnum.Keep;

                // Add to the container and position it
                __instance.AddChild(customLogoRect);
                customLogoRect.Position = new Vector2(490, 70);
            }
        }

        /// <summary>
        /// Keeps AP Singleplayer on one consistent path regardless of vanilla run-count
        /// shortcuts. Connection is selected independently from the main menu.
        /// </summary>
        [HarmonyPatch(typeof(NMainMenu), nameof(NMainMenu.MethodName.SingleplayerButtonPressed))]
        public static class DisableSkippingToCharSelect
        {
            [HarmonyPrefix]
            public static bool Prefix(NMainMenu __instance, NButton _)
            {
                if (!ArchipelagoClient.IsConnected)
                {
                    NotificationUtility.ShowRawText(
                        "Connect to Archipelago before starting AP Singleplayer."
                    );
                    return false;
                }

                MultiplayerSupport.SelectDestination(ApPlayDestination.Singleplayer);
                Patches_ItemProcessor.ProcessDeferredItemsForSingleplayer();

                /// Always open the singleplayer submenu,
                /// regardless of NumberOfRuns.
                __instance.OpenSingleplayerSubmenu();
                return false;
            }
        }

        /// <summary>
        /// Opens MegaCrit's normal Host/Join flow. A connected process participates with its
        /// AP identity; a disconnected process explicitly participates as a guest.
        /// </summary>
        [HarmonyPatch(
            typeof(NMainMenu),
            nameof(NMainMenu.OpenMultiplayerSubmenu),
            new[] { typeof(NButton) }
        )]
        public static class ConnectBeforeMultiplayer
        {
            [HarmonyPrefix]
            public static bool Prefix()
            {
                MultiplayerSupport.BeginMultiplayerEntry();
                if (ArchipelagoClient.IsConnected)
                {
                    if (!MultiplayerSupport.CanEnterMultiplayerLobby(out _)
                        && !ArchipelagoClient.TryPrepareCurrentMultiplayerSession(
                            out string preparationError))
                    {
                        LogUtility.Warn(
                            $"Cannot open AP multiplayer lobby: {preparationError}"
                        );
                        NotificationUtility.ShowRawText(preparationError);
                        return false;
                    }

                    return MultiplayerSupport.CanEnterMultiplayerLobby(out _);
                }

                return MultiplayerSupport.CanEnterMultiplayerLobby(out _);
            }
        }

        /// <summary>
        /// Stops every native host entry point before it creates a lobby unless this process is
        /// connected to and prepared for its own AP slot. Joining remains available to guests.
        /// </summary>
        private static bool AllowHostCreation()
        {
            if (MultiplayerSupport.CanHostMultiplayer(out string reason))
                return true;

            LogUtility.Warn($"Blocked multiplayer host creation: {reason}");
            NotificationUtility.ShowRawText(reason);
            return false;
        }

        [HarmonyPatch(typeof(NMultiplayerSubmenu), "OnHostPressed", new[] { typeof(NButton) })]
        private static class RequireApSlotForHostButton
        {
            [HarmonyPrefix]
            private static bool Prefix()
            {
                if (!AllowHostCreation())
                    return false;

                // Vanilla bypasses NMultiplayerHostSubmenu for a profile's first-ever run.
                // Always route AP hosting through the mode submenu so save selection cannot
                // skip the campaign picker.
                NSubmenuStack? submenuStack = MenuUtility.SubmenuStack;
                if (submenuStack == null)
                {
                    const string reason = "The main-menu submenu stack is unavailable.";
                    LogUtility.Error(reason);
                    NotificationUtility.ShowRawText(reason);
                    return false;
                }
                submenuStack.PushSubmenuType<NMultiplayerHostSubmenu>();
                return false;
            }
        }

        [HarmonyPatch(
            typeof(NMultiplayerSubmenu),
            nameof(NMultiplayerSubmenu.FastHost),
            new[] { typeof(GameMode) }
        )]
        private static class RequireApSlotForFastHost
        {
            [HarmonyPrefix]
            private static bool Prefix() => AllowHostCreation();
        }

        [HarmonyPatch(
            typeof(NMultiplayerSubmenu),
            nameof(NMultiplayerSubmenu.StartHost),
            new[] { typeof(SerializableRun) }
        )]
        private static class RequireApSlotForSavedHost
        {
            [HarmonyPrefix]
            private static bool Prefix() => AllowHostCreation();
        }

        [HarmonyPatch(
            typeof(NMultiplayerHostSubmenu),
            nameof(NMultiplayerHostSubmenu.StartHost),
            new[] { typeof(GameMode) }
        )]
        private static class RequireApSlotForHostMode
        {
            [HarmonyPrefix]
            private static bool Prefix(
                NMultiplayerHostSubmenu __instance,
                GameMode gameMode)
            {
                if (!AllowHostCreation())
                    return false;
                if (ApMultiplayerCampaignFlow.IsResumingNativeStart)
                    return true;

                try
                {
                    ApMultiplayerCampaignFlow.OpenPicker(__instance, gameMode);
                }
                catch (Exception ex)
                {
                    LogUtility.Error($"Could not open the AP multiplayer campaign picker: {ex}");
                    NotificationUtility.ShowRawText(
                        "The AP multiplayer campaign picker could not be opened."
                    );
                }
                return false;
            }
        }

        /// <summary>
        /// AP owns multiplayer save selection. Keep Host available and remove the native
        /// single-save Load/Abandon bypasses from the AP multiplayer submenu.
        /// </summary>
        [HarmonyPatch(typeof(NMultiplayerSubmenu), "UpdateButtons")]
        private static class UseUnifiedApCampaignButton
        {
            [HarmonyPostfix]
            private static void Postfix(NMultiplayerSubmenu __instance)
            {
                if (MultiplayerSupport.PendingDestination != ApPlayDestination.Multiplayer
                    || MultiplayerSupport.PendingParticipation != ApParticipationKind.OwnApSlot)
                {
                    return;
                }

                __instance._hostButton.Visible = true;
                __instance._loadButton.Visible = false;
                __instance._abandonButton.Visible = false;
            }
        }

        [HarmonyPatch(typeof(NMultiplayerSubmenu), "get_InitialFocusedControl")]
        private static class FocusUnifiedApCampaignButton
        {
            [HarmonyPostfix]
            private static void Postfix(NMultiplayerSubmenu __instance, ref Control __result)
            {
                if (MultiplayerSupport.PendingDestination != ApPlayDestination.Multiplayer
                    || MultiplayerSupport.PendingParticipation != ApParticipationKind.OwnApSlot)
                {
                    return;
                }

                __result = __instance._hostButton;
            }
        }

        /// <summary>Prevents a local ready signal unless this process's AP owner is prepared.</summary>
        [HarmonyPatch(typeof(NCharacterSelectScreen), "OnEmbarkPressed")]
        public static class RequireApReadyToEmbark
        {
            [HarmonyPrefix]
            public static bool Prefix(NCharacterSelectScreen __instance)
            {
                if (MultiplayerSupport.PendingDestination != ApPlayDestination.Multiplayer)
                    return true;

                ApRunData.StageLocalPlayer(__instance.Lobby);
                CharacterModel character = BetaMainCompatibility.GetLocalCharacter(__instance.Lobby);
                if (!MultiplayerSupport.CanEmbark(character, out string blockedReason))
                    return BlockReady(__instance, blockedReason);

                if (__instance.Lobby.NetService.Type == NetGameType.Host
                    && !ApRunData.TryValidateHostLobbyContributions(
                        __instance.Lobby,
                        out blockedReason))
                {
                    return BlockReady(__instance, blockedReason);
                }

                if (!ApMultiplayerCampaignFlow.AllowNewCampaignEmbark(__instance))
                {
                    return false;
                }

                return true;
            }

            private static bool BlockReady(
                NCharacterSelectScreen screen,
                string blockedReason)
            {
                if (BetaMainCompatibility.IsLocalPlayerReady(screen.Lobby))
                    screen.Lobby.SetReady(ready: false);
                NotificationUtility.ShowRawText(blockedReason);
                LogUtility.Warn($"Blocked AP multiplayer embark: {blockedReason}");
                return false;
            }
        }

        /// <summary>
        /// The native load lobby permits continuing with missing players. Preserve that, but
        /// reject any connected STS identity that was not in the campaign's frozen roster.
        /// </summary>
        [HarmonyPatch(typeof(NMultiplayerLoadGameScreen), "ShouldAllowRunToBegin")]
        private static class RequireOriginalSavedRoster
        {
            [HarmonyPostfix]
            private static void Postfix(
                NMultiplayerLoadGameScreen __instance,
                ref Task<bool> __result)
            {
                __result = ValidateAfterVanilla(__instance, __result);
            }

            private static async Task<bool> ValidateAfterVanilla(
                NMultiplayerLoadGameScreen screen,
                Task<bool> vanillaResult)
            {
                if (!await vanillaResult)
                    return false;
                if (ApMultiplayerCampaignFlow.ValidateLoadLobbyRoster(
                    screen,
                    out string reason))
                {
                    return true;
                }

                LogUtility.Warn($"Blocked AP saved-run launch: {reason}");
                Callable.From(() => NotificationUtility.ShowRawText(reason)).CallDeferred();
                return false;
            }
        }

        /// <summary>
        /// Final authoritative guard for the race where lobby staging changes after the host
        /// readies or a client is the last player to ready. The host recomputes readiness from
        /// current RitsuLib staging; no saved validation flag is consulted.
        /// </summary>
        [HarmonyPatch(typeof(StartRunLobby), "BeginRunForAllPlayersIfAllReady")]
        public static class RequireCompleteApLobbyBeforeLaunch
        {
            [HarmonyPrefix]
            public static bool Prefix(StartRunLobby __instance)
            {
                if (MultiplayerSupport.PendingDestination != ApPlayDestination.Multiplayer
                    || __instance.NetService.Type != NetGameType.Host
                    || !__instance.IsAboutToBeginGame())
                {
                    return true;
                }

                if (ApRunData.TryValidateHostLobbyContributions(
                    __instance,
                    out string blockedReason))
                {
                    return ApCoopLobbyWarning.AllowLaunch(__instance);
                }
                // FLAG: i bet you if anything softlocks, it'll be here

                string message = $"AP multiplayer launch blocked: {blockedReason}";
                LogUtility.Warn(message);
                NotificationUtility.ShowRawText(message);
                MultiplayerSupport.RequestHostLobbyRefresh(__instance);
                return false;
            }
        }

        /// <summary>
        /// Replaces the null-platform test names with the AP slot and harness role while
        /// leaving the native lobby player IDs and ready synchronization untouched.
        /// </summary>
        [HarmonyPatch(typeof(NRemoteLobbyPlayer), nameof(NRemoteLobbyPlayer._Ready))]
        public static class LabelLocalHarnessPlayers
        {
            [HarmonyPostfix]
            public static void Postfix(NRemoteLobbyPlayer __instance)
            {
                if (!ApFastMpLaunchController.TryGetHarnessPlayerLabel(
                        __instance.PlayerId,
                        out string label))
                {
                    return;
                }

                __instance.GetNode<MegaLabel>("%NameplateLabel").SetTextAutoSize(label);
            }
        }

        #endregion

        #region Character Select Patches

        /// <summary>
        /// Hides the Back Button from the Character Select Screen.
        /// </summary>
        [HarmonyPatch(typeof(NCharacterSelectScreen))]
        public static class NCharacterSelectScreenPatches
        {
            [HarmonyPatch("_Ready")]
            [HarmonyPostfix]
            private static void HideBackButtonOnCharSelectScreen(NCharacterSelectScreen __instance)
            {
                if (MultiplayerSupport.PendingDestination == ApPlayDestination.Multiplayer)
                    return;

                __instance.GetNode<NBackButton>("BackButton").Visible = false;
            }
        }

        /// <summary>
        /// Ensures the player backs out to the main menu, and thus hides the
        /// connection UI, when they press the back button from the character
        /// select screen.
        /// </summary>
        [HarmonyPatch(typeof(NSubmenuStack), nameof(NSubmenuStack.Pop))]
        public static class BackOutFromCharSelectToMainMenu
        {
            [HarmonyPrefix]
            public static void Prefix(NSubmenuStack __instance, out bool __state)
            {
                // Capture what is being popped. Looking only at what remains in
                // the postfix can mistake any submenu above Single Player for
                // the character-select screen.
                __state = __instance.Peek() is NCharacterSelectScreen;
            }

            [HarmonyPostfix]
            public static void Postfix(NSubmenuStack __instance, bool __state)
            {
                // Only skip the hidden Single Player submenu after successfully
                // popping character select.
                if (__state && __instance.Peek() is NSingleplayerSubmenu)
                {
                    // Go back to the main menu
                    __instance.Pop();

                    // Force the UI to hide on the next main-thread frame
                    var sceneTree = Engine.GetMainLoop() as SceneTree;

                    if (sceneTree != null)
                    {
                        sceneTree.CreateTimer(0f).Timeout += () =>
                        {
                            ArchipelagoConnectionUI.Hide();
                        };
                    }
                }
            }
        }

        /// <summary>
        /// Injects the Archipelago Progress Tracker panel when the Character
        /// Select screen opens, and removes it when the screen closes.
        /// Keeping injection in OnSubmenuOpened ensures the CanvasLayer is
        /// created after the scene tree is fully set up.
        /// </summary>
        [HarmonyPatch(typeof(NCharacterSelectScreen))]
        public static class CharTrackerPanelPatches
        {
            /// <summary>
            /// Show the tracker panels as soon as the screen becomes active.
            /// </summary>
            [HarmonyPatch(nameof(NCharacterSelectScreen.OnSubmenuOpened))]
            [HarmonyPostfix]
            private static void OnOpened(NCharacterSelectScreen __instance)
            {
                MultiplayerSupport.ObserveStartLobby(__instance);

                // Vanilla guests have no AP settings or progress trackers.
                if (MultiplayerSupport.IsLocalGuest)
                    return;

                // Find the first character on the screen
                Control charButtonContainer = __instance.GetNode<Control>(
                    "CharSelectButtons/ButtonContainer"
                );

                NCharacterSelectButton firstButton =
                    charButtonContainer.GetChild<NCharacterSelectButton>(0);

                CharacterModel character = firstButton.Character;

                // Setup the character tracker UI
                ArchipelagoCharTrackerUI.InjectUI(character);

                // Setup the goal tracker UI. The initial goal text needs
                // to be slightly delayed or the text is rendered tiny.
                ArchipelagoGoalTrackerUI.InjectUI();

                var sceneTree = Engine.GetMainLoop() as SceneTree;

                if (sceneTree != null)
                {
                    sceneTree.CreateTimer(0.2f).Timeout += () =>
                    {
                        Callable.From(ArchipelagoGoalTrackerUI.UpdateGoalProgress).CallDeferred();
                    };
                }
            }

            /// <summary>
            /// Remove the tracker panels when the player leaves the
            /// Character Select screen.
            /// </summary>
            [HarmonyPatch(nameof(NCharacterSelectScreen.OnSubmenuClosed))]
            [HarmonyPostfix]
            private static void OnClosed(NCharacterSelectScreen __instance)
            {
                MultiplayerSupport.StopObservingStartLobby(__instance);
                ArchipelagoCharTrackerUI.RemoveUI();
                ArchipelagoGoalTrackerUI.RemoveUI();
            }
        }

        #endregion
    }
}
