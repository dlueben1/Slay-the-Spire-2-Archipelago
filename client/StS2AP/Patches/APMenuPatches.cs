using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Nodes.Screens.Settings;
using MegaCrit.Sts2.Core.Unlocks;
using StS2AP.UI;
using StS2AP.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace StS2AP.Patches
{
    /// <summary>
    /// Collection of Harmony patches related to configuring the menu for Archipelago
    /// </summary>
    public static class APMenuPatches
    {
        /// <summary>
        /// Changes the main menu UI so that "Single Player", "Multiplayer", and everything not necessary is hidden.
        /// </summary>
        [HarmonyPatch(typeof(NMainMenu), nameof(NMainMenu._Ready), new Type[] {})]
        public class ReconfigureMainMenuPatch
        {
            static void Postfix(NMainMenu __instance)
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

                // Shutting the linter up
                if (_singleplayerButton.label == null) return;
                
                // Set the menu for Archipelago
                _singleplayerButton.Visible = true;
                _multiplayerButton.Visible = false;
                _continueButton.Visible = false;
                _abandonRunButton.Visible = false;
                _compendiumButton.Visible = false;
                _timelineButton.Visible = false;

                // Change the name of the "Single Player" menu to "Archipelago"
                _singleplayerButton.label.Text = "Archipelago";
            }
        }

        /// <summary>
        /// Adds the Archipelago UI to the main menu.
        /// Injected when the user selects "Single Player".
        /// </summary>
        [HarmonyPatch(typeof(NMainMenu), nameof(NMainMenu.OpenSingleplayerSubmenu),
            new Type[] { })]
        public class InjectAPMenuPatch
        {
            static void Postfix(NSingleplayerSubmenu __result)
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

                // Inject the custom Archipelago Connection UI
                ArchipelagoConnectionUI.InjectUI();
            }
        }
    }
}
