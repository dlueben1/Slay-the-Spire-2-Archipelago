using System.Text.Json.Nodes;
using Godot;
using MegaCrit.Sts2.Core.Saves;
using StS2AP.Utils;
using StS2AP.Utils.RunGraph;
using static StS2AP.UI.RunGraph.RunGraphStyle;

namespace StS2AP.UI.RunGraph;

/// <summary>
/// Main-menu list of every AP run with a graph log on this profile, newest first. Each row opens
/// the same graph as the game-over screen, drawn from the game's saved run history, and can be
/// exported or deleted.
/// </summary>
internal sealed partial class ApPastRunsOverlay : Control
{
    private const string NodeName = "ApPastRunsOverlay";
    private const string EmptyText =
        "No AP runs recorded yet. Finish a run while connected to Archipelago and it will appear here.";

    private readonly ApRunGraphStore _store = ApRunGraphRecorder.Store;
    private VBoxContainer _list = null!;

    internal static void Show(Control host)
    {
        host.GetNodeOrNull(NodeName)?.QueueFree();
        var overlay = new ApPastRunsOverlay { Name = NodeName };
        overlay.Build();
        host.AddChild(overlay);
    }

    private void Build()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop;
        ZIndex = 100;

        var dim = new ColorRect { Color = Backdrop, MouseFilter = MouseFilterEnum.Stop };
        dim.SetAnchorsPreset(LayoutPreset.FullRect);
        dim.GuiInput += input =>
        {
            if (input is InputEventMouseButton { Pressed: true })
                QueueFree();
        };
        AddChild(dim);

        var panel = new PanelContainer();
        panel.SetAnchorsPreset(LayoutPreset.Center);
        panel.OffsetLeft = -640; panel.OffsetRight = 640;
        panel.OffsetTop = -440; panel.OffsetBottom = 440;
        var panelStyle = Box(PanelColor, PanelBorder, 16, 26f, borderWidth: 2);
        panelStyle.ShadowColor = new Color(0f, 0f, 0f, 0.6f);
        panelStyle.ShadowSize = 28;
        panel.AddThemeStyleboxOverride("panel", panelStyle);
        AddChild(panel);

        var root = new VBoxContainer();
        root.AddThemeConstantOverride("separation", 16);
        panel.AddChild(root);

        var header = new HBoxContainer();
        var titles = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        titles.AddChild(Label("Past AP Runs", 34, Text, bold: true));
        titles.AddChild(Label("Every Archipelago run recorded on this profile, newest first", 18, TextMuted));
        header.AddChild(titles);
        header.AddThemeConstantOverride("separation", 12);
        var exportAll = CreateButton("Export all", Card, PanelBorder);
        exportAll.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        exportAll.TooltipText = "Write every run below into one JSON file, for charts across runs.";
        exportAll.Pressed += ExportAll;
        header.AddChild(exportAll);
        var openFolder = CreateButton("Open folder", Card, PanelBorder);
        openFolder.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        openFolder.TooltipText = "Open the folder that holds the run logs and exports.";
        openFolder.Pressed += () => OpenFolder(_store.RootDirectory);
        header.AddChild(openFolder);
        var close = CreateButton("Close  (Esc)", Card, PanelBorder);
        close.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        close.Shortcut = new Shortcut { Events = new Godot.Collections.Array { new InputEventAction { Action = "ui_cancel" } } };
        close.Pressed += QueueFree;
        header.AddChild(close);
        root.AddChild(header);

        var scroll = new ScrollContainer
        {
            SizeFlagsVertical = SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        _list = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _list.AddThemeConstantOverride("separation", 10);
        scroll.AddChild(_list);
        root.AddChild(scroll);

        Control? firstFocus = null;
        foreach (ApRunGraphLog log in LoadLogs())
        {
            var (row, open) = BuildRow(log);
            _list.AddChild(row);
            if (!open.Disabled)
                firstFocus ??= open;
        }
        ShowEmptyTextIfNoRuns();

        // Focus the first row, or Close, so a controller can use the list.
        Control focus = firstFocus ?? close;
        Ready += () => focus.GrabFocus();
    }

    private IEnumerable<ApRunGraphLog> LoadLogs()
    {
        foreach (long startTime in _store.ListStartTimes())
        {
            var log = _store.Load(startTime, out string? error);
            if (log != null)
                yield return log;
            else if (error != null)
                LogUtility.Warn(error);
        }
    }

    private void ShowEmptyTextIfNoRuns()
    {
        if (_list.GetChildren().Any(child => !child.IsQueuedForDeletion()))
            return;
        var empty = Label(EmptyText, 20, TextMuted);
        empty.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _list.AddChild(empty);
    }

