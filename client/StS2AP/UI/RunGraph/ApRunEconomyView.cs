using Godot;
using MegaCrit.Sts2.Core.Map;
using static StS2AP.UI.RunGraph.RunGraphStyle;

namespace StS2AP.UI.RunGraph;

/// <summary>
/// The recap's Economy tab: gold across the run as a line, then where it came from (fights, other
/// rooms and AP gold) and where it went (shops, other spending, gold lost or stolen).
/// </summary>
internal sealed partial class ApRunEconomyView : ScrollContainer
{
    internal ApRunEconomyView(ApRunRecapData data)
    {
        SizeFlagsVertical = SizeFlags.ExpandFill;
        HorizontalScrollMode = ScrollMode.Disabled;

        var floors = data.FloorEntries().ToList();
        var economyFloors = new List<ApEconomyFloor>();
        int gold = 0;
        foreach (var floor in floors)
        {
            var stats = floor.Stats;
            // A floor without the local player's stats keeps the gold of the floor before.
            gold = stats?.CurrentGold ?? gold;
            economyFloors.Add(new ApEconomyFloor(
                RoomOf(floor),
                stats?.GoldGained ?? 0,
                stats?.GoldSpent ?? 0,
                stats?.GoldLost ?? 0,
                stats?.GoldStolen ?? 0,
                gold));
        }
        var economy = ApRunEconomy.Build(economyFloors, data.Log);

        var gutter = new MarginContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        gutter.AddThemeConstantOverride("margin_right", 16);
        AddChild(gutter);
        var page = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        page.AddThemeConstantOverride("separation", 20);
        gutter.AddChild(page);

        if (economyFloors.Count == 0)
        {
            page.AddChild(Label("This run has no floors to show.", 16, TextMuted));
            return;
        }

        var tiles = new GridContainer { Columns = 4 };
        tiles.AddThemeConstantOverride("h_separation", 12);
        tiles.AddChild(ApRunRecapView.Tile($"{economy.Gained}", $"gold gained, {economy.ApGold} of it from AP", Gold));
        tiles.AddChild(ApRunRecapView.Tile($"{economy.Spent}", $"gold spent, {economy.ShopSpent} of it in shops", Text));
        tiles.AddChild(ApRunRecapView.Tile($"{economy.PeakGold}", $"most gold held, on floor {economy.PeakFloor + 1}", Gold));
        tiles.AddChild(ApRunRecapView.Tile($"{economy.EndGold}", $"gold at the end, from {economy.StartGold} at the start", Text));
        page.AddChild(tiles);

        var chartPanel = ApRunRecapView.Panel(PanelColor, PanelBorder, 14, 18f);
        var chartBox = new VBoxContainer();
        chartBox.AddThemeConstantOverride("separation", 10);
        chartPanel.AddChild(chartBox);
        var heading = new HBoxContainer();
        heading.AddThemeConstantOverride("separation", 10);
        heading.AddChild(Label("Gold by floor", 21, Text, bold: true));
        var caption = Label("hover a floor for its gold", 14, TextMuted);
        caption.SizeFlagsVertical = SizeFlags.ShrinkEnd;
        caption.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        heading.AddChild(caption);
        heading.AddChild(Legend());
        chartBox.AddChild(heading);
        chartBox.AddChild(new GoldChart(data.Floors, economyFloors, economy) { CustomMinimumSize = new Vector2(0f, 330f) });
        page.AddChild(chartPanel);

        var flows = new HBoxContainer();
        flows.AddThemeConstantOverride("separation", 18);
        int most = new[] { economy.CombatGold, economy.OtherGold, economy.ApGold, economy.ShopSpent, economy.OtherSpent, economy.Lost, economy.Stolen }.Max();
        flows.AddChild(Breakdown("Where it came from", $"{economy.Gained} gold", most, new[]
        {
            ("Fights", "elites, bosses and hallway fights", economy.CombatGold, Gold),
            ("Other rooms", "events, treasure and relics", economy.OtherGold, Gold.Darkened(0.25f)),
            ("AP gold", "claimed from the multiworld", economy.ApGold, Claim),
        }));
        flows.AddChild(Breakdown("Where it went", $"{economy.Spent + economy.Lost + economy.Stolen} gold", most, new[]
        {
            ("Shops", "cards, relics, potions and removals", economy.ShopSpent, Text),
            ("Other spending", "events and the like", economy.OtherSpent, TextMuted),
            ("Lost", "taken by events and effects", economy.Lost, DeathText),
            ("Stolen", "by thieving enemies", economy.Stolen, DeathText.Darkened(0.2f)),
        }));
        page.AddChild(flows);
    }

    private static ApEconomyRoom RoomOf(RecapFloorEntry floor)
    {
        if (floor.Entry.Rooms.Any(r => ApRunFightsView.IsCombat(r.RoomType)))
            return ApEconomyRoom.Combat;
        return floor.Entry.MapPointType == MapPointType.Shop ? ApEconomyRoom.Shop : ApEconomyRoom.Other;
    }

