using Godot;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Runs.History;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Runs;
using static StS2AP.UI.RunGraph.RunGraphStyle;

namespace StS2AP.UI.RunGraph;

/// <summary>
/// The recap's Timeline tab: the run floor by floor, with what the game recorded for each floor
/// (the fight, the choices, HP and gold) beside the checks sent, items claimed and DeathLinks
/// from AP's log for that floor.
/// </summary>
internal sealed partial class ApRunTimelineView : VBoxContainer
{
    private readonly ApRunRecapData _data;
    private readonly Dictionary<int, List<ApRunGraphEvent>> _events;
    private readonly VBoxContainer _list = new();
    private readonly List<(Button Button, bool ApOnly)> _filters = new();
    private bool _apOnly;

    internal ApRunTimelineView(ApRunRecapData data)
    {
        _data = data;
        _events = ApRunGraphCanvas.GroupEvents(data.Log, data.Floors.Count);
        AddThemeConstantOverride("separation", 14);
        SizeFlagsVertical = SizeFlags.ExpandFill;

        AddChild(BuildFilters());
        var scroll = new ScrollContainer
        {
            SizeFlagsVertical = SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        // Room on the right for the scroll bar, so it never covers the HP column.
        var gutter = new MarginContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        gutter.AddThemeConstantOverride("margin_right", 16);
        gutter.AddChild(_list);
        scroll.AddChild(gutter);
        AddChild(scroll);
        _list.AddThemeConstantOverride("separation", 0);
        Fill();
    }

    private Control BuildFilters()
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 10);
        foreach (var (title, apOnly) in new[] { ("Every floor", false), ("Floors with AP events", true) })
        {
            var button = ActionButton(title);
            button.CustomMinimumSize = new Vector2(0f, 40f);
            button.AddThemeFontSizeOverride("font_size", 16);
            button.Pressed += () =>
            {
                _apOnly = apOnly;
                Fill();
            };
            _filters.Add((button, apOnly));
            row.AddChild(button);
        }

