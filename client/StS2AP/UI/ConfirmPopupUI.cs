using Godot;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Multiplayer;
using MegaCrit.Sts2.Core.Nodes.Screens.ScreenContext;
using System.Runtime.InteropServices;

namespace StS2AP.UI
{
    
    public class ConfirmPopup
    {

        public LocString Header { get; set; }
        public LocString Body { get; set; }
        public LocString YesString { get; set; } = new LocString("main_menu_ui", "GENERIC_POPUP.confirm");

        public LocString NoString { get; set; } = new LocString("main_menu_ui", "GENERIC_POPUP.cancel");

        public Action<bool> ButtonPressed;


        public NGenericPopup Popup { get; } = NGenericPopup.Create();

        public void Show()
        {
            if(Header == null || Body == null)
            {
                LogUtility.Warn("Someone didn't set stuff");
            }
            ToCallback(Popup);
        }

        private async void ToCallback(NGenericPopup popup)
        {
            var result = await popup.WaitForConfirmation(Body, Header, NoString, YesString);
            ButtonPressed(result);
        }
    }
}
