using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Screens.GameOverScreen;
using MegaCrit.Sts2.Core.Nodes.Screens.RunHistoryScreen;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Runs.History;
using MegaCrit.Sts2.Core.Saves;
using StS2AP.UI;
using StS2AP.UI.RunGraph;
using StS2AP.Utils;
using StS2AP.Utils.RunGraph;

namespace StS2AP.Patches
{
    /// <summary>
    /// Records each AP run for the end-of-run graph and adds an "Archipelago" button that opens it
    /// from the game-over screen and from Run History.
    /// </summary>
    public static class Patches_RunGraph
    {
        private const string ButtonName = "ApRunGraphButton";
        private const string ApIconPath = "res://images/APIcon.png";
        private const double AutoOpenDelaySeconds = 1.5;

        [HarmonyPatch(typeof(RunManager), nameof(RunManager.Launch))]
        public static class BeginRunGraph
        {
            [HarmonyPostfix]
            public static void Postfix(RunState __result) => ApRunGraphRecorder.BeginRun(__result);
        }

        /// <summary>
        /// Save and quit, a disconnect, or any other way out of a run that is not a game over ends
        /// in CleanUp. A prefix, while the run still exists.
        /// </summary>
        [HarmonyPatch(typeof(RunManager), nameof(RunManager.CleanUp))]
        public static class SnapshotAtQuit
        {
            [HarmonyPrefix]
            public static void Prefix() => ApRunGraphRecorder.CaptureAtQuit();
        }

        [HarmonyPatch(typeof(NRun), nameof(NRun.ShowGameOverScreen))]
        public static class FinishRunGraph
        {
            // A prefix, so the outcome is saved before the game-over screen can open the graph.
            [HarmonyPrefix]
            public static void Prefix(NRun __instance) =>
                ApRunGraphRecorder.FinishRun(__instance._state?.CurrentRoom?.IsVictoryRoom ?? false);
        }

        [HarmonyPatch(typeof(NGameOverScreen), nameof(NGameOverScreen._Ready))]
        public static class GameOverButton
        {
            [HarmonyPostfix]
            public static void Postfix(NGameOverScreen __instance)
            {
                try
                {
                    var run = __instance._serializableRun;
                    if (run == null || !ApRunGraphRecorder.Store.Exists(run.StartTime))
                        return;

                    void OpenRecap() => ApRunRecapOverlay.Open(__instance, run.StartTime, log => RecapAtGameOver(run, log));
                    var button = CreateGraphButton(OpenRecap);
                    button.SetAnchorsPreset(Control.LayoutPreset.BottomLeft);
                    button.OffsetLeft = 40f;
                    button.OffsetTop = -120f;
                    __instance._uiNode.AddChild(button);

                    // The recap opens by itself once the defeat or victory banner has played; closing
                    // it leaves the game's own screen, and the button brings it back.
                    var timer = __instance.GetTree().CreateTimer(AutoOpenDelaySeconds);
                    timer.Timeout += () =>
                    {
                        if (GodotObject.IsInstanceValid(__instance) && __instance.IsInsideTree() && !ApRunRecapOverlay.IsOpen)
                            OpenRecap();
                    };
                }
                catch (Exception ex)
                {
                    LogUtility.Error($"Could not add the run graph button to the game-over screen: {ex}");
                }
            }
        }

        [HarmonyPatch(typeof(NRunHistory), nameof(NRunHistory._Ready))]
        public static class RunHistoryButton
        {
            [HarmonyPostfix]
            public static void Postfix(NRunHistory __instance)
            {
                try
                {
                    if (__instance.GetNodeOrNull(ButtonName) != null)
                        return;
                    var button = CreateGraphButton(() =>
                    {
                        var history = __instance._history;
                        if (history != null)
                            ApRunRecapOverlay.Open(__instance, history.StartTime, log => ApRunRecapData.FromHistory(history, log));
                    });
                    button.SetAnchorsPreset(Control.LayoutPreset.BottomRight);
                    button.OffsetLeft = -300f;
                    button.OffsetTop = -120f;
                    button.Visible = false;
                    __instance.AddChild(button);
                }
                catch (Exception ex)
                {
                    LogUtility.Error($"Could not add the run graph button to Run History: {ex}");
                }
            }
        }

        [HarmonyPatch(typeof(NRunHistory), nameof(NRunHistory.DisplayRun))]
        public static class RunHistoryButtonVisibility
        {
            [HarmonyPostfix]
            public static void Postfix(NRunHistory __instance, RunHistory history)
            {
                if (__instance.GetNodeOrNull<Button>(ButtonName) is { } button)
                    button.Visible = ApRunGraphRecorder.Store.Exists(history.StartTime);
            }
        }

        private static Button CreateGraphButton(Action open)
        {
            var button = new Button
            {
                Name = ButtonName,
                Text = "AP Run Recap",
                FocusMode = Control.FocusModeEnum.All,
                CustomMinimumSize = new Vector2(250f, 60f),
                ExpandIcon = true,
            };
            if (ResourceLoader.Exists(ApIconPath))
                button.Icon = ResourceLoader.Load<Texture2D>(ApIconPath);
            button.AddThemeConstantOverride("icon_max_width", 34);
            button.AddThemeConstantOverride("h_separation", 10);
            button.AddThemeStyleboxOverride("normal", RunGraphStyle.Box(RunGraphStyle.Card, RunGraphStyle.Progression, 12, 10f, borderWidth: 2));
            button.AddThemeStyleboxOverride("hover", RunGraphStyle.Box(RunGraphStyle.Card.Lightened(0.15f), RunGraphStyle.Progression.Lightened(0.2f), 12, 10f, borderWidth: 3));
            button.AddThemeStyleboxOverride("focus", RunGraphStyle.Box(RunGraphStyle.Card.Lightened(0.15f), RunGraphStyle.Progression.Lightened(0.2f), 12, 10f, borderWidth: 3));
            button.AddThemeStyleboxOverride("pressed", RunGraphStyle.Box(RunGraphStyle.Card.Darkened(0.2f), RunGraphStyle.Progression, 12, 10f, borderWidth: 2));
            button.AddThemeFontOverride("font", RunGraphStyle.Bold);
            button.AddThemeFontSizeOverride("font_size", 21);
            button.AddThemeColorOverride("font_color", RunGraphStyle.Text);
            button.AddThemeColorOverride("font_hover_color", RunGraphStyle.Text);
            button.Pressed += open;

            // A slow brightness pulse so the button is noticed on a busy end-of-run screen.
            // The tween belongs to the button and is freed with it.
            button.Ready += () =>
            {
                var pulse = button.CreateTween().SetLoops();
                pulse.TweenProperty(button, "self_modulate", new Color(1.35f, 1.35f, 1.35f), 0.9)
                    .SetTrans(Tween.TransitionType.Sine);
                pulse.TweenProperty(button, "self_modulate", Colors.White, 0.9)
                    .SetTrans(Tween.TransitionType.Sine);
            };
            return button;
        }

        /// <summary>
        /// Prefers the saved history, which names what killed the player; the live run stands in
        /// when the game has not written it.
        /// </summary>
        private static ApRunRecapData RecapAtGameOver(SerializableRun run, ApRunGraphLog log)
        {
            var history = SaveManager.Instance.LoadRunHistory($"{run.StartTime}.run");
            return history.Success && history.SaveData != null
                ? ApRunRecapData.FromHistory(history.SaveData, log)
                : ApRunRecapData.FromRun(run, log);
        }
    }
}