    private (PanelContainer Row, Button Open) BuildRow(ApRunGraphLog log)
    {
        var row = new PanelContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        var style = Box(Card, PanelBorder, 12, 14f);
        // The left edge shows the result at a glance.
        style.BorderColor = log.Outcome switch
        {
            ApRunOutcome.Won => Filler,
            ApRunOutcome.Lost => Death,
            _ => TextMuted,
        };
        style.BorderWidthLeft = 4;
        style.BorderWidthTop = style.BorderWidthRight = style.BorderWidthBottom = 0;
        row.AddThemeStyleboxOverride("panel", style);

        var line = new HBoxContainer();
        line.AddThemeConstantOverride("separation", 24);
        row.AddChild(line);

        var who = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        string outcome = log.Outcome switch
        {
            ApRunOutcome.Won => "Victory",
            ApRunOutcome.Lost => "Defeated",
            _ => "Unfinished",
        };
        who.AddChild(Label($"{PrettyCharacter(log.Character)} · {outcome}", 22, Text, bold: true));
        string when = DateTimeOffset.FromUnixTimeSeconds(log.StartTime).ToLocalTime().ToString("g");
        who.AddChild(Label($"{when} · {log.SlotName}", 15, TextMuted));
        line.AddChild(who);

        var sent = log.Events.Where(e => e.Kind == ApRunGraphEventKind.CheckSent && !e.Abandoned).ToList();
        line.AddChild(Figure($"{sent.Count}", "checks sent", Filler));
        line.AddChild(Figure($"{sent.Count(e => e.Classification == ApRunGraphItemType.Progression)}", "progression", Progression));
        line.AddChild(Figure($"{log.Events.Count(e => e.Kind == ApRunGraphEventKind.ItemClaimed)}", "claimed", Claim));

        var open = CreateButton("View graph", Card.Lightened(0.08f), Progression);
        open.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        open.Pressed += () => OpenGraph(log);
        // The game only saves a run's history when it ends, and the graph is drawn from that history.
        if (log.Outcome == null)
        {
            open.Text = "Not finished";
            open.Disabled = true;
            open.TooltipText = "This run never reached a game-over screen, so the game saved no history to draw.";
            open.AddThemeColorOverride("font_disabled_color", TextMuted);
            open.AddThemeStyleboxOverride("disabled", Box(Card, PanelBorder, 10, 8f));
        }
        line.AddChild(open);

        var export = CreateButton("Export", Card, PanelBorder);
        export.CustomMinimumSize = new Vector2(110f, 46f);
        export.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        export.TooltipText = "Write this run's AP log and the game's history for it into one JSON file.";
        export.Pressed += () => ExportRun(log);
        line.AddChild(export);

        line.AddChild(CreateDeleteButton(row, log));
        return (row, open);
    }

    /// <summary>
    /// The first press arms the button and the second deletes, so a stray click cannot lose a
    /// run. Only AP's log is deleted; the run stays in the game's own Run History.
    /// </summary>
    private Button CreateDeleteButton(PanelContainer row, ApRunGraphLog log)
    {
        const string idleText = "\u00D7";
        var delete = CreateButton(idleText, Card, PanelBorder);
        delete.CustomMinimumSize = new Vector2(56f, 46f);
        delete.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        delete.TooltipText = "Delete this run's AP log. The game's own Run History keeps the run.";
        var armedStyle = Box(Death.Darkened(0.35f), Death, 10, 8f);
        bool armed = false;

        void Disarm()
        {
            armed = false;
            delete.Text = idleText;
            delete.CustomMinimumSize = new Vector2(56f, 46f);
            delete.RemoveThemeStyleboxOverride("normal");
            delete.AddThemeStyleboxOverride("normal", Box(Card, PanelBorder, 10, 8f));
        }

        delete.Pressed += () =>
        {
            if (!armed)
            {
                armed = true;
                delete.Text = "Delete?";
                delete.CustomMinimumSize = new Vector2(110f, 46f);
                delete.AddThemeStyleboxOverride("normal", armedStyle);
                return;
            }
            try
            {
                _store.Delete(log.StartTime);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                LogUtility.Warn($"Could not delete run graph log {_store.PathFor(log.StartTime)}: {ex.Message}");
                NotificationUtility.ShowRawText("Couldn't delete that run's log: " + ex.Message);
                Disarm();
                return;
            }
            row.QueueFree();
            ShowEmptyTextIfNoRuns();
        };
        delete.FocusExited += Disarm;
        delete.MouseExited += Disarm;
        return delete;
    }

    private void ExportRun(ApRunGraphLog log)
    {
        string fileName = $"{log.StartTime}-{log.Character.ToLowerInvariant()}.json";
        Export(fileName, () => ApRunGraphStore.ExportBundle(log, LoadRunHistoryJson(log.StartTime)));
    }

