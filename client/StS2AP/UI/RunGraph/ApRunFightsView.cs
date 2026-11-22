using Godot;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Runs.History;
using MegaCrit.Sts2.Core.Saves;
using static StS2AP.UI.RunGraph.RunGraphStyle;

namespace StS2AP.UI.RunGraph;

/// <summary>One combat of the run, from the local player's side.</summary>
internal sealed record RecapFight(
    int Index,
    int Act,
    RoomType Type,
    string Title,
    string Enemies,
    int Turns,
    int Damage,
    int HpBefore,
    int HpAfter,
    int MaxHp,
    Texture2D? Icon);

/// <summary>
/// The recap's Fights tab: every combat with its enemies, turns, damage taken and HP, elites and
/// bosses marked, and the fight that cost the most HP in each act called out.
/// </summary>
internal sealed partial class ApRunFightsView : VBoxContainer
{
    internal static readonly Color EliteColor = new("#F5A524");
    internal static readonly Color BossColor = Death;

    private readonly ApRunRecapData _data;
    private readonly List<RecapFight> _fights;
    private readonly HashSet<int> _toughest;
    private readonly int _mostDamage;
    private readonly VBoxContainer _list = new();
    private readonly List<(Button Button, bool BigOnly)> _filters = new();
    private bool _bigOnly;

