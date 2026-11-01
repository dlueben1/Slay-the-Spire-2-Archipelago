using Godot;
using MegaCrit.Sts2.Core.ControllerInput;
using StS2AP.Utils;
using StS2AP.Utils.RunGraph;
using static StS2AP.UI.RunGraph.RunGraphStyle;

namespace StS2AP.UI.RunGraph;

/// <summary>
/// Full-screen overlay with the AP end-of-run recap. It is added on top of whichever screen opened
/// it (game over, Run History or Past AP Runs) and removes itself when closed. The climb opens
/// first in its own tab, then the run floor by floor, then the rest of the recap.
/// </summary>
internal sealed partial class ApRunRecapOverlay : Control
{
    private static ApRunRecapOverlay? _open;
    private const float TooltipWidth = 360f;

    private ApRunRecapData _data = null!;
    private Dictionary<int, List<ApRunGraphEvent>> _eventsByFloor = new();
    private PanelContainer _tooltip = null!;
    private VBoxContainer _tooltipLines = null!;
    private Button _saveGraph = null!;
    private Button _saveRecap = null!;
    private readonly List<RecapTab> _tabButtons = new();
    private readonly List<(RecapTab Tab, Control Page)> _tabs = new();
    private int _currentTab;

    internal static bool IsOpen => _open != null && IsInstanceValid(_open);

    /// <summary>
    /// Loads the run's AP log and shows its recap above <paramref name="host"/>, the screen that
    /// asked for it; <paramref name="build"/> adds the game's side of the run.
    /// </summary>
    internal static void Open(Node host, long startTime, Func<ApRunGraphLog, ApRunRecapData> build)
    {
        var log = ApRunGraphRecorder.Store.Load(startTime, out string? error);
        if (log == null)
        {
            LogUtility.Warn(error ?? $"No run graph log exists for run {startTime}.");
            NotificationUtility.ShowRawText("The Archipelago recap for this run could not be read.");
            return;
        }

        ApRunRecapData data;
        try
        {
            data = build(log);
        }
        catch (Exception ex)
        {
            LogUtility.Error($"Could not build the AP recap for run {startTime}: {ex}");
            NotificationUtility.ShowRawText("The Archipelago recap for this run could not be built.");
            return;
        }

        if (IsOpen)
            _open!.QueueFree();
        var overlay = new ApRunRecapOverlay { Name = "ApRunRecapOverlay" };
        overlay.Build(data);
        _open = overlay;
        ApOverlayLayer.Show(overlay, host, overlay.QueueFree);
        overlay.BindTabHotkeys();
    }

