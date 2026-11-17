using Godot;
using MegaCrit.Sts2.Core.ControllerInput;

namespace StS2AP.UI;

/// <summary>
/// Lets the mod's plain Godot buttons be pressed in keyboard-only mode and with a controller.
/// </summary>
internal static class ApButtonInput
{
    /// <summary>
    /// The game presses its own buttons on its select action (Space in keyboard-only mode, A on a
    /// controller) and leaves Godot's <c>ui_accept</c> unbound, so a plain <see cref="Button"/> could
    /// be focused with the arrow keys but never pressed. This presses <paramref name="button"/> when
    /// select, or confirm (Enter) unless <paramref name="confirm"/> is false, is released while it
    /// has focus. Pressing on release, like the game's buttons, keeps the key-up from reaching
    /// whatever the press opens.
    /// </summary>
    internal static T PressOnGameInput<T>(T button, bool confirm = true) where T : BaseButton
    {
        bool held = false;
        button.GuiInput += inputEvent =>
        {
            if (!inputEvent.IsAction(MegaInput.select) && !(confirm && inputEvent.IsAction(MegaInput.confirm)))
                return;

            // Keep the key from also reaching the game's hotkeys on the screen underneath.
            button.AcceptEvent();
            if (inputEvent.IsPressed())
            {
                if (!inputEvent.IsEcho())
                    held = !button.Disabled;
                return;
            }

            if (!held)
                return;
            held = false;
            if (!button.Disabled)
                button.EmitSignal(BaseButton.SignalName.Pressed);
        };
        button.FocusExited += () => held = false;
        return button;
    }
}
