using System.Text.Json.Nodes;
using Godot;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Runs.History;
using MegaCrit.Sts2.Core.Saves;
using StS2AP.Utils;
using StS2AP.Utils.RunGraph;
using static StS2AP.UI.RunGraph.RunGraphStyle;

namespace StS2AP.UI.RunGraph;

/// <summary>
/// Main-menu list of every AP run with a graph log on this profile, grouped by Archipelago seed.
/// Each run card opens the same recap as the game-over screen and can be exported or deleted.
/// </summary>
internal sealed partial class ApPastRunsOverlay : Control
{
    private const string EmptyText =
        "No AP runs recorded yet. Finish a run while connected to Archipelago and it will appear here.";

    private readonly ApRunGraphStore _store = ApRunGraphRecorder.Store;
    private List<SeedGroup> _seeds = new();
    private string? _selectedSeed;
    private bool _showRestarts;
    private Label _subtitle = null!;
    private VBoxContainer _seedList = null!;
    private VBoxContainer _detail = null!;
    private Button _close = null!;

    /// <summary>
    /// A logged run with the game's saved history for it, and how many locations the slot gained
    /// during it. Unfinished runs never reached a game-over screen, so the game saved no history;
    /// their recap comes from the last snapshot of the run, read only when a card needs it.
    /// </summary>
    private sealed record PastRun(ApRunGraphLog Log, RunHistory? History, int SlotGain, ApPastRunStatus Status)
    {
        private readonly Lazy<SerializableRun?> _snapshot = new(() => ApRunGraphRecorder.LoadSnapshot(Log.StartTime));

        internal int ChecksSent => Log.Events.Count(e => e.Kind == ApRunGraphEventKind.CheckSent);
        internal bool Unfinished => History == null;
        internal SerializableRun? Snapshot => Unfinished ? _snapshot.Value : null;
        internal IReadOnlyList<IReadOnlyList<MapPointHistoryEntry>>? MapHistory =>
            History?.MapPointHistory ?? Snapshot?.MapPointHistory;
    }

    /// <summary>The runs played on one AP seed, newest first.</summary>
    private sealed record SeedGroup(string Seed, List<PastRun> Runs)
    {
        internal ApRunGraphLog Newest => Runs[0].Log;
        internal IEnumerable<PastRun> Finished => Runs.Where(r => r.Status == ApPastRunStatus.Finished);
        /// <summary>Every run with its own card: finished ones and unfinished ones worth a recap.</summary>
        internal IEnumerable<PastRun> WithCards => Runs.Where(r => r.Status != ApPastRunStatus.Restart);
        internal List<PastRun> Restarts => Runs.Where(r => r.Status == ApPastRunStatus.Restart).ToList();
        internal int Checked => Runs.Max(r => r.Log.SlotChecksAtStart + r.SlotGain);
        internal int Total => Math.Max(Checked, Runs.Max(r => r.Log.SlotLocationCount));
    }

    private static ApPastRunsOverlay? _open;

    internal static void Show(Control host)
    {
        if (_open != null && IsInstanceValid(_open))
            _open.QueueFree();
        var overlay = new ApPastRunsOverlay { Name = "ApPastRunsOverlay" };
        overlay.Build();
        _open = overlay;
        ApOverlayLayer.Show(overlay, host, overlay.QueueFree);
    }

    private void Build()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop;