    private void Build(ApRunRecapData data)
    {
        _data = data;
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop;

        var dim = new ColorRect { Color = Backdrop, MouseFilter = MouseFilterEnum.Stop };
        dim.SetAnchorsPreset(LayoutPreset.FullRect);
        // Clicking outside the panel closes the overlay.
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
        var panelStyle = Box(Page, PanelBorder, 16, 0f, borderWidth: 2);
        panelStyle.ShadowColor = new Color(0f, 0f, 0f, 0.6f);
        panelStyle.ShadowSize = 28;
        panel.AddThemeStyleboxOverride("panel", panelStyle);
        AddChild(panel);

        var root = new VBoxContainer();
        root.AddThemeConstantOverride("separation", 0);
        panel.AddChild(root);
        root.AddChild(BuildHeader());
        root.AddChild(BuildTabRow());
        root.AddChild(new ColorRect { Color = PanelBorder, CustomMinimumSize = new Vector2(0f, 1f) });

        var climb = Padded(ApRunRecapView.BuildClimbTab(data, out var canvas), top: 24);
        climb.SizeFlagsVertical = SizeFlags.ExpandFill;
        root.AddChild(climb);
        _eventsByFloor = ApRunGraphCanvas.GroupEvents(data.Log, data.Floors.Count);
        canvas.HoveredFloorChanged += ShowTooltip;

        var timeline = Padded(new ApRunTimelineView(data), top: 20);
        timeline.SizeFlagsVertical = SizeFlags.ExpandFill;
        root.AddChild(timeline);

        var fights = Padded(new ApRunFightsView(data), top: 20);
        fights.SizeFlagsVertical = SizeFlags.ExpandFill;
        root.AddChild(fights);

        var choices = Padded(new ApRunChoicesView(data), top: 24);
        choices.SizeFlagsVertical = SizeFlags.ExpandFill;
        root.AddChild(choices);

        var economy = Padded(new ApRunEconomyView(data), top: 24);
        economy.SizeFlagsVertical = SizeFlags.ExpandFill;
        root.AddChild(economy);

        var scroll = new ScrollContainer
        {
            SizeFlagsVertical = SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        var summary = Padded(ApRunRecapView.BuildSummary(data), top: 24);
        summary.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        scroll.AddChild(summary);
        root.AddChild(scroll);

        _tabs.Add((_tabButtons[0], climb));
        _tabs.Add((_tabButtons[1], timeline));
        _tabs.Add((_tabButtons[2], fights));
        _tabs.Add((_tabButtons[3], choices));
        _tabs.Add((_tabButtons[4], economy));
        _tabs.Add((_tabButtons[5], scroll));

        // Added last so it draws above the panel; it ignores the mouse so hovering keeps working.
        _tooltip = new PanelContainer { Visible = false, MouseFilter = MouseFilterEnum.Ignore };
        _tooltip.CustomMinimumSize = new Vector2(TooltipWidth, 0f);
        _tooltip.AddThemeStyleboxOverride("panel", Box(TooltipBg, PanelBorder, 10, 14f));
        _tooltipLines = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        _tooltipLines.AddThemeConstantOverride("separation", 4);
        _tooltip.AddChild(_tooltipLines);
        AddChild(_tooltip);
        ShowTab(0);
    }

    private static MarginContainer Padded(Control content, int top)
    {
        var margin = new MarginContainer();
        foreach (string side in new[] { "left", "right", "bottom" })
            margin.AddThemeConstantOverride($"margin_{side}", 32);
        margin.AddThemeConstantOverride("margin_top", top);
        margin.AddChild(content);
        return margin;
    }

    /// <summary>Who played, the outcome and the run's numbers, above the tabs so every tab keeps them in view.</summary>
    private Control BuildHeader()
    {
        var header = new PanelContainer();
        var style = Box(PanelColor, PanelBorder, 0, 0f, borderWidth: 0);
        // Inside the panel's 2-pixel border, so its corners sit just inside the panel's own.
        style.CornerRadiusTopLeft = style.CornerRadiusTopRight = 14;
        style.ContentMarginLeft = style.ContentMarginRight = 32f;
        style.ContentMarginTop = style.ContentMarginBottom = 14f;
        header.AddThemeStyleboxOverride("panel", style);
        header.AddChild(ApRunRecapView.TopBarRow(_data, withIdentity: true));
        return header;
    }

    private Control BuildTabRow()
    {
        var bar = new MarginContainer();
        bar.AddThemeConstantOverride("margin_left", 20);
        bar.AddThemeConstantOverride("margin_right", 24);
        bar.AddThemeConstantOverride("margin_top", 10);
        bar.AddThemeConstantOverride("margin_bottom", 6);
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 12);
        bar.AddChild(row);

        var tabs = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        tabs.AddThemeConstantOverride("separation", 4);
        foreach (string title in new[] { "The climb", "Timeline", "Fights", "Choices", "Economy", "Recap" })
        {
            int index = _tabButtons.Count;
            var tab = new RecapTab(title);
            tab.Pressed += () => ShowTab(index);
            _tabButtons.Add(tab);
            tabs.AddChild(tab);
        }
        row.AddChild(tabs);

        _saveGraph = ActionButton("Save graph");
        _saveGraph.TooltipText = "Save a short picture: your character, the run's numbers and the climb.";
        _saveGraph.Pressed += () => SaveImage(
            () => ApRunRecapView.BuildGraphCard(_data), ApRunRecapView.GraphImageWidth, "graph");
        row.AddChild(_saveGraph);

        _saveRecap = ActionButton("Save recap");
        _saveRecap.TooltipText = "Save the whole recap as one tall picture.";
        _saveRecap.Pressed += () => SaveImage(
            () => ApRunRecapView.Build(_data), ApRunRecapView.RecapImageWidth, "recap");
        row.AddChild(_saveRecap);

        // Escape and the controller's back button close it through ApOverlayLayer.
        var close = ActionButton("Close  (Esc)");
        close.Pressed += QueueFree;
        row.AddChild(close);
        // Focus the button once in the tree so a controller can close the overlay.
        Ready += () => close.GrabFocus();
        return bar;
    }

    private void ShowTab(int index)
    {
        for (int i = 0; i < _tabs.Count; i++)
        {
            _tabs[i].Tab.Selected = i == index;
            _tabs[i].Page.Visible = i == index;
        }
        _currentTab = index;
        _tooltip.Visible = false;
    }

