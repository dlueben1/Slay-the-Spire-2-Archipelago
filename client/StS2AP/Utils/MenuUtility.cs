using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using STS2RitsuLib.Settings;

namespace StS2AP.Utils
{
    public static class MenuUtility
    {
        /// <summary>
        /// The Main Menu stack, keeps track of the different views a user can be in from the Main Menu.
        /// </summary>
        public static NMainMenuSubmenuStack? SubmenuStack { get; set; }

        /// <summary>The active main menu instance, used to resume a requested play flow after AP login.</summary>
        public static NMainMenu? MainMenu { get; set; }

        /// <summary>
        /// Initializes and opens the shared single-player character-select screen.
        /// The main menu reuses one screen instance, so pushing it while it is
        /// already on top would leave duplicate stack entries sharing one lobby.
        /// </summary>
        /// <returns><c>true</c> when the screen was pushed; otherwise <c>false</c>.</returns>
        public static void OpenCharacterSelect()
        {
            MultiplayerSupport.SelectDestination(ApPlayDestination.Singleplayer);
            Patches.Patches_ItemProcessor.ProcessDeferredItemsForSingleplayer();

            NMainMenuSubmenuStack? submenuStack = SubmenuStack;
            if (submenuStack == null)
            {
                LogUtility.Error("Cannot open character select: the main-menu stack is unavailable");
                return;
            }

            if (submenuStack.Peek() is NCharacterSelectScreen)
            {
                LogUtility.Warn(
                    "Ignored a request to open character select because it is already active."
                );
                return;
            }

            var characterSelect = submenuStack.GetSubmenuType<NCharacterSelectScreen>();
            characterSelect.InitializeSingleplayer();
            submenuStack.Push(characterSelect);
        }

        /// <summary>Continues through MegaCrit's unmodified multiplayer submenu.</summary>
        public static NMultiplayerSubmenu? OpenMultiplayer()
        {
            MultiplayerSupport.BeginMultiplayerEntry();
            if (!MultiplayerSupport.CanEnterMultiplayerLobby(out string blockedReason))
            {
                LogUtility.Warn($"Cannot open AP multiplayer lobby: {blockedReason}");
                NotificationUtility.ShowRawText(blockedReason);
                return null;
            }

            if (MainMenu == null)
            {
                LogUtility.Error("Cannot open multiplayer: the main menu is unavailable");
                return null;
            }

            return MainMenu.OpenMultiplayerSubmenu();
        }

        /// <summary>
        /// Opens RitsuLib's settings UI directly to the Archipelago mod page.
        /// </summary>
        public static void OpenArchipelagoSettings()
        {
            var result = ModSettingsNavigator.RequestOpenByIds(
                ModEntry.ModId,
                pageId: null,
                sectionId: null,
                entryId: null
            );

            if (!result.Success)
            {
                LogUtility.Warn(
                    $"Unable to open Archipelago settings. [{result.Code}] {result.Message}"
                );
            }
        }
    }
}
