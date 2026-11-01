using Godot;
using StS2AP.Utils;
using StS2AP.Utils.RunGraph;
using static StS2AP.UI.RunGraph.RunGraphStyle;

namespace StS2AP.UI.RunGraph;

/// <summary>
/// Full-screen overlay with the AP end-of-run recap. It is added on top of whichever screen opened
/// it (game over, Run History or Past AP Runs) and removes itself when closed.
/// </summary>
internal sealed partial class ApRunRecapOverlay : Control
{
    private const string NodeName = "ApRunRecapOverlay";
    private const float TooltipWidth = 360f;

    private ApRunRecapData _data = null!;
    private Dictionary<int, List<ApRunGraphEvent>> _eventsByFloor = new();
    private PanelContainer _tooltip = null!;
    private VBoxContainer _tooltipLines = null!;
    private Button _saveImage = null!;

    /// <summary>Loads the run's AP log and shows its recap; <paramref name="build"/> adds the game's side of the run.</summary>
    internal static void Open(Control host, long startTime, Func<ApRunGraphLog, ApRunRecapData> build)
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

        host.GetNodeOrNull(NodeName)?.QueueFree();
        var overlay = new ApRunRecapOverlay { Name = NodeName };
        overlay.Build(data);
        host.AddChild(overlay);
    }

    private void Build(ApRunRecapData data)
    {
        _data = data;
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop;
        ZIndex = 100;

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
        root.AddChild(BuildToolbar());

        var scroll = new ScrollContainer
        {
            SizeFlagsVertical = SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        var margin = new MarginContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        foreach (string side in new[] { "left", "right", "bottom" })
            margin.AddThemeConstantOverride($"margin_{side}", 32);
        margin.AddThemeConstantOverride("margin_top", 8);
        scroll.AddChild(margin);
        root.AddChild(scroll);

        margin.AddChild(ApRunRecapView.Build(data, interactive: true, out var canvas));
        _eventsByFloor = ApRunGraphCanvas.GroupEvents(data.Log, data.Floors.Count);
        canvas.HoveredFloorChanged += ShowTooltip;
        // A tooltip placed for one scroll position would point at the wrong floor after scrolling.
        scroll.GetVScrollBar().ValueChanged += _ => _tooltip.Visible = false;

        // Added last so it draws above the panel; it ignores the mouse so hovering keeps working.
        _tooltip = new PanelContainer { Visible = false, MouseFilter = MouseFilterEnum.Ignore };
        _tooltip.CustomMinimumSize = new Vector2(TooltipWidth, 0f);
        _tooltip.AddThemeStyleboxOverride("panel", Box(TooltipBg, PanelBorder, 10, 14f));
        _tooltipLines = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        _tooltipLines.AddThemeConstantOverride("separation", 4);
        _tooltip.AddChild(_tooltipLines);
        AddChild(_tooltip);
    }

    private Control BuildToolbar()
    {
        var bar = new MarginContainer();
        bar.AddThemeConstantOverride("margin_left", 32);
        bar.AddThemeConstantOverride("margin_right", 24);
        bar.AddThemeConstantOverride("margin_top", 20);
        bar.AddThemeConstantOverride("margin_bottom", 16);
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 12);
        bar.AddChild(row);

        var title = Label("Archipelago run recap", 22, TextMuted, bold: true);
        title.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        title.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        row.AddChild(title);

        _saveImage = ActionButton("Save image");
        _saveImage.TooltipText = "Save this recap as a picture, ready to share.";
        _saveImage.Pressed += SaveImage;
        row.AddChild(_saveImage);

        var close = ActionButton("Close  (Esc)");
        close.Shortcut = new Shortcut { Events = new Godot.Collections.Array { new InputEventAction { Action = "ui_cancel" } } };
        close.Pressed += QueueFree;
        row.AddChild(close);
        // Focus the button once in the tree so a controller can close the overlay.
        Ready += () => close.GrabFocus();
        return bar;
    }

    private async void SaveImage()
    {
        _saveImage.Disabled = true;
        try
        {
            string fileName = $"{_data.Log.StartTime}-recap.png";
            string? path = await ApRecapImage.SaveAsync(this, _data, ApRunGraphRecorder.Store.ExportsDirectory, fileName);
            if (path != null)
            {
                NotificationUtility.ShowRawText($"Saved the recap to {path}", timeout: 8);
                ApRunGraphFiles.ShowInFileManager(path);
            }
            else
            {
                NotificationUtility.ShowRawText("The recap image could not be saved. The log has details.");
            }
        }
        finally
        {
            if (IsInstanceValid(_saveImage))
                _saveImage.Disabled = false;
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

        foreach (var child in _tooltipLines.GetChildren())
            child.QueueFree();

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