    private static Control Legend()
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 8);
        row.AddChild(new RunGraphSwatch(RunGraphMarker.Claim, Claim));
        row.AddChild(Label("AP gold claimed", 14, TextMuted));
        row.AddChild(new Control { CustomMinimumSize = new Vector2(10f, 0f) });
        var shop = new Control { CustomMinimumSize = new Vector2(20f, 20f), SizeFlagsVertical = SizeFlags.ShrinkCenter };
        shop.Draw += () => GoldChart.DrawShopMarker(shop, shop.Size / 2f);
        row.AddChild(shop);
        row.AddChild(Label("gold spent in a shop", 14, TextMuted));
        return row;
    }

    private static Control Breakdown(string title, string total, int most,
        IReadOnlyList<(string Name, string Caption, int Amount, Color Color)> rows)
    {
        var panel = ApRunRecapView.Panel(PanelColor, PanelBorder, 14, 18f);
        panel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 12);
        panel.AddChild(box);

        var heading = new HBoxContainer();
        var name = Label(title, 21, Text, bold: true);
        name.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        heading.AddChild(name);
        heading.AddChild(Label(total, 21, Gold, bold: true));
        box.AddChild(heading);

        foreach (var (rowName, rowCaption, amount, color) in rows)
        {
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 14);
            var text = new VBoxContainer { CustomMinimumSize = new Vector2(230f, 0f) };
            text.AddThemeConstantOverride("separation", 0);
            text.AddChild(Label(rowName, 17, Text, bold: true));
            text.AddChild(Label(rowCaption, 13, TextMuted));
            row.AddChild(text);
            var bar = new RecapBar(new[] { (1f, color) }, most > 0 ? (float)amount / most : 0f, 12f)
            {
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                SizeFlagsVertical = SizeFlags.ShrinkCenter,
            };
            row.AddChild(bar);
            var value = Label($"{amount}", 20, amount > 0 ? color : TextFaint, bold: true);
            value.CustomMinimumSize = new Vector2(64f, 0f);
            value.HorizontalAlignment = HorizontalAlignment.Right;
            value.SizeFlagsVertical = SizeFlags.ShrinkCenter;
            row.AddChild(value);
            box.AddChild(row);
        }
        return panel;
    }
}

/// <summary>
/// The run's gold after each floor as a filled line, with acts marked, AP gold claims and shop
/// spending on the floors where they happened, and a readout for the hovered floor.
/// </summary>
internal sealed partial class GoldChart : Control
{
    private const float Left = 58f;
    private const float Right = 18f;
    private const float Top = 40f;
    private const float Bottom = 34f;

    private readonly IReadOnlyList<RunGraphFloor> _floors;
    private readonly IReadOnlyList<ApEconomyFloor> _gold;
    private readonly ApRunEconomy _economy;
    private readonly int _scale;
    private int _hovered = -1;

    internal GoldChart(IReadOnlyList<RunGraphFloor> floors, IReadOnlyList<ApEconomyFloor> gold, ApRunEconomy economy)
    {
        _floors = floors;
        _gold = gold;
        _economy = economy;
        _scale = NiceCeiling(Math.Max(economy.PeakGold, 1));
        MouseFilter = MouseFilterEnum.Stop;
        Draw += DrawChart;
        GuiInput += input =>
        {
            if (input is InputEventMouseMotion motion)
                SetHovered(IndexAt(motion.Position.X));
        };
        MouseExited += () => SetHovered(-1);
    }

    /// <summary>A round number at or above the peak, so the grid lines land on readable values.</summary>
    private static int NiceCeiling(int value)
    {
        foreach (int step in new[] { 50, 100, 250, 500, 1000 })
        {
            if (value <= step * 4)
                return (value + step - 1) / step * step;
        }
        return (value + 999) / 1000 * 1000;
    }

    private float XFor(int index) => _gold.Count <= 1
        ? Left
        : Left + index * (Size.X - Left - Right) / (_gold.Count - 1);

    private float YFor(int gold) => Size.Y - Bottom - (float)gold / _scale * (Size.Y - Bottom - Top);

    private int IndexAt(float x)
    {
        if (_gold.Count == 0 || x < Left - 12f || x > Size.X - Right + 12f)
            return -1;
        float step = _gold.Count <= 1 ? 1f : (Size.X - Left - Right) / (_gold.Count - 1);
        return Math.Clamp((int)MathF.Round((x - Left) / step), 0, _gold.Count - 1);
    }

    internal void SetHovered(int index)
    {
        if (index == _hovered)
            return;
        _hovered = index;
        QueueRedraw();
    }