    /// <summary>The game's tab-left and tab-right inputs switch tabs, as in its settings screen.</summary>
    private void BindTabHotkeys()
    {
        ApOverlayLayer.BindHotkey(this, MegaInput.viewDeckAndTabLeft,
            () => ShowTab((_currentTab + _tabs.Count - 1) % _tabs.Count));
        ApOverlayLayer.BindHotkey(this, MegaInput.viewExhaustPileAndTabRight,
            () => ShowTab((_currentTab + 1) % _tabs.Count));
    }

    private async void SaveImage(Func<Control> build, int width, string kind)
    {
        // One picture at a time: both use the same offscreen viewport approach and file folder.
        _saveGraph.Disabled = _saveRecap.Disabled = true;
        try
        {
            string fileName = $"{_data.Log.StartTime}-{kind}.png";
            string? path = await ApRecapImage.SaveAsync(this, build, width, ApRunGraphRecorder.Store.ExportsDirectory, fileName);
            if (path != null)
            {
                NotificationUtility.ShowRawText($"Saved the {kind} to {path}", timeout: 8);
                ApRunGraphFiles.ShowInFileManager(path);
            }
            else
            {
                NotificationUtility.ShowRawText($"The {kind} image could not be saved. The log has details.");
            }
        }
        finally
        {
            if (IsInstanceValid(this))
                _saveGraph.Disabled = _saveRecap.Disabled = false;
        }
    }

    private void ShowTooltip(int index, Vector2 anchor)
    {
        var floors = _data.Floors;
        if (index < 0 || index >= floors.Count)
        {
            _tooltip.Visible = false;
            return;
        }

        // Removed at once, not just queued, so the resize below measures only the new floor's lines.
        foreach (var child in _tooltipLines.GetChildren())
        {
            _tooltipLines.RemoveChild(child);
            child.QueueFree();
        }

        var floor = floors[index];
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

    internal static Color ColorFor(ApRunGraphEvent e) => e.Kind switch
    {
        ApRunGraphEventKind.CheckSent or ApRunGraphEventKind.ItemClaimed or ApRunGraphEventKind.GoldClaimed
            when e.Abandoned => TextMuted,
        ApRunGraphEventKind.CheckSent => ForItem(e.Classification),
        ApRunGraphEventKind.ItemClaimed or ApRunGraphEventKind.GoldClaimed => Claim,
        _ => Death,
    };

    internal static string Describe(ApRunGraphEvent e)
    {
        string who = e.IsSelf ? "yourself" : e.OtherPlayer;
        return e.Kind switch
        {
            ApRunGraphEventKind.CheckSent => $"Sent {e.ItemName} to {who}"
                + (e.Abandoned ? " (before a reload)" : ""),
            ApRunGraphEventKind.ItemClaimed => $"Claimed {e.ItemName} from {who}"
                + (e.Abandoned ? " (before a reload)" : ""),
            ApRunGraphEventKind.GoldClaimed => $"Claimed {e.Amount} AP gold"
                + (e.Abandoned ? " (before a reload)" : ""),
            ApRunGraphEventKind.DeathLinkSent => "Sent a DeathLink",
            ApRunGraphEventKind.DeathLinkReceived => $"DeathLink from {e.OtherPlayer}",
            _ => e.Kind.ToString(),
        };
    }
}

/// <summary>A tab in the recap's tab row: plain text, with a gold bar under the open tab.</summary>
internal sealed partial class RecapTab : Button
{
    private bool _selected;

    internal RecapTab(string title)
    {
        Text = title;
        FocusMode = FocusModeEnum.All;
        CustomMinimumSize = new Vector2(0f, 52f);
        var plain = new StyleBoxEmpty { ContentMarginLeft = 18f, ContentMarginRight = 18f };
        foreach (string state in new[] { "normal", "hover", "pressed", "disabled" })
            AddThemeStyleboxOverride(state, plain);
        // A controller needs to see which tab has focus.
        AddThemeStyleboxOverride("focus", Box(Colors.Transparent, Progression, 8, 18f));
        AddThemeFontOverride("font", Bold);
        AddThemeFontSizeOverride("font_size", 21);
        AddThemeColorOverride("font_hover_color", RunGraphStyle.Text);
        AddThemeColorOverride("font_pressed_color", RunGraphStyle.Text);
        Draw += () =>
        {
            if (_selected)
                DrawStyleBox(Box(Gold, Gold, 2, 0f), new Rect2(14f, Size.Y - 5f, Size.X - 28f, 4f));
        };
        Selected = false;
    }

    internal bool Selected
    {
        set
        {
            _selected = value;
            Color color = value ? RunGraphStyle.Text : TextMuted;
            AddThemeColorOverride("font_color", color);
            AddThemeColorOverride("font_focus_color", color);
            QueueRedraw();
        }
    }
}
