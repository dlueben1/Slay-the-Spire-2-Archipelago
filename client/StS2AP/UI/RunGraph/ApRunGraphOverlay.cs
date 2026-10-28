using Godot;
using MegaCrit.Sts2.Core.Runs.History;
using static StS2AP.UI.RunGraph.RunGraphStyle;

namespace StS2AP.UI.RunGraph;

/// <summary>
/// Full-screen overlay with the AP end-of-run graph. It is added on top of whichever screen opened
/// it (game over or Run History) and removes itself when closed.
/// </summary>
internal sealed partial class ApRunGraphOverlay : Control
{
    private const string NodeName = "ApRunGraphOverlay";
    private const float TooltipWidth = 360f;

    private IReadOnlyList<RunGraphFloor> _floors = Array.Empty<RunGraphFloor>();
    private Dictionary<int, List<ApRunGraphEvent>> _eventsByFloor = new();
    private PanelContainer _tooltip = null!;
    private VBoxContainer _tooltipLines = null!;

    internal static void Show(
        Control host,
        IReadOnlyList<IReadOnlyList<MapPointHistoryEntry>> mapHistory,
        ApRunGraphLog log)
    {
        host.GetNodeOrNull(NodeName)?.QueueFree();
        var overlay = new ApRunGraphOverlay { Name = NodeName };
        overlay.Build(mapHistory, log);
        host.AddChild(overlay);
    }

    private void Build(IReadOnlyList<IReadOnlyList<MapPointHistoryEntry>> mapHistory, ApRunGraphLog log)
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop;
        ZIndex = 100;

        var dim = new ColorRect { Color = Backdrop, MouseFilter = MouseFilterEnum.Stop };
        dim.SetAnchorsPreset(LayoutPreset.FullRect);
        // Clicking outside the panel closes the overlay.
        dim.GuiInput += input =>
        {
            if (input is InputEventMouseButton { Pressed: true })
                QueueFree();
        };
        AddChild(dim);

        var panel = new PanelContainer();
        panel.SetAnchorsPreset(LayoutPreset.Center);
        panel.OffsetLeft = -880; panel.OffsetRight = 880;
        panel.OffsetTop = -470; panel.OffsetBottom = 470;
        var panelStyle = Box(PanelColor, PanelBorder, 16, 26f, borderWidth: 2);
        panelStyle.ShadowColor = new Color(0f, 0f, 0f, 0.6f);
        panelStyle.ShadowSize = 28;
        panel.AddThemeStyleboxOverride("panel", panelStyle);
        AddChild(panel);

        var root = new VBoxContainer();
        root.AddThemeConstantOverride("separation", 18);
        panel.AddChild(root);

        root.AddChild(BuildHeader(log));
        root.AddChild(BuildStats(log));

        _floors = ApRunGraphCanvas.BuildFloors(mapHistory, log.LocalNetId);
        _eventsByFloor = ApRunGraphCanvas.GroupEvents(log, _floors.Count);
        var canvas = new ApRunGraphCanvas { SizeFlagsVertical = SizeFlags.ExpandFill };
        canvas.Load(_floors, _eventsByFloor, log.Outcome);
        canvas.HoveredFloorChanged += ShowTooltip;
        root.AddChild(canvas);

        root.AddChild(BuildLegend());

