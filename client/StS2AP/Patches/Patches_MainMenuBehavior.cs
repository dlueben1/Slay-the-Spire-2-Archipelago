using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using StS2AP.UI;
using StS2AP.Utils;

namespace StS2AP.Patches
{
    /// <summary>
    /// Patches for `NMainMenu` and all of its related submenus.
    /// Used primarily to reconfigure the UI for Archipelago, as well as injecting our custom Archipelago Connection UI.
    /// </summary>
    public static class Patches_MainMenuBehavior
    {
        /// <summary>
        /// Changes the main menu UI for the Archipelago Mod.
        /// This includes hiding and renaming menu options.
        /// </summary>
        [HarmonyPatch(typeof(NMainMenu), nameof(NMainMenu._Ready), [])]
        public static class ReconfigureMainMenu
        {
            [HarmonyPostfix]
            public static void Postfix(NMainMenu __instance)
            {
                // Grab reference to the menu stack
                MenuUtility.SubmenuStack = __instance.SubmenuStack;

                // Grab the single player button that we will refactor into "Archipelago"
                var _singleplayerButton = __instance.GetNode<NMainMenuTextButton>("MainMenuTextButtons/SingleplayerButton");

                // Grab references to all the buttons we shouldn't have
                var _continueButton = __instance.GetNode<NMainMenuTextButton>("MainMenuTextButtons/ContinueButton");
                var _multiplayerButton = __instance.GetNode<NMainMenuTextButton>("MainMenuTextButtons/MultiplayerButton");
                var _abandonRunButton = __instance.GetNode<NMainMenuTextButton>("MainMenuTextButtons/AbandonRunButton");
                var _compendiumButton = __instance.GetNode<NMainMenuTextButton>("MainMenuTextButtons/CompendiumButton");
                var _timelineButton = __instance.GetNode<NMainMenuTextButton>("MainMenuTextButtons/TimelineButton");
                var _openProfileScreenButton = __instance.GetNode<NOpenProfileScreenButton>("%ChangeProfileButton");

                // Shutting the linter up
                if (_singleplayerButton.label == null) return;

                // Set the menu for Archipelago
                _singleplayerButton.Visible = true;
                _singleplayerButton.Enable();
                _multiplayerButton.Visible = false;
                _continueButton.Visible = false;
                _abandonRunButton.Visible = false;
                _compendiumButton.Visible = false;
                _timelineButton.Visible = false;
                _timelineButton.Disable();
                _compendiumButton.Disable();
                _openProfileScreenButton.Visible = false;

                // Change the name of the "Single Player" menu to "Archipelago"
                _singleplayerButton.label.Text = "Archipelago";
            }
        }

        /// <summary>
        /// Adds the Archipelago UI to the main menu.
        /// Injected when the user selects "Single Player".
        /// </summary>
        [HarmonyPatch(typeof(NMainMenu), nameof(NMainMenu.OpenSingleplayerSubmenu), [])]
        public static class InjectAPMenu
        {
            [HarmonyPostfix]
            public static void Postfix(NSingleplayerSubmenu __result)
            {
                // Hide the actual sub-menu options
                var _standardButton = __result.GetNode<NSubmenuButton>("StandardButton");
                var _dailyButton = __result.GetNode<NSubmenuButton>("DailyButton");
                var _customButton = __result.GetNode<NSubmenuButton>("CustomRunButton");
                var _backButton = __result.GetNode<NBackButton>("BackButton");
                _standardButton.Visible = false;
                _dailyButton.Visible = false;
                _customButton.Visible = false;
                _backButton.Visible = false;

                // If we are connected, dive directly into the game
                if (ArchipelagoClient.IsConnected)
                {
                    var _charSelectScreen = MenuUtility.SubmenuStack.GetSubmenuType<NCharacterSelectScreen>();
                    _charSelectScreen?.InitializeSingleplayer();
                    MenuUtility.SubmenuStack.Push(_charSelectScreen);
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
        /// Normally, first time players skip straight to the Character Select screen after starting a single player run,
        /// but that step is where the connection UI now lives. This patches that behavior out of the game.
        /// </summary>
        [HarmonyPatch(typeof(NMainMenu), nameof(NMainMenu.MethodName.SingleplayerButtonPressed))]
        public static class DisableSkippingToCharSelect
        {
            [HarmonyPrefix]
            public static bool Prefix(NMainMenu __instance, NButton _)
            {
                // Always open the singleplayer submenu, regardless of NumberOfRuns
                __instance.OpenSingleplayerSubmenu();
                return false;
            }
        }

        /// <summary>
        /// Hides the Back Button from the Character Select Screen
        /// </summary>
        [HarmonyPatch(typeof(NCharacterSelectScreen))]
        public static class NCharacterSelectScreenPatches
        {
            [HarmonyPatch("_Ready")]
            [HarmonyPostfix]
            static void HideBackButtonOnCharSelectScreen(NCharacterSelectScreen __instance)
            {
                __instance.GetNode<NBackButton>("BackButton").Visible = false;
            }
        }

        /// <summary>
        /// Injects the Archipelago Progress Tracker panel when the Character Select screen opens,
        /// and removes it when the screen closes. Keeping injection in OnSubmenuOpened (rather than
        /// _Ready) ensures the CanvasLayer is created after the scene tree is fully set up.
        /// </summary>
        [HarmonyPatch(typeof(NCharacterSelectScreen))]
        public static class CharTrackerPanelPatches
        {
            /// <summary>Show the tracker panels as soon as the screen becomes active.</summary>
            [HarmonyPatch(nameof(NCharacterSelectScreen.OnSubmenuOpened))]
            [HarmonyPostfix]
            static void OnOpened(NCharacterSelectScreen __instance)
            {
                // Find the first character on the screen
                Control charButtonContainer = __instance.GetNode<Control>("CharSelectButtons/ButtonContainer");
                NCharacterSelectButton firstButton = charButtonContainer.GetChild<NCharacterSelectButton>(0);
                CharacterModel character = firstButton.Character;

                // Setup the character tracker UI
                ArchipelagoCharTrackerUI.InjectUI(character);

                // Setup the goal tracker UI (initial goal text needs to be ever-so-slightly delayed or the text is TINY)
                ArchipelagoGoalTrackerUI.InjectUI();
                var sceneTree = Engine.GetMainLoop() as SceneTree;
                if (sceneTree != null)
                {
                    sceneTree.CreateTimer(0.2f).Timeout += () =>
                    {
                        ArchipelagoGoalTrackerUI.UpdateGoalProgress();
                    };
                }
            }

            /// <summary>Remove the tracker panels when the player leaves the Character Select screen.</summary>
            [HarmonyPatch(nameof(NCharacterSelectScreen.OnSubmenuClosed))]
            [HarmonyPostfix]
            static void OnClosed(NCharacterSelectScreen __instance)
            {
                ArchipelagoCharTrackerUI.RemoveUI();
                ArchipelagoGoalTrackerUI.RemoveUI();
            }
        }
    }
}