        var spacer = new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        row.AddChild(spacer);
        int withEvents = _events.Count;
        var summary = Label($"{_data.Floors.Count} floors · {withEvents} with AP events", 15, TextMuted);
        summary.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        row.AddChild(summary);
        return row;
    }

    /// <summary>Rebuilds the list for the chosen filter, with a heading at the start of each act.</summary>
    private void Fill()
    {
        foreach (var (button, apOnly) in _filters)
        {
            Color border = apOnly == _apOnly ? Gold : PanelBorder;
            button.AddThemeStyleboxOverride("normal", Box(apOnly == _apOnly ? Card : PanelColor, border, 10, 8f));
        }
        foreach (var child in _list.GetChildren())
        {
            _list.RemoveChild(child);
            child.QueueFree();
        }

        int index = 0;
        int lastAct = -1;
        for (int act = 0; act < _data.MapHistory.Count; act++)
        {
            foreach (var entry in _data.MapHistory[act])
            {
                if (index >= _data.Floors.Count)
                    break;
                _events.TryGetValue(index, out var events);
                if (!_apOnly || events != null)
                {
                    if (act != lastAct)
                    {
                        _list.AddChild(ActHeading(act));
                        lastAct = act;
                    }
                    _list.AddChild(FloorRow(index, entry, events));
                }
                index++;
            }
        }
        if (_list.GetChildCount() == 0)
            _list.AddChild(Label("No floor in this run had an AP event.", 16, TextMuted));
    }

    internal static Control ActHeading(int act)
    {
        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_top", act == 0 ? 0 : 18);
        margin.AddThemeConstantOverride("margin_bottom", 6);
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 12);
        row.AddChild(Caps($"Act {act + 1}", 14, Gold, spacing: 3));
        var line = new ColorRect { Color = PanelBorder, CustomMinimumSize = new Vector2(0f, 1f) };
        line.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        line.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        row.AddChild(line);
        margin.AddChild(row);
        return margin;
    }

    private Control FloorRow(int index, MapPointHistoryEntry entry, List<ApRunGraphEvent>? events)
    {
        var floor = _data.Floors[index];
        var stats = entry.PlayerStats.FirstOrDefault(p => p.PlayerId == _data.Log.LocalNetId) ?? entry.PlayerStats.FirstOrDefault();
        bool progression = events?.Any(e => e.Kind == ApRunGraphEventKind.CheckSent && !e.Abandoned
            && e.Classification == ApRunGraphItemType.Progression) ?? false;

        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 14);
        row.AddChild(new TimelineRail(floor.Icon, progression ? Progression : PanelBorder));

        var content = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        content.AddThemeConstantOverride("separation", 6);
        var (title, kind) = Title(entry, floor);
        content.AddChild(Caps($"Floor {index + 1} · {kind}", 11, TextFaint));
        content.AddChild(Label(title, 19, Text, bold: true));
        if (stats != null)
        {
            var details = Details(entry, stats);
            if (!details.IsEmpty)
                content.AddChild(details.Label);
        }
        if (events != null)
        {
            var apLines = new VBoxContainer();
            apLines.AddThemeConstantOverride("separation", 4);
            // The same item claimed or sent again on one floor reads as one line with a count.
            foreach (var group in events.GroupBy(EventLineText))
                apLines.AddChild(EventLine(group.First(), group.Key, group.Count()));
            content.AddChild(apLines);
        }
        if (index == _data.Floors.Count - 1)
            AddEnding(content);

        var margin = new MarginContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        margin.AddThemeConstantOverride("margin_top", 10);
        margin.AddThemeConstantOverride("margin_bottom", 14);
        margin.AddChild(content);
        row.AddChild(margin);
        row.AddChild(HpColumn(index));

        // Floors where the run did something for the multiworld stand out from the rest.
        if (events == null)
            return row;
        var card = new PanelContainer();
        var style = Box(PanelColor, PanelColor, 12, 0f);
        style.ContentMarginRight = 14f;
        card.AddThemeStyleboxOverride("panel", style);
        card.AddChild(row);
        return card;
    }

    /// <summary>The room's own name (the encounter or event) and the kind of room it was.</summary>
    internal static (string Title, string Kind) Title(MapPointHistoryEntry entry, RunGraphFloor floor)
    {
        var room = entry.Rooms.FirstOrDefault(r => r.ModelId != null) ?? entry.Rooms.FirstOrDefault();
        string kind = floor.RoomName;
        if (room?.ModelId == null)
            return (kind, kind);
        try
        {
            string title = room.RoomType is RoomType.Monster or RoomType.Elite or RoomType.Boss
                ? SaveUtil.EncounterOrDeprecated(room.ModelId).Title.GetFormattedText()
                : SaveUtil.EventOrDeprecated(room.ModelId).Title.GetFormattedText();
            return (title.Length > 0 ? title : kind, kind);
        }
        catch (Exception)
        {
            // A model from a mod that is no longer installed has no title to show.
            return (kind, kind);
        }
    }

    /// <summary>What the floor did to the run: the fight, healing, choices, cards and gold.</summary>
    private static TimelineDetails Details(MapPointHistoryEntry entry, PlayerMapPointHistoryEntry stats)
    {
        var details = new TimelineDetails();
        bool shop = entry.MapPointType == MapPointType.Shop;

        int turns = entry.Rooms.Sum(room => room.TurnsTaken);
        if (turns > 0)
            details.Add(turns == 1 ? "1 turn" : $"{turns} turns", TextMuted);
        if (stats.DamageTaken > 0)
            details.Add($"took {stats.DamageTaken} damage", DeathText);
        if (stats.HpHealed > 0)
            details.Add($"healed {stats.HpHealed}", Claim);
        if (stats.MaxHpGained > 0)
            details.Add($"+{stats.MaxHpGained} max HP", Claim);
        if (stats.MaxHpLost > 0)
            details.Add($"−{stats.MaxHpLost} max HP", DeathText);

        string upgraded = string.Join(", ", stats.UpgradedCards.Select(CardName));
        foreach (string choice in stats.RestSiteChoices.Where(choice => choice != "NOTHING"))
            details.Add(choice == "SMITH" && upgraded.Length > 0 ? $"Smithed {upgraded}" : RestChoice(choice), Text);
        if (stats.RestSiteChoices.Count == 0 && upgraded.Length > 0)
            details.Add("Upgraded " + upgraded, Text);

        foreach (var ancient in stats.AncientChoices.Where(choice => choice.WasChosen))
        {
            string text = Formatted(ancient.Title);
            if (text.Length > 0)
                details.Add("Chose " + text, Text);
        }

        // A shop's unbought cards are what it offered, not a skipped reward.
        var picked = stats.CardChoices.Where(choice => choice.wasPicked).Select(choice => CardName(choice.Card)).ToList();
        if (picked.Count > 0)
            details.Add((shop ? "Bought " : "Took ") + string.Join(", ", picked), Text);
        else if (!shop && stats.CardChoices.Count > 0)
            details.Add("Skipped " + string.Join(", ", stats.CardChoices.Select(choice => CardName(choice.Card))), TextMuted);

        foreach (var relic in stats.RelicChoices.Where(choice => choice.wasPicked))
            details.AddRelic(ApRunRecapData.ToRelic(relic.choice), shop ? "Bought " : "");
        foreach (var removed in stats.RelicsRemoved)
            details.Add("Lost " + ApRunRecapData.ToRelic(removed).Name, DeathText);

        if (stats.CardsRemoved.Count > 0)
            details.Add("Removed " + string.Join(", ", stats.CardsRemoved.Select(CardName)), Text);
        foreach (var transform in stats.CardsTransformed)
            details.Add($"Transformed {CardName(transform.OriginalCard)} into {CardName(transform.FinalCard)}", Text);

        int gold = stats.GoldGained - stats.GoldSpent - stats.GoldLost - stats.GoldStolen;
        if (gold != 0)
            details.Add(gold > 0 ? $"+{gold} gold" : $"−{-gold} gold", Gold);
        return details;
    }

    private void AddEnding(VBoxContainer content)
    {
        var (text, color) = _data.Log.Outcome switch
        {
            ApRunOutcome.Won => ("Victory", Gold),
            ApRunOutcome.Lost => (_data.KilledBy.Length > 0 ? $"Died to {_data.KilledBy}" : "Died here", DeathText),
            _ => ("", TextMuted),
        };
        if (text.Length > 0)
            content.AddChild(Label(text, 16, color, bold: true));
    }

    private static string EventLineText(ApRunGraphEvent e)
    {
        string text = ApRunRecapOverlay.Describe(e);
        return e.Kind == ApRunGraphEventKind.CheckSent && !e.IsSelf && e.OtherGame.Length > 0
            ? text + $" ({e.OtherGame})"
            : text;
    }

    private static Control EventLine(ApRunGraphEvent e, string text, int count)
    {
        var line = new HBoxContainer();
        line.AddThemeConstantOverride("separation", 8);
        var marker = e.Kind switch
        {
            ApRunGraphEventKind.CheckSent when e.Abandoned => RunGraphMarker.DashedRing,
            ApRunGraphEventKind.CheckSent => RunGraphMarker.Gem,
            ApRunGraphEventKind.ItemClaimed or ApRunGraphEventKind.GoldClaimed => RunGraphMarker.Claim,
            _ => RunGraphMarker.Cross,
        };
        Color color = ApRunRecapOverlay.ColorFor(e);
        line.AddChild(new RunGraphSwatch(marker, color));
        var label = Label(count > 1 ? $"{text}  ×{count}" : text, 15, color);
        label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        label.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        line.AddChild(label);
        return line;
    }

    /// <summary>HP after the floor, and how much it changed since the floor before.</summary>
    private Control HpColumn(int index)
    {
        var floor = _data.Floors[index];
        var column = new VBoxContainer { CustomMinimumSize = new Vector2(96f, 0f) };
        column.AddThemeConstantOverride("separation", 0);
        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_top", 12);
        margin.AddChild(column);

        float fraction = floor.MaxHp > 0 ? (float)floor.Hp / floor.MaxHp : 0f;
        var hp = Label($"{floor.Hp}/{floor.MaxHp}", 17, ForHp(fraction), bold: true);
        hp.HorizontalAlignment = HorizontalAlignment.Right;
        column.AddChild(hp);
        if (index > 0)
        {
            int change = floor.Hp - _data.Floors[index - 1].Hp;
            var delta = Label(change > 0 ? $"+{change}" : change < 0 ? $"−{-change}" : "±0", 14,
                change > 0 ? Claim : change < 0 ? DeathText : TextFaint);
            delta.HorizontalAlignment = HorizontalAlignment.Right;
            column.AddChild(delta);
        }
        return margin;
    }

    internal static string CardName(SerializableCard card)
    {
        if (card.Id == null)
            return "a card";
        string name = SaveUtil.CardOrDeprecated(card.Id).Title;
        return card.CurrentUpgradeLevel > 0 ? name + "+" : name;
    }

    internal static string CardName(ModelId id) => SaveUtil.CardOrDeprecated(id).Title;

    /// <summary>
    /// The game's rest-site option ids, and AP's Campfire check, whose id is the type of item
    /// the check held (see ApRestSiteOption). "NOTHING" is AP's placeholder and is skipped.
    /// </summary>
    internal static string RestChoice(string choice) => choice switch
    {
        "HEAL" => "Rested",
        "SMITH" => "Smithed",
        "PROGRESSION" or "USEFUL" or "FILLER" or "TRAP" => "Campfire check",
        _ => choice.Length == 0 ? "" : char.ToUpperInvariant(choice[0]) + choice[1..].ToLowerInvariant(),
    };

    internal static string Formatted(LocString text)
    {
        try
        {
            return text.GetFormattedText();
        }
        catch (Exception)
        {
            return "";
        }
    }
}

