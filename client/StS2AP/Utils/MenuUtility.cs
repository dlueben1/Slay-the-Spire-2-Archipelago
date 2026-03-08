using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace StS2AP.Utils
{
    public static class MenuUtility
    {
        /// <summary>
        /// The Main Menu stack, keeps track of the different views a user can be in from the Main Menu.
        /// </summary>
        public static NMainMenuSubmenuStack SubmenuStack { get; set; }
    }
}
