using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using StS2AP.UI;
using StS2AP.Utils;
using STS2RitsuLib.Settings;

namespace StS2AP.Patches
{
    /// <summary>
    /// Patches for <see cref="NMainMenu"/> and all of its related submenus.
    /// Used primarily to reconfigure the UI for Archipelago, as well as
    /// injecting our custom Archipelago Connection UI.
    /// </summary>
    public static class Patches_MainMenuBehavior
    {
        #region Clone Target References

        // The path that StS2 stores the main menu buttons in
        private const string MainMenuButtonsPath = "MainMenuTextButtons";

        // The subpath to the "Single Player" button, which we rename to "Archipelago"
        private const string SingleplayerButtonPath = MainMenuButtonsPath + "/SingleplayerButton";

        // The subpath to the "Settings" button, which we will clone many times
        private const string SettingsButtonPath = MainMenuButtonsPath + "/SettingsButton";

        // The new name of our injected Archipelago Settings button, which is a clone of the vanilla Settings button
        private const string ArchipelagoSettingsButtonName = "ArchipelagoSettingsButton";

        // The new path to our injected Archipelago Settings button
        private const string ArchipelagoSettingsButtonPath =
            MainMenuButtonsPath + "/" + ArchipelagoSettingsButtonName;

        #endregion

        #region Main Menu Patches

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
                InjectArchipelagoSettingsButton(__instance);
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

                // Grab the single player button that we will refactor into "Archipelago"
                var singleplayerButton = __instance.GetNode<NMainMenuTextButton>(
                    SingleplayerButtonPath
                );

                // Grab the custom Archipelago settings button
                var archipelagoSettingsButton = __instance.GetNodeOrNull<NMainMenuTextButton>(
                    ArchipelagoSettingsButtonPath
                );

                // Grab the original settings button
                var settingsButton = __instance.GetNode<NMainMenuTextButton>(SettingsButtonPath);

                // Grab references to all the buttons we shouldn't have
                var continueButton = __instance.GetNode<NMainMenuTextButton>(
                    MainMenuButtonsPath + "/ContinueButton"
                );
                var multiplayerButton = __instance.GetNode<NMainMenuTextButton>(
                    MainMenuButtonsPath + "/MultiplayerButton"
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
                multiplayerButton.Visible = false;
                continueButton.Visible = false;
                abandonRunButton.Visible = false;
                compendiumButton.Visible = false;
                timelineButton.Visible = false;
                openProfileScreenButton.Visible = false;

                // Some buttons need this additional Enable()/Disable() call I'm honestly still not sure why this worked
                singleplayerButton.Enable();
                timelineButton.Disable();
                compendiumButton.Disable();

                // Change the name of "Single Player" for Archipelago
                singleplayerButton!.label!.Text = "Play";

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
            }
        }

        /// <summary>
        /// Creates an Archipelago Settings button by duplicating the vanilla
        /// Settings button and placing it immediately after Archipelago.
        ///
        /// Duplicating a vanilla button preserves the game's styling,
        /// sounds, animations, and controller behavior.
        /// </summary>
        private static void InjectArchipelagoSettingsButton(NMainMenu mainMenu)
        {
            // Avoid injecting a duplicate if _Ready is ever called again.
            if (mainMenu.GetNodeOrNull<NMainMenuTextButton>(ArchipelagoSettingsButtonPath) != null)
            {
                return;
            }

            // Grab references to the buttons we need to manipulate
            var singleplayerButton = mainMenu.GetNode<NMainMenuTextButton>(SingleplayerButtonPath);
            var settingsButton = mainMenu.GetNode<NMainMenuTextButton>(SettingsButtonPath);

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
            singleplayerButton.AddSibling(archipelagoSettingsButton);
            archipelagoSettingsButton.CustomMinimumSize = new Vector2(
                300f,
                archipelagoSettingsButton.CustomMinimumSize.Y
            );

            // Adjust button focusing
            var selfNodePath = new NodePath(".");
            archipelagoSettingsButton.FocusNeighborLeft = selfNodePath;
            archipelagoSettingsButton.FocusNeighborRight = selfNodePath;
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
                    var charSelectScreen =
                        MenuUtility.SubmenuStack.GetSubmenuType<NCharacterSelectScreen>();

                    charSelectScreen?.InitializeSingleplayer();
                    MenuUtility.SubmenuStack.Push(charSelectScreen!);
                }
                // Inject the custom Archipelago UIs if we're not connected
                else
                {
                    ArchipelagoConnectionUI.InjectUI();
                    ArchipelagoNotificationUI.InjectUI();
                }
            }
        }

        /// <summary>
        /// Normally, first time players skip straight to the Character Select
        /// screen after starting a single player run, but that step is where
        /// the connection UI now lives. This patches that behavior out.
        /// </summary>
        [HarmonyPatch(typeof(NMainMenu), nameof(NMainMenu.MethodName.SingleplayerButtonPressed))]
        public static class DisableSkippingToCharSelect
        {
            [HarmonyPrefix]
            public static bool Prefix(NMainMenu __instance, NButton _)
            {
                /// Always open the singleplayer submenu,
                /// regardless of NumberOfRuns.
                __instance.OpenSingleplayerSubmenu();
                return false;
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
            public static void Postfix(NSubmenuStack __instance)
            {
                // Only pop again if NCharacterSelectScreen was on top
                if (__instance.Peek() is NSingleplayerSubmenu)
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
                ArchipelagoCharTrackerUI.RemoveUI();
                ArchipelagoGoalTrackerUI.RemoveUI();
            }
        }

        #endregion
    }
}
