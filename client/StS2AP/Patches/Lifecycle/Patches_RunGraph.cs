using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Screens.GameOverScreen;
using MegaCrit.Sts2.Core.Nodes.Screens.RunHistoryScreen;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Runs.History;
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

        [HarmonyPatch(typeof(RunManager), nameof(RunManager.Launch))]
        public static class BeginRunGraph
        {
            [HarmonyPostfix]
            public static void Postfix(RunState __result) => ApRunGraphRecorder.BeginRun(__result);
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

                    var button = CreateGraphButton(() => Open(__instance, run.StartTime, run.MapPointHistory));
                    button.SetAnchorsPreset(Control.LayoutPreset.BottomLeft);
                    button.OffsetLeft = 40f;
                    button.OffsetTop = -120f;
                    // Appear with the main-menu button, once the run summary has finished animating.
                    var mainMenu = __instance._mainMenuButton;
                    button.Visible = mainMenu.Visible;
                    mainMenu.VisibilityChanged += () =>
                    {
                        if (GodotObject.IsInstanceValid(button))
                            button.Visible = mainMenu.Visible;
                    };
                    __instance._uiNode.AddChild(button);
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
                            Open(__instance, history.StartTime, history.MapPointHistory);
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
                Text = "AP Run Graph",
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

        private static void Open(Control host, long startTime, List<List<MapPointHistoryEntry>> mapHistory)
        {
            var log = ApRunGraphRecorder.Store.Load(startTime, out string? error);
            if (log == null)
            {
                LogUtility.Warn(error ?? $"No run graph log exists for run {startTime}.");
                NotificationUtility.ShowRawText("The Archipelago graph for this run could not be read.");
                return;
            }
            ApRunGraphOverlay.Show(host, mapHistory, log);
        }
    }
}