/// <summary>
/// A floor's details as one wrapping paragraph: colored parts separated by dots, with relic icons
/// inline. One rich text label wraps across the row, where separate labels in a flow container
/// would each be squeezed to nothing.
/// </summary>
internal sealed class TimelineDetails
{
    private const int FontSize = 15;
    private bool _first = true;

    internal RichTextLabel Label { get; } = new()
    {
        FitContent = true,
        ScrollActive = false,
        AutowrapMode = TextServer.AutowrapMode.WordSmart,
        SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        MouseFilter = Control.MouseFilterEnum.Ignore,
    };

    internal bool IsEmpty => _first;

    internal TimelineDetails()
    {
        Label.AddThemeFontOverride("normal_font", RunGraphStyle.Regular);
        Label.AddThemeFontSizeOverride("normal_font_size", FontSize);
        Label.AddThemeColorOverride("default_color", RunGraphStyle.Text);
    }

    internal void Add(string text, Color color)
    {
        Separate();
        Label.PushColor(color);
        Label.AddText(text);
        Label.Pop();
    }

    internal void AddRelic(RecapRelic relic, string prefix)
    {
        Separate();
        if (prefix.Length > 0)
            Label.AddText(prefix);
        if (relic.Icon != null)
        {
            Label.AddImage(relic.Icon, 22, 22, inlineAlign: InlineAlignment.Center);
            Label.AddText(" ");
        }
        Label.PushColor(relic.FromArchipelago ? RunGraphStyle.Claim : RunGraphStyle.Text);
        Label.AddText(relic.Name);
        Label.Pop();
    }

