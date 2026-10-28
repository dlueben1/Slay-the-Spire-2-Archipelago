using Godot;

namespace StS2AP.UI.RunGraph;

/// <summary>Shared look of the run graph: a dark, cool palette so the HP line and AP colors stand out.</summary>
internal static class RunGraphStyle
{
    internal static readonly Color Backdrop = new(0.02f, 0.02f, 0.05f, 0.82f);
    internal static readonly Color PanelColor = new("#151A2C");
    internal static readonly Color PanelBorder = new("#353C63");
    internal static readonly Color Card = new("#1D2338");
    internal static readonly Color FrameTop = new("#1F2544");
    internal static readonly Color FrameBottom = new("#0F1220");
    internal static readonly Color LaneStrip = new(1f, 1f, 1f, 0.035f);
    internal static readonly Color TooltipBg = new(0.05f, 0.06f, 0.11f, 0.97f);

    internal static readonly Color Text = new("#F2EAD8");
    internal static readonly Color TextMuted = new("#9AA0C3");
    internal static readonly Color Grid = new(1f, 1f, 1f, 0.06f);
    internal static readonly Color Ink = new(0.03f, 0.03f, 0.07f, 0.9f);

    // Archipelago's standard item colors, as the AP text client shows them.
    internal static readonly Color Progression = new("#AF99EF");
    internal static readonly Color Useful = new("#6D8BE8");
    internal static readonly Color Filler = new("#00EEEE");
    internal static readonly Color Trap = new("#FA8072");
    internal static readonly Color Claim = new("#4FD1A5");
    internal static readonly Color Death = new("#E5484D");

    private static readonly Color HpLow = new("#E5484D");
    private static readonly Color HpMid = new("#F5A524");
    private static readonly Color HpHigh = new("#46C47E");

    private static Font? _regular;
    private static Font? _bold;

    internal static Font Regular => _regular ??= LoadFont("res://fonts/kreon_regular.ttf");
    internal static Font Bold => _bold ??= LoadFont("res://fonts/kreon_bold.ttf");

    internal static Color ForItem(ApRunGraphItemType type) => type switch
    {
        ApRunGraphItemType.Progression => Progression,
        ApRunGraphItemType.Useful => Useful,
        ApRunGraphItemType.Trap => Trap,
        _ => Filler,
    };

    /// <summary>Red at low HP, amber at half, green when healthy.</summary>
    internal static Color ForHp(float fraction) => fraction < 0.5f
        ? HpLow.Lerp(HpMid, Math.Clamp(fraction / 0.5f, 0f, 1f))
        : HpMid.Lerp(HpHigh, Math.Clamp((fraction - 0.5f) / 0.5f, 0f, 1f));

    internal static StyleBoxFlat Box(Color background, Color border, int radius, float margin, int borderWidth = 1)
    {
        var style = new StyleBoxFlat { BgColor = background, BorderColor = border };
        style.SetBorderWidthAll(borderWidth);
        style.SetCornerRadiusAll(radius);
        style.SetContentMarginAll(margin);
        return style;
    }

    internal static Label Label(string text, int size, Color color, bool bold = false)
    {
        var label = new Label { Text = text, AutowrapMode = TextServer.AutowrapMode.Off };
        label.AddThemeFontOverride("font", bold ? Bold : Regular);
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", color);
        return label;
    }

    private static Font LoadFont(string path) =>
        ResourceLoader.Exists(path) ? ResourceLoader.Load<Font>(path) : ThemeDB.FallbackFont;

    // Marker shapes are shared by the graph and its legend so the two always match.

    /// <summary>A check-sent pip: dark rim, darker lower half and a small highlight, so it reads as a gem.</summary>
    internal static void DrawGem(CanvasItem canvas, Vector2 center, float radius, Color color)
    {
        canvas.DrawCircle(center, radius + 1.5f, Ink);
        canvas.DrawCircle(center, radius, color.Darkened(0.25f));
        canvas.DrawCircle(center + new Vector2(0f, -radius * 0.18f), radius * 0.78f, color);
        canvas.DrawCircle(center + new Vector2(-radius * 0.3f, -radius * 0.35f), radius * 0.28f, new Color(1f, 1f, 1f, 0.55f));
    }

    /// <summary>A check sent on a branch that a checkpoint reload later undid.</summary>
    internal static void DrawDashedRing(CanvasItem canvas, Vector2 center, float radius, Color color)
    {
        const int dashes = 8;
        float span = Mathf.Tau / dashes;
        for (int i = 0; i < dashes; i++)
            canvas.DrawArc(center, radius, i * span, i * span + span * 0.55f, 6, color, 2.5f, antialiased: true);
    }

    /// <summary>A downward triangle whose tip points at the floor where an item was claimed.</summary>
    internal static void DrawClaimMarker(CanvasItem canvas, Vector2 tip)
    {
        var triangle = new[] { tip + new Vector2(-7f, -12f), tip + new Vector2(7f, -12f), tip };
        canvas.DrawColoredPolygon(triangle, Claim);
        canvas.DrawPolyline(new[] { triangle[0], triangle[1], triangle[2], triangle[0] }, Ink, 1.5f, antialiased: true);
    }

    internal static void DrawCross(CanvasItem canvas, Vector2 center, float half, Color color)
    {
        canvas.DrawLine(center + new Vector2(-half, -half), center + new Vector2(half, half), color, 3f, antialiased: true);
        canvas.DrawLine(center + new Vector2(half, -half), center + new Vector2(-half, half), color, 3f, antialiased: true);
    }
}

internal enum RunGraphMarker
{
    Gem,
    DashedRing,
    Claim,
    Cross,
}

/// <summary>A legend swatch drawn with the same shape the graph uses.</summary>
internal sealed partial class RunGraphSwatch : Control
{
    internal RunGraphSwatch(RunGraphMarker marker, Color color)
    {
        CustomMinimumSize = new Vector2(20f, 20f);
        SizeFlagsVertical = SizeFlags.ShrinkCenter;
        MouseFilter = MouseFilterEnum.Ignore;
        Draw += () =>
        {
            var center = Size / 2f;
            switch (marker)
            {
                case RunGraphMarker.Gem: RunGraphStyle.DrawGem(this, center, 7f, color); break;
                case RunGraphMarker.DashedRing: RunGraphStyle.DrawDashedRing(this, center, 6f, color); break;
                case RunGraphMarker.Claim: RunGraphStyle.DrawClaimMarker(this, center + new Vector2(0f, 6f)); break;
                case RunGraphMarker.Cross: RunGraphStyle.DrawCross(this, center, 5f, color); break;
            }
        };
    }
}