        var dim = new ColorRect { Color = Backdrop, MouseFilter = MouseFilterEnum.Stop };
        dim.SetAnchorsPreset(LayoutPreset.FullRect);
        dim.GuiInput += input =>
        {
            if (input is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left })
                QueueFree();
        };
        AddChild(dim);

        var panel = new PanelContainer();
        panel.SetAnchorsPreset(LayoutPreset.Center);
        panel.OffsetLeft = -900; panel.OffsetRight = 900;
        panel.OffsetTop = -500; panel.OffsetBottom = 500;
        var panelStyle = Box(Page, PanelBorder, 16, 32f, borderWidth: 2);
        panelStyle.ShadowColor = new Color(0f, 0f, 0f, 0.6f);
        panelStyle.ShadowSize = 28;
        panel.AddThemeStyleboxOverride("panel", panelStyle);
        AddChild(panel);

        var root = new VBoxContainer();
        root.AddThemeConstantOverride("separation", 24);
        panel.AddChild(root);
        root.AddChild(BuildHeader());

        var body = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        body.AddThemeConstantOverride("separation", 24);
        root.AddChild(body);

        var seedScroll = new ScrollContainer
        {
            CustomMinimumSize = new Vector2(340f, 0f),
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        _seedList = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _seedList.AddThemeConstantOverride("separation", 12);
        seedScroll.AddChild(_seedList);
        body.AddChild(seedScroll);

        _detail = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _detail.AddThemeConstantOverride("separation", 16);
        body.AddChild(_detail);

        Reload();
        Ready += () => _close.GrabFocus();
    }

    private Control BuildHeader()
    {
        var header = new HBoxContainer();
        header.AddThemeConstantOverride("separation", 12);
        var titles = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        titles.AddChild(Label("Past AP Runs", 36, Text, bold: true));
        _subtitle = Label("", 16, TextMuted);
        titles.AddChild(_subtitle);
        header.AddChild(titles);

        var exportAll = ActionButton("Export all");
        exportAll.TooltipText = "Write every run into one JSON file, for charts across runs.";
        exportAll.Pressed += ExportAll;
        header.AddChild(exportAll);
        var openFolder = ActionButton("Open folder");
        openFolder.TooltipText = "Open the folder that holds the run logs, exports and saved images.";
        openFolder.Pressed += () => ApRunGraphFiles.ShowInFileManager(_store.RootDirectory);
        header.AddChild(openFolder);
        // Escape and the controller's back button close it through ApOverlayLayer.
        _close = ActionButton("Close  (Esc)");
        _close.Pressed += QueueFree;
        header.AddChild(_close);
        return header;
    }

    /// <summary>Reads every log and its history again, keeping the selected seed when it still exists.</summary>
    private void Reload()
    {
        var logs = LoadLogs().ToList();
        var histories = logs.ToDictionary(log => log.StartTime, log => LoadHistory(log.StartTime));
        var statuses = ApPastRunRules.Classify(logs, log => histories[log.StartTime] != null);
        _seeds = logs
            .GroupBy(log => log.Seed)
            .Select(group => new SeedGroup(group.Key, WithSlotGains(group, histories, statuses)
                .OrderByDescending(r => r.Log.StartTime).ToList()))
            .OrderByDescending(group => group.Newest.StartTime)
            .ToList();
        if (_selectedSeed == null || _seeds.All(s => s.Seed != _selectedSeed))
            _selectedSeed = _seeds.FirstOrDefault()?.Seed;

        int finished = _seeds.Sum(seed => seed.Finished.Count());
        int unfinished = _seeds.Sum(seed => seed.WithCards.Count()) - finished;
        int restarts = _seeds.Sum(seed => seed.Restarts.Count);
        var parts = new List<string> { $"{finished} finished {(finished == 1 ? "run" : "runs")}" };
        if (unfinished > 0)
            parts.Add($"{unfinished} unfinished");
        if (restarts > 0)
            parts.Add($"{restarts} {(restarts == 1 ? "restart" : "restarts")}");
        _subtitle.Text = logs.Count == 0
            ? "Every Archipelago run recorded on this profile"
            : string.Join(", ", parts.Take(parts.Count - 1)) + (parts.Count > 1 ? " and " : "") + parts[^1]
                + $" on this profile, across {_seeds.Count} Archipelago {(_seeds.Count == 1 ? "seed" : "seeds")}";
        ShowSeeds();
        ShowSelectedSeed();
    }

    /// <summary>
    /// Pairs one seed's logs with their histories and works out what each run added to the slot.
    /// A finished run recorded the slot's count at its end. An unfinished one did not, so the next
    /// run's starting count stands in; for the newest run, its own distinct checks sent do.
    /// </summary>
    private static IEnumerable<PastRun> WithSlotGains(
        IEnumerable<ApRunGraphLog> logs,
        IReadOnlyDictionary<long, RunHistory?> histories,
        IReadOnlyDictionary<long, ApPastRunStatus> statuses)
    {
        var ordered = logs.OrderBy(log => log.StartTime).ToList();
        for (int i = 0; i < ordered.Count; i++)
        {
            var log = ordered[i];
            int end = log.SlotChecksAtEnd
                ?? (i + 1 < ordered.Count
                    ? ordered[i + 1].SlotChecksAtStart
                    : log.SlotChecksAtStart + log.Events
                        .Where(e => e.Kind == ApRunGraphEventKind.CheckSent && e.LocationId != null)
                        .Select(e => e.LocationId)
                        .Distinct()
                        .Count());
            yield return new PastRun(log, histories[log.StartTime], Math.Max(0, end - log.SlotChecksAtStart),
                statuses[log.StartTime]);
        }
    }

    private void ShowSeeds()
    {
        Clear(_seedList);
        _seedList.AddChild(Caps("Seeds", 12, TextMuted, spacing: 3));
        foreach (var seed in _seeds)
            _seedList.AddChild(SeedButton(seed));
    }

    private Control SeedButton(SeedGroup seed)
    {
        bool selected = seed.Seed == _selectedSeed;
        var button = new Button { FocusMode = FocusModeEnum.All, CustomMinimumSize = new Vector2(0f, 136f) };
        var normal = Box(selected ? Card : PanelColor, selected ? Gold : PanelBorder, 14, 0f, borderWidth: 2);
        button.AddThemeStyleboxOverride("normal", normal);
        button.AddThemeStyleboxOverride("hover", Box(Card, selected ? Gold : Progression, 14, 0f, borderWidth: 2));
        button.AddThemeStyleboxOverride("focus", Box(Card, Progression, 14, 0f, borderWidth: 2));
        button.AddThemeStyleboxOverride("pressed", normal);
        button.Pressed += () =>
        {
            if (_selectedSeed != seed.Seed)
                _showRestarts = false;
            _selectedSeed = seed.Seed;
            ShowSeeds();
            ShowSelectedSeed();
        };

        // The button draws the frame; the text sits on top and lets clicks through.
        var margin = new MarginContainer { MouseFilter = MouseFilterEnum.Ignore };
        margin.SetAnchorsPreset(LayoutPreset.FullRect);
        foreach (string side in new[] { "left", "right", "top", "bottom" })
            margin.AddThemeConstantOverride($"margin_{side}", 16);
        var box = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        box.AddThemeConstantOverride("separation", 8);
        margin.AddChild(box);
        button.AddChild(margin);

        var top = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        var name = Label($"Seed …{ApRunRecapView.Tail(seed.Seed)}", 20, Text, bold: true);
        name.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        top.AddChild(name);
        top.AddChild(Label(LocalDate(seed.Newest.StartTime).ToString("MMM d"), 14, TextMuted));
        box.AddChild(top);
        int wins = seed.Runs.Count(r => r.Log.Outcome == ApRunOutcome.Won);
        int cards = seed.WithCards.Count();
        string winText = wins == 0 ? "no wins yet" : wins == 1 ? "1 win" : $"{wins} wins";
        string runsText = cards > 0
            ? $"{cards} {(cards == 1 ? "run" : "runs")} · {winText}"
            : $"{seed.Restarts.Count} {(seed.Restarts.Count == 1 ? "restart" : "restarts")}";
        var summary = Label($"{seed.Newest.SlotName} · {runsText}", 14, TextMuted);
        summary.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        summary.ClipText = true;
        box.AddChild(summary);
        float fraction = seed.Total > 0 ? (float)seed.Checked / seed.Total : 0f;
        box.AddChild(new RecapBar(new[] { (1f, Gold) }, fraction, 8f));
        box.AddChild(Label($"{seed.Checked} of {seed.Total} locations checked", 14, Text));
        foreach (var child in box.GetChildren().OfType<Control>())
            child.MouseFilter = MouseFilterEnum.Ignore;
        return button;
    }

    private void ShowSelectedSeed()
    {
        Clear(_detail);
        var seed = _seeds.FirstOrDefault(s => s.Seed == _selectedSeed);
        if (seed == null)
        {
            var empty = Label(EmptyText, 20, TextMuted);
            empty.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            _detail.AddChild(empty);
            return;
        }

        _detail.AddChild(SeedStrip(seed));

        var scroll = new ScrollContainer
        {
            SizeFlagsVertical = SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        var column = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        column.AddThemeConstantOverride("separation", 16);
        scroll.AddChild(column);
        _detail.AddChild(scroll);

        var grid = new GridContainer { Columns = 3, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        grid.AddThemeConstantOverride("h_separation", 14);
        grid.AddThemeConstantOverride("v_separation", 14);
        foreach (var run in seed.WithCards)
            grid.AddChild(RunCard(run));
        if (grid.GetChildCount() > 0)
            column.AddChild(grid);
        else
            column.AddChild(Label("No runs on this seed got past a restart yet.", 16, TextMuted));

        var restarts = seed.Restarts;
        if (restarts.Count > 0)
            column.AddChild(RestartFold(restarts));
    }

    /// <summary>
    /// Unfinished runs that a later run of the same character replaced before floor
    /// <see cref="ApPastRunRules.KeepReplacedFromFloor"/>, folded into one row: most are restarts
    /// to reroll the first rewards. They stay as data for a future seed overview.
    /// </summary>
    private Control RestartFold(List<PastRun> runs)
    {
        var panel = ApRunRecapView.Panel(PanelColor with { A = 0.6f }, PanelBorder, 14, 16f);
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 12);
        panel.AddChild(box);

        var header = new HBoxContainer();
        header.AddThemeConstantOverride("separation", 14);
        var text = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ShrinkCenter };
        text.AddThemeConstantOverride("separation", 2);
        text.AddChild(Label($"{runs.Count} {(runs.Count == 1 ? "restart" : "restarts")}", 19, Text, bold: true));
        int sent = runs.Sum(run => run.ChecksSent);
        text.AddChild(Label($"Replaced by a later run before floor {ApPastRunRules.KeepReplacedFromFloor} · {sent} checks sent", 14, TextMuted));
        header.AddChild(text);

        var toggle = ActionButton(_showRestarts ? "Hide" : "Show");
        toggle.CustomMinimumSize = new Vector2(110f, 42f);
        toggle.Pressed += () =>
        {
            _showRestarts = !_showRestarts;
            ShowSelectedSeed();
        };
        header.AddChild(toggle);
        var deleteAll = CreateDeleteButton(runs.Select(run => run.Log).ToList(), "Delete all",
            "Delete the AP logs of every restart on this seed. Runs with their own card are kept.");
        deleteAll.CustomMinimumSize = new Vector2(130f, 42f);
        header.AddChild(deleteAll);
        box.AddChild(header);

        if (_showRestarts)
        {
            foreach (var run in runs)
                box.AddChild(RestartRow(run));
        }
        return panel;
    }

    private Control RestartRow(PastRun run)
    {
        var log = run.Log;
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 12);
        var character = CharacterOf(run);
        Color color = character?.NameColor ?? TextMuted;
        row.AddChild(ApRunRecapView.CharacterIcon(character, Pretty(log.Character), color, 38f));
        row.AddChild(Centered(Label(character?.Title.GetFormattedText() ?? Pretty(log.Character), 16, Text, bold: true), 150f));
        row.AddChild(Centered(Label(LocalDate(log.StartTime).ToString("MMM d · HH:mm"), 14, TextMuted), 130f));
        row.AddChild(Centered(Label($"floor {run.Log.FurthestFloor}", 14, TextMuted), 80f));
        var checks = Label(run.ChecksSent == 0 ? "no checks" : $"{run.ChecksSent} checks", 14, run.ChecksSent == 0 ? TextFaint : Gold);
        checks.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        row.AddChild(Centered(checks, 0f));
        var export = ActionButton("Export");
        export.CustomMinimumSize = new Vector2(96f, 38f);
        export.TooltipText = "Write this run's AP log into a JSON file.";
        export.Pressed += () => ExportRun(log);
        row.AddChild(export);
        var delete = CreateDeleteButton(new[] { log }, "Delete", "Delete this run's AP log.");
        delete.CustomMinimumSize = new Vector2(96f, 38f);
        row.AddChild(delete);
        return row;
    }

    private static Label Centered(Label label, float width)
    {
        label.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        label.CustomMinimumSize = new Vector2(width, 0f);
        return label;
    }

    /// <summary>How each run moved the slot, oldest first, one colored segment per run.</summary>
    private static Control SeedStrip(SeedGroup seed)
    {
        var panel = ApRunRecapView.Panel(PanelColor, PanelBorder, 14, 18f);
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 10);
        panel.AddChild(box);

        var heading = new HBoxContainer();
        heading.AddThemeConstantOverride("separation", 12);
        heading.AddChild(Label($"Seed …{ApRunRecapView.Tail(seed.Seed)}", 20, Text, bold: true));
        var caption = Label("how each run moved the slot, oldest first", 14, TextMuted);
        caption.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        caption.SizeFlagsVertical = SizeFlags.ShrinkEnd;
        heading.AddChild(caption);
        heading.AddChild(Label($"{seed.Checked} / {seed.Total}", 14, TextMuted));
        box.AddChild(heading);

        var parts = Enumerable.Reverse(seed.Runs)
            .Where(run => run.SlotGain > 0)
            .Select(run => ((float)run.SlotGain, RunColor(run)))
            .ToArray();
        float fill = seed.Total > 0 ? parts.Sum(p => p.Item1) / seed.Total : 0f;
        box.AddChild(new RecapBar(parts, fill, 22f));
        return panel;
    }

    private Control RunCard(PastRun run)
    {
        var log = run.Log;
        bool won = log.Outcome == ApRunOutcome.Won;
        var card = ApRunRecapView.Panel(PanelColor, won ? Gold.Darkened(0.45f) : PanelBorder, 14, 16f);
        card.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 12);
        card.AddChild(box);

        var character = CharacterOf(run);
        Color color = RunColor(run);
        var top = new HBoxContainer();
        top.AddThemeConstantOverride("separation", 12);
        top.AddChild(ApRunRecapView.CharacterIcon(character, Pretty(log.Character), color));
        var names = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        names.AddThemeConstantOverride("separation", 3);
        var nameRow = new HBoxContainer();
        nameRow.AddThemeConstantOverride("separation", 8);
        nameRow.AddChild(Label(character?.Title.GetFormattedText() ?? Pretty(log.Character), 19, Text, bold: true));
        nameRow.AddChild(OutcomePill(run));
        names.AddChild(nameRow);
        var detail = Label(RunDetail(run), 13, TextMuted);
        detail.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        detail.ClipText = true;
        names.AddChild(detail);
        top.AddChild(names);
        top.AddChild(Label(LocalDate(log.StartTime).ToString("MMM d · HH:mm"), 13, TextMuted));
        box.AddChild(top);

        if (run.MapHistory != null)
            box.AddChild(new HpSparkline(HpSeries(run), won ? Gold : TextMuted));

        var stats = new HBoxContainer();
        stats.AddThemeConstantOverride("separation", 16);
        int floors = run.MapHistory?.Sum(act => act.Count) ?? log.SavedFloor ?? 0;
        stats.AddChild(Label(floors > 0 ? $"{floors} floors" : "no floors saved", 14, TextMuted));
        long? seconds = run.History != null ? (long)run.History.RunTime : run.Snapshot?.RunTime;
        if (seconds != null)
            stats.AddChild(Label(ApRunRecapView.FormatTime(seconds.Value), 14, TextMuted));
        var checks = Label($"{run.ChecksSent} checks", 14, Gold);
        checks.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        stats.AddChild(checks);
        int undone = log.Events.Count(e => e.Kind == ApRunGraphEventKind.CheckSent && e.Abandoned);
        if (undone > 0)
            stats.AddChild(Label($"{undone} undone", 14, TextMuted));
        box.AddChild(stats);

        var sent = log.Events.Where(e => e.Kind == ApRunGraphEventKind.CheckSent).ToList();
        var parts = ApRunRecap.TypeOrder
            .Select(type => ((float)sent.Count(e => !e.Abandoned && e.Classification == type), ForItem(type)))
            .Append(((float)undone, PanelBorder.Lightened(0.15f)))
            .Where(part => part.Item1 > 0f)
            .ToArray();
        box.AddChild(new RecapBar(parts, parts.Length > 0 ? 1f : 0f, 8f));

        var buttons = new HBoxContainer();
        buttons.AddThemeConstantOverride("separation", 8);
        var open = ActionButton("Open recap", Gold.Darkened(0.55f), Gold);
        open.CustomMinimumSize = new Vector2(0f, 42f);
        open.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        open.Pressed += () => OpenRecap(run);
        if (run.MapHistory == null)
        {
            // Logs from before snapshots were kept, or a snapshot that could not be read.
            open.Disabled = true;
            open.TooltipText = "The game saved nothing for this run that a recap can be drawn from.";
        }
        buttons.AddChild(open);
        var export = ActionButton("Export");
        export.CustomMinimumSize = new Vector2(96f, 42f);
        export.TooltipText = "Write this run's AP log and the game's history for it into one JSON file.";
        export.Pressed += () => ExportRun(run.Log);
        buttons.AddChild(export);
        buttons.AddChild(CreateDeleteButton(new[] { run.Log }, "Delete", run.Unfinished
            ? "Delete this run's AP log and snapshot. Its checkpoints are kept."
            : "Delete this run's AP log. The game's own Run History keeps the run."));
        box.AddChild(buttons);
        return card;
    }

    private static Control OutcomePill(PastRun run)
    {
        var (text, color) = run.Status switch
        {
            ApPastRunStatus.Finished when run.Log.Outcome == ApRunOutcome.Won => ("Victory", Gold),
            ApPastRunStatus.Finished => ("Defeat", DeathText),
            ApPastRunStatus.InProgress => ("In progress", Progression),
            _ => ("Unfinished", TextMuted),
        };
        var pill = ApRunRecapView.Panel(color with { A = 0.16f }, color with { A = 0.16f }, 999, 2f);
        var style = (StyleBoxFlat)pill.GetThemeStylebox("panel");
        style.ContentMarginLeft = style.ContentMarginRight = 9f;
        pill.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        pill.AddChild(Caps(text, 11, color));
        return pill;
    }

    private static string RunDetail(PastRun run)
    {
        var history = run.History;
        if (history == null)
        {
            int? floor = run.Snapshot?.FloorReached ?? run.Log.SavedFloor;
            string saved = floor is { } f ? $"Last saved on floor {f}" : "Never saved";
            return run.Status == ApPastRunStatus.InProgress ? saved : $"{saved}, then replaced";
        }
        if (history.Win)
            return $"Beat the final act, Ascension {history.Ascension}";
        int floors = history.MapPointHistory.Sum(act => act.Count);
        string killedBy = ApRunRecapData.KilledByName(history);
        return killedBy.Length > 0 ? $"Killed by {killedBy}, floor {floors}" : $"Ended on floor {floors}";
    }

    /// <summary>The run's character, from the game's history or, for an unfinished run, from AP's log.</summary>
    private static CharacterModel? CharacterOf(PastRun run)
    {
        var player = run.History?.Players.FirstOrDefault(p => p.Id == run.Log.LocalNetId) ?? run.History?.Players.FirstOrDefault();
        if (player == null)
            return ApCampaignUi.FindCharacter(run.Log.Character);
        return player.Character == ModelId.none ? null : SaveUtil.CharacterOrDeprecated(player.Character);
    }

    private static Color RunColor(PastRun run) =>
        run.Log.Outcome == ApRunOutcome.Won ? Gold : CharacterOf(run)?.NameColor ?? TextMuted;

    /// <summary>HP as a fraction of max HP at each floor, read straight from the saved history.</summary>
    private static IReadOnlyList<float> HpSeries(PastRun run)
    {
        if (run.MapHistory == null)
            return Array.Empty<float>();
        return run.MapHistory
            .SelectMany(act => act)
            .Select(entry => entry.PlayerStats.FirstOrDefault(p => p.PlayerId == run.Log.LocalNetId) ?? entry.PlayerStats.FirstOrDefault())
            .Select(stats => stats == null || stats.MaxHp <= 0 ? 0f : Math.Clamp((float)stats.CurrentHp / stats.MaxHp, 0f, 1f))
            .ToList();
    }

    private void OpenRecap(PastRun run)
    {
        if (run.History is { } history)
            ApRunRecapOverlay.Open(this, run.Log.StartTime, log => ApRunRecapData.FromHistory(history, log));
        else if (run.Snapshot is { } snapshot)
            ApRunRecapOverlay.Open(this, run.Log.StartTime,
                log => ApRunRecapData.FromRun(snapshot, log, inProgress: run.Status == ApPastRunStatus.InProgress));
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

    /// <summary>The game saves a run's history as "{StartTime}.run" once the run ends.</summary>
    private static RunHistory? LoadHistory(long startTime)
    {
        try
        {
            var history = SaveManager.Instance.LoadRunHistory($"{startTime}.run");
            return history.Success ? history.SaveData : null;
        }
        catch (Exception ex)
        {
            LogUtility.Warn($"Could not read the game's history for run {startTime}: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// The first press arms the button and the second deletes, so a stray click cannot lose a
    /// run. Only AP's logs are deleted; finished runs stay in the game's own Run History.
    /// </summary>
    private Button CreateDeleteButton(IReadOnlyList<ApRunGraphLog> logs, string idleText, string tooltip)
    {
        var delete = ActionButton(idleText);
        delete.CustomMinimumSize = new Vector2(96f, 42f);
        delete.TooltipText = tooltip;
        bool armed = false;

        void Disarm()
        {
            armed = false;
            delete.Text = idleText;
            delete.AddThemeStyleboxOverride("normal", Box(Card, PanelBorder, 10, 8f));
        }

        delete.Pressed += () =>
        {
            if (!armed)
            {
                armed = true;
                delete.Text = idleText + "?";
                delete.AddThemeStyleboxOverride("normal", Box(Death.Darkened(0.35f), Death, 10, 8f));
                return;
            }
            foreach (var log in logs)
            {
                try
                {
                    _store.Delete(log.StartTime);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    LogUtility.Warn($"Could not delete run graph log {_store.PathFor(log.StartTime)}: {ex.Message}");
                    NotificationUtility.ShowRawText("Couldn't delete a run's log: " + ex.Message);
                    break;
                }
            }
            // Rebuilt after this press has finished, since the rebuild frees this button.
            Callable.From(Reload).CallDeferred();
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
        ApRunGraphFiles.ShowInFileManager(path);
    }

    /// <summary>The game's history for a finished run, as the JSON it saves; null for an unfinished one.</summary>
    private static string? LoadRunHistoryJson(long startTime)
    {
        var history = SaveManager.Instance.LoadRunHistory($"{startTime}.run");
        return history.Success && history.SaveData != null
            ? JsonSerializationUtility.ToJson(history.SaveData)
            : null;
    }

    private static DateTimeOffset LocalDate(long startTime) => DateTimeOffset.FromUnixTimeSeconds(startTime).ToLocalTime();

    private static string Pretty(string character) => string.IsNullOrEmpty(character)
        ? "Unknown"
        : char.ToUpperInvariant(character[0]) + character[1..].ToLowerInvariant();

    private static void Clear(Node container)
    {
        foreach (var child in container.GetChildren())
        {
            container.RemoveChild(child);
            child.QueueFree();
        }
    }
}

/// <summary>A run's HP across its floors as a small line, for the run cards.</summary>
internal sealed partial class HpSparkline : Control
{
    internal HpSparkline(IReadOnlyList<float> hp, Color color)
    {
        CustomMinimumSize = new Vector2(0f, 46f);
        MouseFilter = MouseFilterEnum.Ignore;
        Draw += () =>
        {
            DrawStyleBox(RunGraphStyle.Box(new Color(1f, 1f, 1f, 0.03f), new Color(1f, 1f, 1f, 0.03f), 6, 0f), new Rect2(Vector2.Zero, Size));
            if (hp.Count < 2)
                return;
            const float inset = 4f;
            float width = Size.X - inset * 2f;
            float height = Size.Y - inset * 2f;
            var points = hp
                .Select((value, i) => new Vector2(inset + i * width / (hp.Count - 1), inset + (1f - value) * height))
                .ToArray();
            DrawPolyline(points, color, 2f, antialiased: true);
        };
    }
}
