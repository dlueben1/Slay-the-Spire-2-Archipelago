using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.ControllerInput;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Debug;

namespace StS2AP.Patches
{
    /// <summary>
    /// Opens the AP Loot menu with L3 when the game reads the controller without Steam Input.
    /// </summary>
    public static class Patches_ApLootControllerHotkey
    {
        /// <summary>
        /// With Steam Input, the game delivers L3 as a named action event, which the RitsuLib
        /// hotkey registered in <see cref="ModSettingsRegistration"/> matches. When Steam Input is
        /// off for the controller, the game falls back to Godot's own joypad input and L3 arrives
        /// as a raw joypad button that RitsuLib's action bindings never see. NInputManager is where
        /// the game turns that raw button into its own actions, so catch it there. Action events
        /// are skipped, as RitsuLib already handles those.
        /// </summary>
        [HarmonyPatch(typeof(NInputManager), nameof(NInputManager._UnhandledInput))]
        public static class OpenApLootFromRawStickPress
        {
            [HarmonyPostfix]
            public static void Postfix(NInputManager __instance, InputEvent inputEvent)
            {
                if (inputEvent is InputEventAction
                    || inputEvent.IsEcho()
                    || !inputEvent.IsActionPressed(Controller.lStickPress)
                    || !NGame.IsGameFocusedWindow()
                    || NDevConsole.IsConsoleVisible)
                {
                    return;
                }

                ModSettingsRegistration.ToggleApLootMenu();
                __instance.GetViewport()?.SetInputAsHandled();
            }
        }
    }
}