        // Added last so it draws above the panel; it ignores the mouse so hovering keeps working.
        _tooltip = new PanelContainer { Visible = false, MouseFilter = MouseFilterEnum.Ignore };
        _tooltip.CustomMinimumSize = new Vector2(TooltipWidth, 0f);
        _tooltip.AddThemeStyleboxOverride("panel", Box(TooltipBg, PanelBorder, 10, 14f));
        _tooltipLines = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        _tooltipLines.AddThemeConstantOverride("separation", 4);
        _tooltip.AddChild(_tooltipLines);
        AddChild(_tooltip);
    }

    private HBoxContainer BuildHeader(ApRunGraphLog log)
    {
        var header = new HBoxContainer();
        header.AddThemeConstantOverride("separation", 16);

        var titles = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        titles.AddThemeConstantOverride("separation", 0);
        titles.AddChild(Label(log.SlotName, 34, Text, bold: true));
        string outcome = log.Outcome switch
        {
            ApRunOutcome.Won => "Victory",
            ApRunOutcome.Lost => "Defeated",
            _ => "Run in progress",
        };
        string character = string.IsNullOrEmpty(log.Character)
            ? ""
            : char.ToUpperInvariant(log.Character[0]) + log.Character[1..].ToLowerInvariant() + " · ";
        titles.AddChild(Label($"{character}{outcome} · Archipelago run graph", 18, TextMuted));
        header.AddChild(titles);

        var close = new Button
        {
            Text = "Close  (Esc)",
            FocusMode = FocusModeEnum.All,
            SizeFlagsVertical = SizeFlags.ShrinkCenter,
            CustomMinimumSize = new Vector2(170f, 46f),
            Shortcut = new Shortcut { Events = new Godot.Collections.Array { new InputEventAction { Action = "ui_cancel" } } },
        };
        close.AddThemeStyleboxOverride("normal", Box(Card, PanelBorder, 10, 8f));
        close.AddThemeStyleboxOverride("hover", Box(Card.Lightened(0.12f), Progression, 10, 8f));
        close.AddThemeStyleboxOverride("focus", Box(Card.Lightened(0.12f), Progression, 10, 8f));
        close.AddThemeStyleboxOverride("pressed", Box(Card.Darkened(0.2f), Progression, 10, 8f));
        close.AddThemeFontOverride("font", Bold);
        close.AddThemeFontSizeOverride("font_size", 19);
        close.AddThemeColorOverride("font_color", Text);
        close.Pressed += QueueFree;
        header.AddChild(close);
        // Focus the button once in the tree so a controller can close the overlay.
        Ready += () => close.GrabFocus();
        return header;
    }

    private static HBoxContainer BuildStats(ApRunGraphLog log)
    {
        var sent = log.Events.Where(e => e.Kind == ApRunGraphEventKind.CheckSent).ToList();
        int reloaded = sent.Count(e => e.Abandoned);
        int progression = sent.Count(e => !e.Abandoned && e.Classification == ApRunGraphItemType.Progression);
        int claimed = log.Events.Count(e => e.Kind == ApRunGraphEventKind.ItemClaimed);
        int gold = log.Events.Where(e => e.Kind == ApRunGraphEventKind.GoldClaimed).Sum(e => e.Amount);

        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 16);
        row.AddChild(StatCard("Checks sent", $"{sent.Count - reloaded}",
            reloaded > 0 ? $"+{reloaded} on reloaded branches" : "this run", Filler));
        row.AddChild(StatCard("Progression sent", $"{progression}", "items others needed", Progression));
        row.AddChild(StatCard("Items claimed", $"{claimed}", gold > 0 ? $"plus {gold} AP gold" : "from the multiworld", Claim));
        row.AddChild(StatCard("Slot checks done", SlotProgress(log), SlotProgressDetail(log), Useful));
        return row;
    }

    private static PanelContainer StatCard(string caption, string value, string detail, Color accent)
    {
        var card = new PanelContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        var style = Box(Card, PanelBorder, 12, 14f);
        // A colored left edge ties each card to its color on the graph.
        style.BorderColor = accent;
        style.BorderWidthLeft = 4;
        style.BorderWidthTop = style.BorderWidthRight = style.BorderWidthBottom = 0;
        card.AddThemeStyleboxOverride("panel", style);

        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 2);
        box.AddChild(Label(caption, 15, TextMuted));
        box.AddChild(Label(value, 32, Text, bold: true));
        box.AddChild(Label(detail, 14, TextMuted));
        card.AddChild(box);
        return card;
    }

    private static string SlotProgress(ApRunGraphLog log)
    {
        if (log.SlotLocationCount <= 0)
            return "—";
        int done = log.SlotChecksAtEnd ?? log.SlotChecksAtStart;
        return $"{100.0 * done / log.SlotLocationCount:0}%";
    }

    private static string SlotProgressDetail(ApRunGraphLog log)
    {
        if (log.SlotLocationCount <= 0)
            return "";
        string start = $"{100.0 * log.SlotChecksAtStart / log.SlotLocationCount:0}%";
        return log.SlotChecksAtEnd is { } end
            ? $"up from {start} ({end - log.SlotChecksAtStart} new)"
            : $"{start} when the run began";
    }

    private static HBoxContainer BuildLegend()
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 8);
        void Add(RunGraphMarker marker, Color color, string text)
        {
            row.AddChild(new RunGraphSwatch(marker, color));
            row.AddChild(Label(text, 15, TextMuted));
            row.AddChild(new Control { CustomMinimumSize = new Vector2(8f, 0f) });
        }
        void Heading(string text)
        {
            row.AddChild(Label(text, 15, Text, bold: true));
            row.AddChild(new Control { CustomMinimumSize = new Vector2(2f, 0f) });
        }

        // Pips are colored by what the checked location held for whoever receives it.
        Heading("Checks you sent:");
        Add(RunGraphMarker.Gem, Progression, "Progression");
        Add(RunGraphMarker.Gem, Useful, "Useful");
        Add(RunGraphMarker.Gem, Filler, "Filler");
        Add(RunGraphMarker.Gem, Trap, "Trap");
        Add(RunGraphMarker.DashedRing, Useful, "Before a reload");
        row.AddChild(new Control { CustomMinimumSize = new Vector2(18f, 0f) });
        Heading("On the HP line:");
        Add(RunGraphMarker.Claim, Claim, "Item you claimed");
        Add(RunGraphMarker.Cross, Death, "DeathLink");

        var hint = Label("Hover a floor for details", 15, TextMuted);
        hint.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        hint.HorizontalAlignment = HorizontalAlignment.Right;
        row.AddChild(hint);
        return row;
    }

    private void ShowTooltip(int index, Vector2 anchor)
    {
        if (index < 0 || index >= _floors.Count)
        {
            _tooltip.Visible = false;
            return;
        }

        foreach (var child in _tooltipLines.GetChildren())
            child.QueueFree();

        var floor = _floors[index];
        _tooltipLines.AddChild(Label($"Floor {index + 1} · {floor.RoomName}", 20, Text, bold: true));
        _tooltipLines.AddChild(Label($"HP {floor.Hp} / {floor.MaxHp}", 16, TextMuted));
        if (_eventsByFloor.TryGetValue(index, out var events))
        {
            foreach (var e in events)
            {
                var line = Label(Describe(e), 16, ColorFor(e));
                line.AutowrapMode = TextServer.AutowrapMode.WordSmart;
                line.CustomMinimumSize = new Vector2(TooltipWidth - 28f, 0f);
                _tooltipLines.AddChild(line);
            }
        }

        _tooltip.Visible = true;
        _tooltip.ResetSize();
        // Sit beside the hovered floor, flipping to the left near the right edge of the overlay.
        var local = anchor - GlobalPosition;
        float x = local.X + 24f;
        if (x + TooltipWidth > Size.X - 16f)
            x = local.X - TooltipWidth - 24f;
        float y = Math.Clamp(local.Y, 16f, Math.Max(16f, Size.Y - _tooltip.Size.Y - 16f));
        _tooltip.Position = new Vector2(Math.Max(16f, x), y);
    }

    private static Color ColorFor(ApRunGraphEvent e) => e.Kind switch
    {
        ApRunGraphEventKind.CheckSent when e.Abandoned => TextMuted,
        ApRunGraphEventKind.CheckSent => ForItem(e.Classification),
        ApRunGraphEventKind.ItemClaimed or ApRunGraphEventKind.GoldClaimed => Claim,
        _ => Death,
    };

    private static string Describe(ApRunGraphEvent e)
    {
        string who = e.IsSelf ? "yourself" : e.OtherPlayer;
        return e.Kind switch
        {
            ApRunGraphEventKind.CheckSent => $"Sent {e.ItemName} to {who}"
                + (e.Abandoned ? " (before a reload)" : ""),
            ApRunGraphEventKind.ItemClaimed => $"Claimed {e.ItemName} from {who}",
            ApRunGraphEventKind.GoldClaimed => $"Claimed {e.Amount} AP gold",
            ApRunGraphEventKind.DeathLinkSent => "Sent a DeathLink",
            ApRunGraphEventKind.DeathLinkReceived => $"DeathLink from {e.OtherPlayer}",
            _ => e.Kind.ToString(),
        };
    }
}
