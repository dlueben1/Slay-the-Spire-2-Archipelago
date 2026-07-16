using System;
using System.Linq;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.addons.mega_text;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Screens.PauseMenu;
using StS2AP.Utils;

namespace StS2AP.Patches
{
    /// <summary>
    /// Patches for <see cref="NPauseMenu"/> and its pause-menu interactions.
    /// Used primarily to inject and maintain the custom Archipelago Settings
    /// button alongside the vanilla pause-menu controls.
    /// </summary>
    public static class Patches_PauseMenuBehavior
    {
        #region Clone Target References

        // The path that StS2 stores pause-menu buttons in.
        private const string ButtonContainerPath = "%ButtonContainer";

        // The subpath to the vanilla Settings button.
        private const string SettingsButtonPath = ButtonContainerPath + "/Settings";

        // The subpath to the vanilla Give Up button.
        private const string GiveUpButtonPath = ButtonContainerPath + "/GiveUp";

        // The new name of our injected Archipelago Settings button.
        private const string PauseMenuArchipelagoSettingsButtonName = "ArchipelagoSettingsButton";

        #endregion

        #region Pause Menu Patches

        /// <summary>
        /// Adds the Archipelago Settings button when the pause menu is initialized.
        /// </summary>
        [HarmonyPatch(typeof(NPauseMenu), nameof(NPauseMenu._Ready), [])]
        public static class InjectArchipelagoSettingsIntoPauseMenu
        {
            [HarmonyPostfix]
            public static void Postfix(NPauseMenu __instance)
            {
                TryAttachArchipelagoSettingsButton(__instance);
            }
        }

        /// <summary>
        /// Ensures the Archipelago Settings button is present and correctly
        /// configured whenever the pause menu is reopened.
        /// </summary>
        [HarmonyPatch(typeof(NPauseMenu), nameof(NPauseMenu.OnSubmenuOpened), [])]
        public static class RefreshArchipelagoSettingsPauseButton
        {
            [HarmonyPostfix]
            public static void Postfix(NPauseMenu __instance)
            {
                TryAttachArchipelagoSettingsButton(__instance);
            }
        }

        #endregion

        #region Helper Functions

        /// <summary>
        /// Safely adds or refreshes the Archipelago Settings pause-menu button.
        /// </summary>
        private static void TryAttachArchipelagoSettingsButton(NPauseMenu pauseMenu)
        {
            try
            {
                AttachArchipelagoSettingsButton(pauseMenu);
            }
            catch (Exception exception)
            {
                LogUtility.Error(
                    $"Failed to attach Archipelago Settings button to the pause menu. {exception}"
                );
            }
        }

        /// <summary>
        /// Duplicates the vanilla Settings button, changes its behavior, and
        /// inserts it immediately before Give Up.
        /// </summary>
        private static void AttachArchipelagoSettingsButton(NPauseMenu pauseMenu)
        {
            // Grab the existing settings button
            var settingsButton = pauseMenu.GetNodeOrNull<NPauseMenuButton>(SettingsButtonPath);
            if (settingsButton?.GetParent() is not Control buttonContainer)
            {
                LogUtility.Warn("Could not find the Settings button container on NPauseMenu.");

                return;
            }

            // Rename the button to "Game Settings"
            var settingsLabel = settingsButton.GetNodeOrNull<MegaLabel>("Label");
            if (settingsLabel != null)
            {
                settingsLabel.SetTextAutoSize("Game Settings");
            }

            // OnSubmenuOpened can run repeatedly, so refresh an existing button rather than injecting another copy if possible
            var existingButton =
                buttonContainer.FindChild(
                    PauseMenuArchipelagoSettingsButtonName,
                    recursive: false,
                    owned: false
                ) as NPauseMenuButton;
            if (existingButton != null)
            {
                existingButton.Visible = true;
                existingButton.Enable();

                var existingLabel = existingButton.GetNodeOrNull<MegaLabel>("Label");

                if (existingLabel != null)
                {
                    existingLabel.SetTextAutoSize("Archipelago Settings");
                }

                RewirePauseMenuFocus(buttonContainer);
                return;
            }

            /// Intentionally do not include DuplicateFlags.Signals.
            /// Otherwise, the clone could retain the vanilla Settings button's
            /// Released callback in addition to our custom callback.
            const Node.DuplicateFlags duplicateFlags =
                Node.DuplicateFlags.Groups
                | Node.DuplicateFlags.Scripts
                | Node.DuplicateFlags.UseInstantiation;

            // Create our new cloned button (the Archipelago Settings button)
            if (settingsButton.Duplicate((int)duplicateFlags) is not NPauseMenuButton button)
            {
                LogUtility.Warn(
                    "Failed to duplicate the pause-menu Settings button as an NPauseMenuButton."
                );

                return;
            }

            // Setup the new button
            button.Name = PauseMenuArchipelagoSettingsButtonName;
            button.Visible = true;
            button.Enable();
            var label = button.GetNodeOrNull<MegaLabel>("Label");
            if (label == null)
            {
                LogUtility.Warn(
                    "The duplicated pause-menu button does not contain its expected Label node."
                );

                button.QueueFree();
                return;
            }
            label.SetTextAutoSize("Archipelago Settings");

            // Remove all original click callbacks before registering ours
            GodotUtility.DisconnectSignalConnections(button, NClickableControl.SignalName.Released);

            // Add our custom click callback to open the Archipelago Settings menu
            button.Connect(
                NClickableControl.SignalName.Released,
                Callable.From<NButton>(_ =>
                {
                    MenuUtility.OpenArchipelagoSettings();
                })
            );

            // Inject it into the Pause Menu next to the Game Settings
            buttonContainer.AddChild(button);
            var insertIndex = settingsButton.GetIndex();
            insertIndex = Math.Min(insertIndex, buttonContainer.GetChildCount() - 1);
            buttonContainer.MoveChild(button, insertIndex);
            RewirePauseMenuFocus(buttonContainer);
        }

        /// <summary>
        /// Rebuilds vertical controller-navigation links after adding the button.
        /// </summary>
        private static void RewirePauseMenuFocus(Control buttonContainer)
        {
            var visibleButtons = buttonContainer
                .GetChildren()
                .OfType<NButton>()
                .Where(button => button.Visible)
                .ToList();

            for (var index = 0; index < visibleButtons.Count; index++)
            {
                var currentButton = visibleButtons[index];
                var previousButton = index > 0 ? visibleButtons[index - 1] : currentButton;
                var nextButton =
                    index < visibleButtons.Count - 1 ? visibleButtons[index + 1] : currentButton;

                // This menu navigates vertically. Prevent horizontal focus from unexpectedly jumping to another control.
                currentButton.FocusNeighborLeft = currentButton.GetPath();
                currentButton.FocusNeighborRight = currentButton.GetPath();
                currentButton.FocusNeighborTop = previousButton.GetPath();
                currentButton.FocusNeighborBottom = nextButton.GetPath();
            }
        }

        #endregion
    }
}
