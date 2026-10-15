using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Multiplayer;

namespace StS2AP.UI
{
    
    public class ConfirmPopup
    {

        public LocString? Header { get; set; }
        public LocString? Body { get; set; }
        public LocString YesString { get; set; } = new LocString("main_menu_ui", "GENERIC_POPUP.confirm");

        public LocString NoString { get; set; } = new LocString("main_menu_ui", "GENERIC_POPUP.cancel");

        public Action<bool>? ButtonPressed;


        public NGenericPopup? Popup { get; } = NGenericPopup.Create();

        public void Show()
        {
            if (Header is not { } header
                || Body is not { } body
                || ButtonPressed is not { } buttonPressed
                || Popup is not { } popup)
            {
                LogUtility.Warn(
                    "Cannot show a confirmation popup before its content, callback, and game popup are available"
                );
                return;
            }
            NModalContainer.Instance!.Add(popup);
            var activePopup = NModalContainer.Instance!.OpenModal as NGenericPopup;
            if (activePopup == null)
            {
                LogUtility.Warn("Failed to get active popup from NModalContainer");
                return;
            }
            _ = ToCallback(activePopup, body, header, buttonPressed);
        }

        /// <summary>
        /// Handles the Button Pressed callback
        /// </summary>
        private async Task ToCallback(
            NGenericPopup popup,
            LocString body,
            LocString header,
            Action<bool> buttonPressed)
        {
            var result = await popup.WaitForConfirmation(body, header, NoString, YesString);
            buttonPressed(result);
        }
    }
}