    internal ApRunFightsView(ApRunRecapData data)
    {
        _data = data;
        _fights = BuildFights(data);
        // The toughest fight of each act: the most damage taken, the earlier one on a tie.
        _toughest = _fights
            .Where(f => f.Damage > 0)
            .GroupBy(f => f.Act)
            .Select(act => act.OrderByDescending(f => f.Damage).ThenBy(f => f.Index).First().Index)
            .ToHashSet();
        _mostDamage = _fights.Count == 0 ? 0 : _fights.Max(f => f.Damage);

        AddThemeConstantOverride("separation", 14);
        SizeFlagsVertical = SizeFlags.ExpandFill;
        AddChild(BuildFilters());

        var scroll = new ScrollContainer
        {
            SizeFlagsVertical = SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        var gutter = new MarginContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        gutter.AddThemeConstantOverride("margin_right", 16);
        var content = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        content.AddThemeConstantOverride("separation", 18);
        if (_fights.Count > 0)
            content.AddChild(Summary());
        _list.AddThemeConstantOverride("separation", 6);
        content.AddChild(_list);
        gutter.AddChild(content);
        scroll.AddChild(gutter);
        AddChild(scroll);
        Fill();
    }

    internal static List<RecapFight> BuildFights(ApRunRecapData data) => data.FloorEntries()
        .Select(floor => (Floor: floor, Combat: floor.Entry.Rooms.Where(r => IsCombat(r.RoomType)).ToList()))
        .Where(f => f.Combat.Count > 0)
        .Select(f => Fight(data, f.Floor, f.Combat))
        .ToList();

    internal static bool IsCombat(RoomType type) => type is RoomType.Monster or RoomType.Elite or RoomType.Boss;

    private static RecapFight Fight(ApRunRecapData data, RecapFloorEntry entry, List<MapPointRoomHistoryEntry> combat)
    {
        int index = entry.Index;
        var floor = data.Floors[index];
        var stats = entry.Stats;
        var type = combat.Any(r => r.RoomType == RoomType.Boss) ? RoomType.Boss
            : combat.Any(r => r.RoomType == RoomType.Elite) ? RoomType.Elite
            : RoomType.Monster;
        var room = combat.FirstOrDefault(r => r.ModelId != null) ?? combat[0];
        string title = room.ModelId == null ? "" : SafeTitle(() => SaveUtil.EncounterOrDeprecated(room.ModelId).Title.GetFormattedText());
        string enemies = string.Join(", ", combat
            .SelectMany(r => r.MonsterIds)
            .GroupBy(id => id)
            .Select(group =>
            {
                string name = SafeTitle(() => SaveUtil.MonsterOrDeprecated(group.Key).Title.GetFormattedText());
                if (name.Length == 0)
                    name = "an enemy";
                return group.Count() > 1 ? $"{name} ×{group.Count()}" : name;
            }));

        int damage = stats?.DamageTaken ?? 0;
        // HP going in is where the floor before left it; the run's first floor has nothing before it.
        int before = index > 0
            ? data.Floors[index - 1].Hp
            : floor.Hp + damage - (stats?.HpHealed ?? 0);
        return new RecapFight(index, entry.Act, type, title.Length > 0 ? title : floor.RoomName, enemies,
            combat.Sum(r => r.TurnsTaken), damage, Math.Max(0, before), floor.Hp, floor.MaxHp, floor.Icon);
    }

    private static string SafeTitle(Func<string> title)
    {
        try
        {
            return title();
        }
        catch (Exception)
        {
            // A model from a mod that is no longer installed has no title to show.
            return "";
        }
    }

    private Control BuildFilters()
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 10);
        foreach (var (title, bigOnly) in new[] { ("Every fight", false), ("Elites and bosses", true) })
        {
            var button = ActionButton(title);
            button.CustomMinimumSize = new Vector2(0f, 40f);
            button.AddThemeFontSizeOverride("font_size", 16);
            button.Pressed += () =>
            {
                _bigOnly = bigOnly;
                Fill();
            };
            _filters.Add((button, bigOnly));
            row.AddChild(button);
        }
        row.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill });
        int elites = _fights.Count(f => f.Type == RoomType.Elite);
        int bosses = _fights.Count(f => f.Type == RoomType.Boss);
        var summary = Label($"{Plural(_fights.Count, "fight")} · {Plural(elites, "elite")} · {Plural(bosses, "boss", "bosses")}", 15, TextMuted);
        summary.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        row.AddChild(summary);
        return row;
    }

    /// <summary>The run's fighting in four numbers, then the toughest fight of each act.</summary>
    private Control Summary()
    {
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 12);

        var tiles = new GridContainer { Columns = 4 };
        tiles.AddThemeConstantOverride("h_separation", 12);
        int damage = _fights.Sum(f => f.Damage);
        int turns = _fights.Sum(f => f.Turns);
        tiles.AddChild(ApRunRecapView.Tile($"{damage}", "damage taken in fights", DeathText));
        tiles.AddChild(ApRunRecapView.Tile($"{(float)damage / _fights.Count:0.#}", "damage per fight on average", Text));
        tiles.AddChild(ApRunRecapView.Tile($"{turns}", "turns fought", Text));
        int flawless = _fights.Count(f => f.Damage == 0);
        tiles.AddChild(ApRunRecapView.Tile($"{flawless}", "fights without taking damage", Claim));
        box.AddChild(tiles);

        var toughest = _fights.Where(f => _toughest.Contains(f.Index)).OrderBy(f => f.Act).ToList();
        if (toughest.Count > 0)
        {
            var cards = new HBoxContainer();
            cards.AddThemeConstantOverride("separation", 12);
            foreach (var fight in toughest)
                cards.AddChild(ToughestCard(fight));
            box.AddChild(cards);
        }
        return box;
    }

    private static Control ToughestCard(RecapFight fight)
    {
        var card = ApRunRecapView.Panel(PanelColor, Gold with { A = 0.55f }, 12, 16f);
        card.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 14);
        card.AddChild(row);
        row.AddChild(new RecapRoomIcon(fight.Icon, TypeColor(fight.Type) ?? PanelBorder, 44f));

        var text = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        text.AddThemeConstantOverride("separation", 2);
        text.AddChild(Caps($"Act {fight.Act + 1} · toughest fight", 11, Gold, spacing: 2));
        var title = Label(fight.Title, 19, Text, bold: true);
        title.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        title.ClipText = true;
        text.AddChild(title);
        text.AddChild(Label($"Floor {fight.Index + 1} · {Plural(fight.Turns, "turn")}", 14, TextMuted));
        row.AddChild(text);

        var number = new VBoxContainer { SizeFlagsVertical = SizeFlags.ShrinkCenter };
        number.AddThemeConstantOverride("separation", 0);
        var value = Label($"−{fight.Damage}", 30, DeathText, bold: true);
        value.HorizontalAlignment = HorizontalAlignment.Right;
        number.AddChild(value);
        var caption = Label("HP", 13, TextMuted);
        caption.HorizontalAlignment = HorizontalAlignment.Right;
        number.AddChild(caption);
        row.AddChild(number);
        return card;
    }

    private void Fill()
    {
        foreach (var (button, bigOnly) in _filters)
            button.AddThemeStyleboxOverride("normal", Box(bigOnly == _bigOnly ? Card : PanelColor, bigOnly == _bigOnly ? Gold : PanelBorder, 10, 8f));
        foreach (var child in _list.GetChildren())
        {
            _list.RemoveChild(child);
            child.QueueFree();
        }

        int lastAct = -1;
        foreach (var fight in _fights)
        {
            if (_bigOnly && fight.Type == RoomType.Monster)
                continue;
            if (fight.Act != lastAct)
            {
                if (lastAct == -1)
                    _list.AddChild(HeaderRow());
                _list.AddChild(ApRunTimelineView.ActHeading(fight.Act));
                lastAct = fight.Act;
            }
            _list.AddChild(FightRow(fight));
        }
        if (_list.GetChildCount() == 0)
            _list.AddChild(Label(_fights.Count == 0 ? "This run had no fights." : "This run met no elite or boss.", 16, TextMuted));
    }

    private static Control HeaderRow()
    {
        var row = Columns();
        row.AddChild(new Control { CustomMinimumSize = new Vector2(IconColumn, 0f) });
        var fight = Caps("Fight", 11, TextFaint);
        fight.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        row.AddChild(fight);
        row.AddChild(Fixed(Caps("Turns", 11, TextFaint), TurnsColumn, HorizontalAlignment.Right));
        row.AddChild(Fixed(Caps("Damage taken", 11, TextFaint), DamageColumn, HorizontalAlignment.Left));
        row.AddChild(Fixed(Caps("HP before → after", 11, TextFaint), HpColumn, HorizontalAlignment.Right));
        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_left", 16);
        margin.AddThemeConstantOverride("margin_right", 16);
        margin.AddChild(row);
        return margin;
    }

    private const float IconColumn = 48f;
    private const float TurnsColumn = 70f;
    private const float DamageColumn = 330f;
    private const float HpColumn = 190f;

    private static HBoxContainer Columns()
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 20);
        return row;
    }

    private static Control Fixed(Control control, float width, HorizontalAlignment align)
    {
        control.CustomMinimumSize = new Vector2(width, 0f);
        control.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        if (control is Label label)
            label.HorizontalAlignment = align;
        return control;
    }

    private Control FightRow(RecapFight fight)
    {
        bool toughest = _toughest.Contains(fight.Index);
        Color? accent = TypeColor(fight.Type);
        bool died = _data.Log.Outcome == ApRunOutcome.Lost && fight.Index == _data.Floors.Count - 1;

        var card = new PanelContainer();
        var style = Box(PanelColor, toughest ? Gold with { A = 0.55f } : accent ?? PanelColor, 10, 0f);
        style.ContentMarginLeft = style.ContentMarginRight = 16f;
        style.ContentMarginTop = style.ContentMarginBottom = 10f;
        // A colored edge on the left marks elites and bosses at a glance.
        if (accent.HasValue)
            style.BorderWidthLeft = 4;
        if (!toughest && accent.HasValue)
            style.BorderWidthTop = style.BorderWidthRight = style.BorderWidthBottom = 0;
        card.AddThemeStyleboxOverride("panel", style);

        var row = Columns();
        card.AddChild(row);
        var icon = new RecapRoomIcon(fight.Icon, accent ?? PanelBorder, 40f);
        icon.CustomMinimumSize = new Vector2(IconColumn, 44f);
        row.AddChild(icon);

        var info = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ShrinkCenter };
        info.AddThemeConstantOverride("separation", 2);
        var heading = new HBoxContainer();
        heading.AddThemeConstantOverride("separation", 8);
        heading.AddChild(Caps($"Floor {fight.Index + 1}", 11, TextFaint));
        if (fight.Type == RoomType.Elite)
            heading.AddChild(ApRunRecapView.Pill("Elite", EliteColor));
        else if (fight.Type == RoomType.Boss)
            heading.AddChild(ApRunRecapView.Pill("Boss", BossColor));
        if (toughest)
            heading.AddChild(ApRunRecapView.Pill($"Toughest of act {fight.Act + 1}", Gold));
        if (died)
            heading.AddChild(ApRunRecapView.Pill("Died here", DeathText));
        info.AddChild(heading);
        info.AddChild(Label(fight.Title, 18, Text, bold: true));
        if (fight.Enemies.Length > 0 && fight.Enemies != fight.Title)
        {
            var enemies = Label(fight.Enemies, 14, TextMuted);
            enemies.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            info.AddChild(enemies);
        }
        row.AddChild(info);

        row.AddChild(Fixed(Label(fight.Turns > 0 ? $"{fight.Turns}" : "–", 18, Text, bold: true), TurnsColumn, HorizontalAlignment.Right));

        var damage = new HBoxContainer { CustomMinimumSize = new Vector2(DamageColumn, 0f), SizeFlagsVertical = SizeFlags.ShrinkCenter };
        damage.AddThemeConstantOverride("separation", 12);
        float share = _mostDamage > 0 ? (float)fight.Damage / _mostDamage : 0f;
        var bar = new RecapBar(new[] { (1f, fight.Damage > 0 ? DeathText : Claim) }, share, 10f)
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ShrinkCenter,
        };
        damage.AddChild(bar);
        var amount = Label(fight.Damage > 0 ? $"−{fight.Damage}" : "none", 18, fight.Damage > 0 ? DeathText : Claim, bold: true);
        amount.CustomMinimumSize = new Vector2(60f, 0f);
        amount.HorizontalAlignment = HorizontalAlignment.Right;
        damage.AddChild(amount);
        row.AddChild(damage);

        row.AddChild(Fixed(HpChange(fight), HpColumn, HorizontalAlignment.Right));
        return card;
    }

    /// <summary>HP going in, muted, and HP coming out in its health color, as one line.</summary>
    private static Control HpChange(RecapFight fight)
    {
        var text = new RichTextLabel
        {
            BbcodeEnabled = false,
            FitContent = true,
            ScrollActive = false,
            AutowrapMode = TextServer.AutowrapMode.Off,
            MouseFilter = MouseFilterEnum.Ignore,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        text.AddThemeFontOverride("normal_font", Regular);
        text.AddThemeFontOverride("bold_font", Bold);
        text.AddThemeFontSizeOverride("normal_font_size", 16);
        text.AddThemeFontSizeOverride("bold_font_size", 18);
        text.PushColor(TextMuted);
        text.AddText($"{fight.HpBefore}  →  ");
        text.Pop();
        float fraction = fight.MaxHp > 0 ? (float)fight.HpAfter / fight.MaxHp : 0f;
        text.PushBold();
        text.PushColor(ForHp(fraction));
        text.AddText($"{fight.HpAfter}");
        text.Pop();
        text.Pop();
        text.PushColor(TextFaint);
        text.AddText($" / {fight.MaxHp}");
        text.Pop();
        return text;
    }

    internal static Color? TypeColor(RoomType type) => type switch
    {
        RoomType.Elite => EliteColor,
        RoomType.Boss => BossColor,
        _ => null,
    };

    internal static string Plural(int count, string one, string? many = null) =>
        count == 1 ? $"1 {one}" : $"{count} {many ?? one + "s"}";
}

/// <summary>A room's map icon in a ring, as the recap's lists show it.</summary>
internal sealed partial class RecapRoomIcon : Control
{
    internal RecapRoomIcon(Texture2D? icon, Color ring, float size)
    {
        CustomMinimumSize = new Vector2(size, size);
        SizeFlagsVertical = SizeFlags.ShrinkCenter;
        MouseFilter = MouseFilterEnum.Ignore;
        Draw += () =>
        {
            var center = Size / 2f;
            float radius = Math.Min(Size.X, Size.Y) / 2f;
            DrawCircle(center, radius, ring);
            DrawCircle(center, radius - 2f, Card);
            if (icon != null)
            {
                float inner = (radius - 6f) * 2f;
                DrawTextureRect(icon, new Rect2(center - new Vector2(inner, inner) / 2f, new Vector2(inner, inner)), tile: false);
            }
        };
    }
}
