using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
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
