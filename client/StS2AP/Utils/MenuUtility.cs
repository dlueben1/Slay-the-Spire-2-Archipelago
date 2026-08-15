using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
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
        public static NMainMenuSubmenuStack SubmenuStack { get; set; }

        /// <summary>
        /// Initializes and opens the shared single-player character-select screen.
        /// The main menu reuses one screen instance, so pushing it while it is
        /// already on top would leave duplicate stack entries sharing one lobby.
        /// </summary>
        /// <returns><c>true</c> when the screen was pushed; otherwise <c>false</c>.</returns>
        public static void OpenCharacterSelect()
        {
            if (SubmenuStack.Peek() is NCharacterSelectScreen)
            {
                LogUtility.Warn(
                    "Ignored a request to open character select because it is already active."
                );
            }

            var characterSelect = SubmenuStack.GetSubmenuType<NCharacterSelectScreen>();
            characterSelect.InitializeSingleplayer();
            SubmenuStack.Push(characterSelect);
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
