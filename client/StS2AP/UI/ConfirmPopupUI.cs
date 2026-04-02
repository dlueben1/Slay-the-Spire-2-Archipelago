using Godot;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Screens.ScreenContext;

namespace StS2AP.UI
{
    public abstract class ConfirmPopup : Control, IScreenContext
    {

        public Control? DefaultFocusedControl => null;

        protected NVerticalPopup _verticalPopup;

        protected abstract LocString Header { get; }
        protected abstract LocString Body { get; }
        protected virtual LocString YesString => new LocString("main_menu_ui", "GENERIC_POPUP.confirm");

        protected virtual LocString NoString => new LocString("main_menu_ui", "GENERIC_POPUP.cancel");

        
        public override void _Ready()
        {
            _verticalPopup = new NVerticalPopup();
            _verticalPopup.SetText(Header, Body);
            _verticalPopup.InitYesButton(YesString, OnYesButtonPressed);
            _verticalPopup.InitNoButton(NoString, OnNoButtonPressed);
        }

        private void OnYesTemplate(NButton button)
        {
            OnYesButtonPressed(button);
            this.QueueFreeSafely();
        }

        protected abstract void OnYesButtonPressed(NButton button);


        private void OnNoTemplate(NButton button)
        {
            OnNoButtonPressed(button);
            this.QueueFreeSafely();
        }

        protected abstract void OnNoButtonPressed(NButton button);
    }
}