    private void Separate()
    {
        if (!_first)
        {
            Label.PushColor(RunGraphStyle.TextFaint);
            Label.AddText("   ·   ");
            Label.Pop();
        }
        _first = false;
    }
}

/// <summary>The timeline's spine: a line down the row with the room's icon on it.</summary>
internal sealed partial class TimelineRail : Control
{
    private const float Width = 64f;
    private const float Icon = 40f;
    private const float Top = 14f;

    internal TimelineRail(Texture2D? icon, Color ring)
    {
        CustomMinimumSize = new Vector2(Width, Icon + Top * 2f);
        MouseFilter = MouseFilterEnum.Ignore;
        Draw += () =>
        {
            float x = Width / 2f;
            DrawLine(new Vector2(x, 0f), new Vector2(x, Size.Y), RunGraphStyle.PanelBorder, 2f);
            var center = new Vector2(x, Top + Icon / 2f);
            DrawCircle(center, Icon / 2f + 3f, RunGraphStyle.Page);
            DrawCircle(center, Icon / 2f + 1f, ring);
            DrawCircle(center, Icon / 2f - 1f, RunGraphStyle.Card);
        };
        if (icon != null)
        {
            var picture = new TextureRect
            {
                Texture = icon,
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                Position = new Vector2((Width - Icon) / 2f + 4f, Top + 4f),
                Size = new Vector2(Icon - 8f, Icon - 8f),
                MouseFilter = MouseFilterEnum.Ignore,
            };
            AddChild(picture);
        }
    }
}