    private void DrawChart()
    {
        var font = Regular;
        float bottom = Size.Y - Bottom;

        // Grid lines at quarters of the scale, labelled on the left.
        for (int i = 0; i <= 4; i++)
        {
            int value = _scale * i / 4;
            float y = YFor(value);
            DrawLine(new Vector2(Left, y), new Vector2(Size.X - Right, y), Grid, 1f);
            DrawString(font, new Vector2(0f, y + 5f), $"{value}", HorizontalAlignment.Right, Left - 12f, 13, TextFaint);
        }

        // Act boundaries, with the act's name at the top.
        for (int i = 0; i < _floors.Count && i < _gold.Count; i++)
        {
            if (i > 0 && _floors[i].ActIndex == _floors[i - 1].ActIndex)
                continue;
            float x = i == 0 ? Left : (XFor(i - 1) + XFor(i)) / 2f;
            if (i > 0)
                DrawDashedLine(new Vector2(x, Top - 18f), new Vector2(x, bottom), PanelBorder.Lightened(0.15f), 1.5f, 6f);
            DrawString(Bold, new Vector2(x + 8f, Top - 22f), $"ACT {_floors[i].ActIndex + 1}", HorizontalAlignment.Left, -1, 12, TextMuted);
        }

        if (_gold.Count == 0)
            return;
        var points = _gold.Select((floor, i) => new Vector2(XFor(i), YFor(floor.CurrentGold))).ToArray();
        if (points.Length >= 2)
        {
            // One quad per segment, so the fill fades evenly from the line down to the axis.
            var top = Gold with { A = 0.22f };
            var fade = Gold with { A = 0.02f };
            for (int i = 1; i < points.Length; i++)
            {
                DrawPolygon(
                    new[] { points[i - 1], points[i], new Vector2(points[i].X, bottom), new Vector2(points[i - 1].X, bottom) },
                    new[] { top, top, fade, fade });
            }
            DrawPolyline(points, Gold, 2.5f, antialiased: true);
        }
        else
        {
            DrawCircle(points[0], 4f, Gold);
        }

        for (int i = 0; i < _gold.Count; i++)
        {
            if (_gold[i].Room == ApEconomyRoom.Shop && _gold[i].GoldSpent > 0)
                DrawShopMarker(this, points[i]);
        }
        for (int i = 0; i < _gold.Count && i < _economy.ApGoldByFloor.Count; i++)
        {
            if (_economy.ApGoldByFloor[i] > 0)
                DrawClaimMarker(this, points[i] + new Vector2(0f, -8f));
        }

        // Floor numbers along the bottom every few floors.
        int every = _gold.Count > 40 ? 10 : 5;
        for (int i = 0; i < _gold.Count; i++)
        {
            if ((i + 1) % every == 0 || i == 0)
                DrawString(font, new Vector2(XFor(i) - 20f, Size.Y - 10f), $"{i + 1}", HorizontalAlignment.Center, 40f, 12, TextFaint);
        }

        if (_hovered >= 0 && _hovered < _gold.Count)
            DrawReadout(_hovered, points[_hovered]);
    }

    private void DrawReadout(int index, Vector2 point)
    {
        DrawLine(new Vector2(point.X, Top - 6f), new Vector2(point.X, Size.Y - Bottom), Text with { A = 0.25f }, 1f);
        DrawCircle(point, 6f, Ink);
        DrawCircle(point, 4.5f, Gold);

        var floor = _gold[index];
        int ap = index < _economy.ApGoldByFloor.Count ? _economy.ApGoldByFloor[index] : 0;
        var lines = new List<(string Text, Color Color)>
        {
            ($"Floor {index + 1} · {(index < _floors.Count ? _floors[index].RoomName : "")}", Text),
            ($"{floor.CurrentGold} gold", Gold),
        };
        int earned = Math.Max(0, floor.GoldGained - ap);
        if (earned > 0)
            lines.Add(($"+{earned} gold", Gold));
        if (ap > 0)
            lines.Add(($"+{ap} AP gold", Claim));
        if (floor.GoldSpent > 0)
            lines.Add(($"−{floor.GoldSpent} spent", Text));
        if (floor.GoldLost > 0)
            lines.Add(($"−{floor.GoldLost} lost", DeathText));
        if (floor.GoldStolen > 0)
            lines.Add(($"−{floor.GoldStolen} stolen", DeathText));

        const float width = 200f;
        const float lineHeight = 20f;
        float height = lines.Count * lineHeight + 16f;
        float x = point.X + 14f;
        if (x + width > Size.X)
            x = point.X - width - 14f;
        float y = Math.Clamp(point.Y - height / 2f, 0f, Math.Max(0f, Size.Y - height));
        DrawStyleBox(Box(TooltipBg, PanelBorder, 8, 0f), new Rect2(x, y, width, height));
        for (int i = 0; i < lines.Count; i++)
            DrawString(i == 0 ? Bold : Regular, new Vector2(x + 12f, y + 8f + (i + 1) * lineHeight - 5f),
                lines[i].Text, HorizontalAlignment.Left, width - 24f, i == 0 ? 15 : 14, lines[i].Color);
    }

    /// <summary>A small coin with a notch, for gold spent at a shop.</summary>
    internal static void DrawShopMarker(CanvasItem canvas, Vector2 center)
    {
        canvas.DrawCircle(center, 7.5f, Ink);
        canvas.DrawCircle(center, 6f, Text);
        canvas.DrawLine(center + new Vector2(-3f, 0f), center + new Vector2(3f, 0f), Ink, 2f);
    }
}
