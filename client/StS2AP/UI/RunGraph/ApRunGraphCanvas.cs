using Godot;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Runs.History;
using static StS2AP.UI.RunGraph.RunGraphStyle;

namespace StS2AP.UI.RunGraph;

/// <summary>One map point of the run as the graph draws it.</summary>
internal sealed record RunGraphFloor(
    int Index,
    int ActIndex,
    int Hp,
    int MaxHp,
    MapPointType MapPointType,
    string RoomName,
    Texture2D? Icon,
    Texture2D? IconOutline);

/// <summary>
/// Draws the HP line, room icons and AP lane for one run. Drawing and input use signals rather
/// than virtual overrides, as the other mod-created controls do.
/// </summary>
internal sealed partial class ApRunGraphCanvas : Control
{
    private const string ApIconPath = "res://images/APIcon.png";

    private const float Left = 70f;
    private const float Right = 28f;
    private const float Top = 44f;
    private const float IconSize = 30f;
    private const float PipRadius = 7f;
    private const float PipSpacing = 19f;
    private const int MaxStackedPips = 4;
    private const float LaneHeight = MaxStackedPips * PipSpacing + 26f;

    private IReadOnlyList<RunGraphFloor> _floors = Array.Empty<RunGraphFloor>();
    private IReadOnlyDictionary<int, List<ApRunGraphEvent>> _eventsByFloor = new Dictionary<int, List<ApRunGraphEvent>>();
    private ApRunOutcome? _outcome;
    private Texture2D? _apIcon;
    private float _reveal;
    private int _hovered = -1;

    /// <summary>Raised with the hovered floor (or -1) and the global point a tooltip should sit beside.</summary>
    internal event Action<int, Vector2>? HoveredFloorChanged;

    internal static IReadOnlyList<RunGraphFloor> BuildFloors(
        IReadOnlyList<IReadOnlyList<MapPointHistoryEntry>> mapHistory,
        ulong localNetId)
    {
        var floors = new List<RunGraphFloor>();
        for (int act = 0; act < mapHistory.Count; act++)
        {
            foreach (var entry in mapHistory[act])
            {
                var stats = entry.PlayerStats.FirstOrDefault(p => p.PlayerId == localNetId)
                    ?? entry.PlayerStats.FirstOrDefault();
                var room = entry.Rooms.FirstOrDefault();
                floors.Add(new RunGraphFloor(
                    floors.Count,
                    act,
                    stats?.CurrentHp ?? 0,
                    stats?.MaxHp ?? 0,
                    entry.MapPointType,
                    DescribeRoom(entry.MapPointType, room?.RoomType.ToString()),
                    room == null ? null : LoadIcon(entry.MapPointType, room, outline: false),
                    room == null ? null : LoadIcon(entry.MapPointType, room, outline: true)));
            }
        }
        return floors;
    }

    /// <summary>Events are keyed by graph index; the recorder stores the game's TotalFloor, which counts from 1.</summary>
    internal static Dictionary<int, List<ApRunGraphEvent>> GroupEvents(ApRunGraphLog log, int floorCount)
    {
        var byFloor = new Dictionary<int, List<ApRunGraphEvent>>();
        if (floorCount == 0)
            return byFloor;
        foreach (var e in log.Events)
        {
            int index = Math.Clamp(e.Floor - 1, 0, floorCount - 1);
            if (!byFloor.TryGetValue(index, out var list))
                byFloor[index] = list = new List<ApRunGraphEvent>();
            list.Add(e);
        }
        return byFloor;
    }