    private void ExportAll()
    {
        var logs = LoadLogs().ToList();
        if (logs.Count == 0)
        {
            NotificationUtility.ShowRawText("There are no AP runs to export yet.");
            return;
        }
        string fileName = $"all-runs-{DateTimeOffset.Now.ToUnixTimeSeconds()}.json";
        Export(fileName, () => ApRunGraphStore.ExportAllBundle(
            logs.Select(log => ApRunGraphStore.ExportBundle(log, LoadRunHistoryJson(log.StartTime)))));
    }

    private void Export(string fileName, Func<JsonObject> buildBundle)
    {
        string path;
        try
        {
            path = _store.WriteExport(fileName, buildBundle());
        }
        catch (Exception ex)
        {
            LogUtility.Error($"Run graph export failed: {ex}");
            NotificationUtility.ShowRawText("Export failed: " + ex.Message);
            return;
        }
        LogUtility.Info($"Exported run graph data to {path}");
        NotificationUtility.ShowRawText($"Exported to {path}", timeout: 8);
        OpenFolder(path);
    }

    /// <summary>The game's history for a finished run, as the JSON it saves; null for an unfinished one.</summary>
    private static string? LoadRunHistoryJson(long startTime)
    {
        var history = SaveManager.Instance.LoadRunHistory($"{startTime}.run");
        return history.Success && history.SaveData != null
            ? JsonSerializationUtility.ToJson(history.SaveData)
            : null;
    }

    /// <summary>Shows a file in the platform's file manager, or opens a folder.</summary>
    private static void OpenFolder(string path)
    {
        try
        {
            bool isFile = File.Exists(path);
            if (!isFile)
                Directory.CreateDirectory(path);
            // Godot uses Explorer on Windows and the platform's file manager elsewhere.
            Error opened = isFile
                ? OS.ShellShowInFileManager(path, openFolder: false)
                : OS.ShellOpen(path);
            if (opened != Error.Ok && isFile)
                opened = OS.ShellOpen(Path.GetDirectoryName(path)!);
            if (opened != Error.Ok)
                NotificationUtility.ShowRawText($"Couldn't open the folder automatically. It is at {path}", timeout: 8);
        }
        catch (Exception ex)
        {
            LogUtility.Warn("Could not open run graph folder: " + ex.Message);
            NotificationUtility.ShowRawText($"Couldn't open the folder automatically. It is at {path}", timeout: 8);
        }
    }

    private static VBoxContainer Figure(string value, string caption, Color accent)
    {
        var box = new VBoxContainer { CustomMinimumSize = new Vector2(110f, 0f) };
        box.AddThemeConstantOverride("separation", 0);
        var number = Label(value, 26, accent, bold: true);
        number.HorizontalAlignment = HorizontalAlignment.Center;
        box.AddChild(number);
        var label = Label(caption, 14, TextMuted);
        label.HorizontalAlignment = HorizontalAlignment.Center;
        box.AddChild(label);
        return box;
    }

    private void OpenGraph(ApRunGraphLog log)
    {
        // The game saves a run's history as "{StartTime}.run" once the run ends.
        var history = SaveManager.Instance.LoadRunHistory($"{log.StartTime}.run");
        if (!history.Success || history.SaveData == null)
        {
            NotificationUtility.ShowRawText("The game has no saved history for this run, so its graph can't be drawn. Unfinished runs have none yet.");
            return;
        }
        ApRunGraphOverlay.Show(this, history.SaveData.MapPointHistory, log);
    }

    private static string PrettyCharacter(string character) => string.IsNullOrEmpty(character)
        ? "Unknown character"
        : char.ToUpperInvariant(character[0]) + character[1..].ToLowerInvariant();

    private static Button CreateButton(string text, Color background, Color border)
    {
        var button = new Button
        {
            Text = text,
            FocusMode = FocusModeEnum.All,
            CustomMinimumSize = new Vector2(170f, 46f),
        };
        button.AddThemeStyleboxOverride("normal", Box(background, border, 10, 8f));
        button.AddThemeStyleboxOverride("hover", Box(background.Lightened(0.12f), Progression, 10, 8f));
        button.AddThemeStyleboxOverride("focus", Box(background.Lightened(0.12f), Progression, 10, 8f));
        button.AddThemeStyleboxOverride("pressed", Box(background.Darkened(0.2f), Progression, 10, 8f));
        button.AddThemeFontOverride("font", Bold);
        button.AddThemeFontSizeOverride("font_size", 19);
        button.AddThemeColorOverride("font_color", Text);
        button.AddThemeColorOverride("font_hover_color", Text);
        return button;
    }
}