    internal void Load(
        IReadOnlyList<RunGraphFloor> floors,
        IReadOnlyDictionary<int, List<ApRunGraphEvent>> eventsByFloor,
        ApRunOutcome? outcome)
    {
        _floors = floors;
        _eventsByFloor = eventsByFloor;
        _outcome = outcome;
        if (ResourceLoader.Exists(ApIconPath))
            _apIcon = ResourceLoader.Load<Texture2D>(ApIconPath);

        MouseFilter = MouseFilterEnum.Stop;
        Draw += DrawGraph;
        GuiInput += OnGuiInput;
        MouseExited += () => SetHovered(-1);

        var tween = CreateTween();
        tween.TweenMethod(Callable.From<float>(value =>
        {
            _reveal = value;
            QueueRedraw();
        }), 0f, 1f, 1.4).SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Cubic);
    }

    private float LaneTop => Size.Y - LaneHeight;
    private float IconRowY => LaneTop - IconSize * 0.5f - 12f;
    private float HpBottom => IconRowY - IconSize * 0.5f - 16f;

    private float XFor(int index)
    {
        if (_floors.Count <= 1)
            return Left;
        return Left + index * (Size.X - Left - Right) / (_floors.Count - 1);
    }

    private static float HpFraction(RunGraphFloor floor) =>
        floor.MaxHp > 0 ? Math.Clamp((float)floor.Hp / floor.MaxHp, 0f, 1f) : 0f;

    private float YForHp(RunGraphFloor floor) => HpBottom - HpFraction(floor) * (HpBottom - Top);

    private void OnGuiInput(InputEvent input)
    {
        if (input is not InputEventMouseMotion motion || _floors.Count == 0)
            return;
        float step = _floors.Count <= 1 ? 1f : (Size.X - Left - Right) / (_floors.Count - 1);
        int index = (int)MathF.Round((motion.Position.X - Left) / step);
        SetHovered(Math.Clamp(index, 0, _floors.Count - 1));
    }

    private void SetHovered(int index)
    {
        if (index == _hovered)
            return;
        _hovered = index;
        QueueRedraw();
        var anchor = index < 0 ? Vector2.Zero : GlobalPosition + new Vector2(XFor(index), Top);
        HoveredFloorChanged?.Invoke(index, anchor);
    }

    private void DrawGraph()
    {
        DrawFrame();
        if (_floors.Count == 0)
        {
            DrawString(Regular, new Vector2(Left, Top + 40f), "This run has no floors to show.",
                HorizontalAlignment.Left, -1, 22, TextMuted);
            return;
        }

        DrawGrid();
        if (_hovered >= 0)
        {
            float x = XFor(_hovered);
            float half = Math.Max(10f, (Size.X - Left - Right) / Math.Max(1, _floors.Count - 1) / 2f);
            DrawRect(new Rect2(x - half, Top - 14f, half * 2f, Size.Y - Top + 6f), new Color(1f, 1f, 1f, 0.05f));
        }

        int visible = Math.Clamp((int)MathF.Ceiling(_reveal * _floors.Count), 1, _floors.Count);
        var points = new Vector2[visible];
        for (int i = 0; i < visible; i++)
            points[i] = new Vector2(XFor(i), YForHp(_floors[i]));

        DrawHpArea(points);
        DrawHpLine(points);

        for (int i = 0; i < visible; i++)
        {
            DrawRoomIcon(_floors[i], i == _hovered);
            if (_eventsByFloor.TryGetValue(i, out var events))
                DrawFloorEvents(i, points[i], events, i == _hovered);
        }
        for (int i = 0; i < visible; i++)
            DrawHpPoint(i, points[i]);
    }

    private void DrawFrame()
    {
        // A vertical gradient behind the graph gives it depth without a texture.
        var rect = new Rect2(Vector2.Zero, Size);
        DrawPolygon(
            new[] { rect.Position, new Vector2(rect.End.X, 0f), rect.End, new Vector2(0f, rect.End.Y) },
            new[] { FrameTop, FrameTop, FrameBottom, FrameBottom });
        DrawRect(rect, PanelBorder, filled: false, width: 1f);

        // The AP lane gets its own faint strip under the room icons.
        DrawRect(new Rect2(8f, LaneTop - 8f, Size.X - 16f, LaneHeight - 2f), LaneStrip);
        if (_apIcon != null)
            DrawTextureRect(_apIcon, new Rect2(19f, LaneTop - 2f, 32f, 32f), tile: false);
        else
            DrawString(Bold, new Vector2(20f, LaneTop + 20f), "AP", HorizontalAlignment.Left, -1, 20, Text);
        DrawString(Bold, new Vector2(12f, LaneTop + 50f), "Sent", HorizontalAlignment.Center, 46f, 15, TextMuted);
    }

    private void DrawGrid()
    {
        foreach (float fraction in new[] { 0f, 0.25f, 0.5f, 0.75f, 1f })
        {
            float y = HpBottom - fraction * (HpBottom - Top);
            DrawLine(new Vector2(Left, y), new Vector2(Size.X - Right, y), Grid, 1f);
            DrawString(Regular, new Vector2(12f, y + 6f), $"{fraction * 100:0}%",
                HorizontalAlignment.Left, Left - 18f, 15, TextMuted);
        }

        int lastAct = -1;
        for (int i = 0; i < _floors.Count; i++)
        {
            if (_floors[i].ActIndex == lastAct)
                continue;
            lastAct = _floors[i].ActIndex;
            float x = i == 0 ? Left : (XFor(i - 1) + XFor(i)) / 2f;
            if (i > 0)
                DrawDashedLine(new Vector2(x, 10f), new Vector2(x, Size.Y - 10f), new Color(1f, 1f, 1f, 0.1f), 1f, 5f);
            DrawString(Bold, new Vector2(x + 8f, 28f), $"Act {lastAct + 1}",
                HorizontalAlignment.Left, -1, 18, TextMuted);
        }
    }

    private void DrawHpArea(Vector2[] points)
    {
        // One quad per segment so each fades from its HP color to transparent at the base.
        for (int i = 0; i + 1 < points.Length; i++)
        {
            var a = points[i];
            var b = points[i + 1];
            if (a.Y >= HpBottom - 0.5f && b.Y >= HpBottom - 0.5f)
                continue;
            Color ca = ForHp(HpFraction(_floors[i])) with { A = 0.22f };
            Color cb = ForHp(HpFraction(_floors[i + 1])) with { A = 0.22f };
            DrawPolygon(
                new[] { a, b, new Vector2(b.X, HpBottom), new Vector2(a.X, HpBottom) },
                new[] { ca, cb, cb with { A = 0f }, ca with { A = 0f } });
        }
    }

    private void DrawHpLine(Vector2[] points)
    {
        if (points.Length < 2)
            return;
        var colors = new Color[points.Length];
        for (int i = 0; i < points.Length; i++)
            colors[i] = ForHp(HpFraction(_floors[i]));

        // Two soft passes under the core line read as a glow.
        DrawPolylineColors(points, colors.Select(c => c with { A = 0.07f }).ToArray(), 14f, antialiased: true);
        DrawPolylineColors(points, colors.Select(c => c with { A = 0.16f }).ToArray(), 8f, antialiased: true);
        DrawPolylineColors(points, colors, 3f, antialiased: true);
    }

    private void DrawHpPoint(int index, Vector2 point)
    {
        bool last = index == _floors.Count - 1;
        bool died = last && _outcome == ApRunOutcome.Lost;
        bool hovered = index == _hovered;
        float radius = died ? 7f : hovered ? 6f : 3.5f;
        Color color = died ? Death : ForHp(HpFraction(_floors[index]));
        DrawCircle(point, radius + 2f, Ink);
        DrawCircle(point, radius, color);
        if (died)
            DrawCross(this, point, 4f, Ink);
    }

    private void DrawRoomIcon(RunGraphFloor floor, bool hovered)
    {
        float size = hovered ? IconSize * 1.3f : IconSize;
        float x = XFor(floor.Index);
        var rect = new Rect2(x - size / 2f, IconRowY - size / 2f, size, size);
        if (floor.IconOutline != null)
            DrawTextureRect(floor.IconOutline, rect, tile: false);
        if (floor.Icon != null)
            DrawTextureRect(floor.Icon, rect, tile: false);
    }

    private void DrawFloorEvents(int index, Vector2 hpPoint, List<ApRunGraphEvent> events, bool hovered)
    {
        float x = XFor(index);

        // Items claimed from the multiworld hang above the HP point.
        if (events.Any(e => e.Kind is ApRunGraphEventKind.ItemClaimed or ApRunGraphEventKind.GoldClaimed))
        {
            DrawClaimMarker(this, new Vector2(x, hpPoint.Y - 11f));
        }

        if (events.Any(e => e.Kind == ApRunGraphEventKind.DeathLinkReceived))
            DrawCross(this, new Vector2(x, hpPoint.Y + 17f), 6f, Death);
        if (events.Any(e => e.Kind == ApRunGraphEventKind.DeathLinkSent))
            DrawArc(hpPoint, 12f, 0f, Mathf.Tau, 28, Death, 2f, antialiased: true);

        var checks = events.Where(e => e.Kind == ApRunGraphEventKind.CheckSent).ToList();
        float scale = hovered ? 1.25f : 1f;
        for (int k = 0; k < Math.Min(checks.Count, MaxStackedPips); k++)
        {
            var center = new Vector2(x, LaneTop + 8f + k * PipSpacing);
            Color color = ForItem(checks[k].Classification);
            if (checks[k].Abandoned)
                DrawDashedRing(this, center, (PipRadius - 1f) * scale, color);
            else
                DrawGem(this, center, PipRadius * scale, color);
        }
        if (checks.Count > MaxStackedPips)
        {
            DrawString(Bold, new Vector2(x - 10f, LaneTop + 8f + MaxStackedPips * PipSpacing + 2f),
                $"+{checks.Count - MaxStackedPips}", HorizontalAlignment.Left, -1, 14, TextMuted);
        }
    }

    private static string DescribeRoom(MapPointType type, string? roomType)
    {
        string name = type switch
        {
            MapPointType.RestSite => "Rest site",
            MapPointType.Unassigned => "Unknown",
            _ => type.ToString(),
        };
        // An unknown map point reveals its real room once entered.
        return type == MapPointType.Unknown && roomType != null ? $"{name} ({roomType})" : name;
    }

    /// <summary>
    /// Mirrors Run History's NMapPointHistoryEntry: only bosses and ancients have per-model icons.
    /// Passing a monster's encounter ID would ask for an icon that does not exist.
    /// </summary>
    private static Texture2D? LoadIcon(MapPointType type, MapPointRoomHistoryEntry room, bool outline)
    {
        var modelId = type is MapPointType.Boss or MapPointType.Ancient ? room.ModelId : null;
        string? path = outline
            ? ImageHelper.GetRoomIconOutlinePath(type, room.RoomType, modelId)
            : ImageHelper.GetRoomIconPath(type, room.RoomType, modelId);
        if (path == null || !ResourceLoader.Exists(path))
            return null;
        return PreloadManager.Cache.GetCompressedTexture2D(path);
    }
}
