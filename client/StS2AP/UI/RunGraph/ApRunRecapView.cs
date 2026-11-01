using Godot;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using static StS2AP.UI.RunGraph.RunGraphStyle;

namespace StS2AP.UI.RunGraph;

/// <summary>
/// Builds the end-of-run recap: the run's numbers, highlights, the climb, who the checks helped,
/// and the deck. The overlay splits it into tabs; a saved image draws it as one page.
/// </summary>
internal static class ApRunRecapView
{
    private const int SectionGap = 36;

    /// <summary>Widths of the saved pictures: the tall full recap, and the short graph card.</summary>
    internal const int RecapImageWidth = 1200;
    internal const int GraphImageWidth = 1600;

    /// <summary>
    /// The whole recap as one tall page, for the saved picture: the top bar, the climb, then the
    /// summary that the overlay shows in its Recap tab.
    /// </summary>
    internal static VBoxContainer Build(ApRunRecapData data)
    {
        var page = VBox(SectionGap);
        page.AddChild(TopBar(data));
        page.AddChild(Climb(data));
        AddSummary(page, data);
        return page;
    }

    /// <summary>The overlay's Recap tab: everything but the top bar and the climb, which the overlay shows itself.</summary>
    internal static VBoxContainer BuildSummary(ApRunRecapData data)
    {
        var page = VBox(SectionGap);
        AddSummary(page, data);
        return page;
    }

    private static void AddSummary(VBoxContainer page, ApRunRecapData data)
    {
        page.AddChild(Hero(data));
        if (data.Recap.Highlights.Count > 0)
            page.AddChild(Highlights(data.Recap));
        page.AddChild(Multiworld(data));
        page.AddChild(DeckAndRelics(data));
        page.AddChild(Footer(data));
    }

    /// <summary>
    /// The overlay's first tab: the climb filling the tab, with its legend. Returns the graph too,
    /// whose hovered floors the overlay turns into tooltips.
    /// </summary>
    internal static Control BuildClimbTab(ApRunRecapData data, out ApRunGraphCanvas canvas)
    {
        var box = VBox(14);
        canvas = new ApRunGraphCanvas
        {
            CustomMinimumSize = new Vector2(0f, 400f),
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        canvas.Load(data.Floors, ApRunGraphCanvas.GroupEvents(data.Log, data.Floors.Count), data.Log.Outcome, interactive: true);
        box.AddChild(canvas);

        var bottom = HBox(16);
        var legend = Legend();
        legend.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        bottom.AddChild(legend);
        bottom.AddChild(Label("Hover a floor to see what happened there", 14, TextFaint));
        box.AddChild(bottom);
        return box;
    }

    /// <summary>
    /// The short picture: who played, the run's numbers and the climb, without the rest of the
    /// recap. It is only saved as an image, so its graph is static.
    /// </summary>
    internal static VBoxContainer BuildGraphCard(ApRunRecapData data)
    {
        var page = VBox(20);

        var header = HBox(20);
        Color characterColor = data.Character?.NameColor ?? TextMuted;
        header.AddChild(Portrait(data, characterColor));
        var names = VBox(8);
        names.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        names.AddChild(Ellipsized(Label(data.Log.SlotName, 34, Text, bold: true)));
        var tags = HBox(10);
        tags.AddChild(Pill(CharacterName(data), characterColor));
        var (outcome, outcomeColor) = Outcome(data);
        tags.AddChild(Centered(Label(outcome, 18, outcomeColor, bold: true)));
        var when = Centered(Ellipsized(Label($"{HeroSubtitle(data)} · {data.Date:d MMMM yyyy}", 15, TextMuted)));
        when.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        tags.AddChild(when);
        names.AddChild(tags);
        header.AddChild(Centered(names));
        var stats = HBox(28);
        stats.AddChild(Stat($"{data.Floors.Count}", "Floor", Text));
        stats.AddChild(Stat(FormatTime(data.RunSeconds), "Time", Text));
        stats.AddChild(Stat($"{data.Recap.ChecksSent}", "Checks sent", Gold));
        stats.AddChild(Stat($"{data.Recap.Claims.Count}", "Items claimed", Claim));
        header.AddChild(Centered(stats));
        page.AddChild(header);

        var canvas = new ApRunGraphCanvas { CustomMinimumSize = new Vector2(0f, 460f) };
        canvas.Load(data.Floors, ApRunGraphCanvas.GroupEvents(data.Log, data.Floors.Count), data.Log.Outcome, interactive: false);
        page.AddChild(canvas);
        page.AddChild(Legend());
        page.AddChild(Footer(data));
        return page;
    }

    private static (string Text, Color Color) Outcome(ApRunRecapData data) => data.Log.Outcome switch
    {
        ApRunOutcome.Won => ("Victory", Gold),
        ApRunOutcome.Lost => ("Defeat", DeathText),
        _ => ("Unfinished", TextMuted),
    };

    // ---- Top bar: outcome and the run's numbers, like the game's own top bar ----

    private static Control TopBar(ApRunRecapData data)
    {
        var bar = Panel(PanelColor, PanelBorder, 12, 16f);
        bar.AddChild(TopBarRow(data));
        return bar;
    }

    /// <summary>
    /// The top bar's contents. The overlay also uses it as its header above the tabs, with
    /// <paramref name="withIdentity"/> adding who played, since its tabs have no hero card of their own.
    /// </summary>
    internal static HBoxContainer TopBarRow(ApRunRecapData data, bool withIdentity = false)
    {
        var row = HBox(32);
        if (withIdentity)
            row.AddChild(Identity(data));

        var outcome = HBox(14);
        outcome.AddChild(Centered(Caps("Archipelago", 13, TextMuted, spacing: 3)));
        var (text, color) = Outcome(data);
        outcome.AddChild(Label(text, 36, color, bold: true));
        row.AddChild(withIdentity ? Centered(outcome) : outcome);

        var stats = HBox(28);
        stats.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        stats.AddChild(Stat($"{data.Floors.Count}", "Floor", Text));
        stats.AddChild(Stat(FormatTime(data.RunSeconds), "Time", Text));
        stats.AddChild(Stat($"{data.Ascension}", "Ascension", Text));
        stats.AddChild(Stat($"{data.Recap.ChecksSent}", "Checks sent", Gold));
        stats.AddChild(Stat($"{data.Recap.Claims.Count}", "Items claimed", Claim));
        row.AddChild(withIdentity ? Centered(stats) : stats);

        var when = VBox(2);
        when.AddChild(RightAligned(Label(data.Date.ToString("d MMMM yyyy"), 15, TextMuted)));
        // With the identity shown, the slot name is already there; say what ended a lost run instead.
        if (!withIdentity)
            when.AddChild(RightAligned(Label(data.Log.SlotName, 15, Text)));
        else if (data.Log.Outcome == ApRunOutcome.Lost && data.KilledBy.Length > 0)
            when.AddChild(RightAligned(Label($"Killed by {data.KilledBy}", 15, DeathText)));
        row.AddChild(Centered(when));
        return row;
    }

    /// <summary>A small portrait with the slot name and the character, for the overlay's header.</summary>
    private static Control Identity(ApRunRecapData data)
    {
        Color characterColor = data.Character?.NameColor ?? TextMuted;
        var identity = HBox(14);
        identity.AddChild(Portrait(data, characterColor, 64f));
        var names = VBox(4);
        // Archipelago caps slot names at 16 characters, so the name fits without cutting.
        names.AddChild(Label(data.Log.SlotName, 22, Text, bold: true));
        var pill = Pill(CharacterName(data), characterColor);
        pill.SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin;
        names.AddChild(pill);
        identity.AddChild(Centered(names));
        return identity;
    }

    private static VBoxContainer Stat(string value, string caption, Color color)
    {
        var box = VBox(0);
        box.AddChild(Label(value, 24, color, bold: true));
        box.AddChild(Caps(caption, 11, TextMuted));
        return box;
    }

    // ---- Hero: the player's card and what the run did for the slot ----

    private static Control Hero(ApRunRecapData data)
    {
        var row = HBox(24);
        row.AddChild(HeroCard(data));
        row.AddChild(SlotProgress(data));
        return row;
    }

    private static Control HeroCard(ApRunRecapData data)
    {
        var card = Panel(Card, HeroBorder, 16, 22f);
        var style = (StyleBoxFlat)card.GetThemeStylebox("panel");
        style.ShadowColor = new Color(0f, 0f, 0f, 0.45f);
        style.ShadowSize = 18;
        card.CustomMinimumSize = new Vector2(440f, 0f);
        var box = VBox(18);
        card.AddChild(box);

        Color characterColor = data.Character?.NameColor ?? TextMuted;
        var identity = HBox(18);
        identity.AddChild(Portrait(data, characterColor));
        var names = VBox(8);
        names.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        names.AddChild(Ellipsized(Label(data.Log.SlotName, 30, Text, bold: true)));
        var tags = HBox(8);
        tags.AddChild(Pill(CharacterName(data), characterColor));
        tags.AddChild(Centered(Ellipsized(Label(HeroSubtitle(data), 14, TextMuted))));
        names.AddChild(tags);
        identity.AddChild(Centered(names));
        box.AddChild(identity);

        var total = Panel(PanelColor, PanelBorder, 12, 14f);
        var totalRow = HBox(14);
        totalRow.AddChild(Label($"{data.Recap.ChecksSent}", 72, Gold, bold: true));
        var totalText = VBox(2);
        totalText.AddChild(Caps("Checks sent", 13, TextMuted, spacing: 3));
        totalText.AddChild(Label($"{data.Recap.SentToOthers} to other players, {data.Recap.SentToSelf} to you", 14, TextMuted));
        totalRow.AddChild(Centered(totalText));
        total.AddChild(totalRow);
        box.AddChild(total);

        var types = new GridContainer { Columns = 4 };
        types.AddThemeConstantOverride("h_separation", 12);
        types.AddThemeConstantOverride("v_separation", 10);
        int most = Math.Max(1, data.Recap.SentByType.Values.DefaultIfEmpty(0).Max());
        foreach (var type in ApRunRecap.TypeOrder)
        {
            int count = data.Recap.SentByType.GetValueOrDefault(type);
            types.AddChild(new RunGraphSwatch(RunGraphMarker.Gem, ForItem(type)));
            var name = Label(type.ToString(), 15, Text);
            name.CustomMinimumSize = new Vector2(100f, 0f);
            types.AddChild(name);
            var bar = new RecapBar(new[] { (1f, ForItem(type)) }, (float)count / most, 8f)
            {
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
            };
            types.AddChild(bar);
            var number = Label($"{count}", 17, Text, bold: true);
            number.CustomMinimumSize = new Vector2(34f, 0f);
            number.HorizontalAlignment = HorizontalAlignment.Right;
            types.AddChild(number);
        }
        box.AddChild(types);
        return card;
    }

    private static Control Portrait(ApRunRecapData data, Color border, float size = 112f)
    {
        var frame = Panel(Card.Darkened(0.3f), border, size < 80f ? 8 : 12, size < 80f ? 2f : 4f, borderWidth: 2);
        frame.CustomMinimumSize = new Vector2(size, size);
        frame.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        Texture2D? portrait = null;
        try
        {
            portrait = data.Character?.CharacterSelectIcon;
        }
        catch (Exception)
        {
            // A modded character may have no select-screen picture; the initial stands in.
        }
        if (portrait != null)
        {
            frame.AddChild(new TextureRect
            {
                Texture = portrait,
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
                ClipContents = true,
            });
        }
        else
        {
            var initial = Label(CharacterName(data)[..1], (int)(size * 0.4f), border, bold: true);
            initial.HorizontalAlignment = HorizontalAlignment.Center;
            initial.VerticalAlignment = VerticalAlignment.Center;
            frame.AddChild(initial);
        }
        return frame;
    }

    /// <summary>
    /// A character's small top-bar icon in a frame of <paramref name="color"/>, or the initial of
    /// <paramref name="fallbackName"/> when there is no icon, as for some modded characters.
    /// </summary>
    internal static Control CharacterIcon(CharacterModel? character, string fallbackName, Color color, float size = 52f)
    {
        var frame = Panel(color with { A = 0.14f }, color, 10, 2f, borderWidth: 2);
        frame.CustomMinimumSize = new Vector2(size, size);
        frame.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        Texture2D? icon = null;
        try
        {
            icon = character?.IconTexture;
        }
        catch (Exception)
        {
            // A modded character without a top-bar icon gets its initial instead.
        }
        if (icon != null)
        {
            frame.AddChild(new TextureRect
            {
                Texture = icon,
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            });
        }
        else
        {
            var initial = Label(fallbackName.Length > 0 ? fallbackName[..1] : "?", (int)(size / 2f), color, bold: true);
            initial.HorizontalAlignment = HorizontalAlignment.Center;
            initial.VerticalAlignment = VerticalAlignment.Center;
            frame.AddChild(initial);
        }
        return frame;
    }

    private static string HeroSubtitle(ApRunRecapData data) => data.Log.Outcome switch
    {
        ApRunOutcome.Lost when data.KilledBy.Length > 0 => $"Killed by {data.KilledBy}",
        _ => $"Ascension {data.Ascension}",
    };

    private static Control SlotProgress(ApRunRecapData data)
    {
        var panel = Panel(PanelColor, PanelBorder, 16, 22f);
        panel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        var box = VBox(18);
        panel.AddChild(box);

        var log = data.Log;
        int start = log.SlotChecksAtStart;
        int end = Math.Max(start, log.SlotChecksAtEnd ?? start);
        int total = Math.Max(log.SlotLocationCount, end);

        var heading = HBox(12);
        var caption = Caps("Slot progress", 13, TextMuted, spacing: 3);
        caption.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        heading.AddChild(caption);
        heading.AddChild(Label($"seed …{Tail(log.Seed)}", 14, TextMuted));
        box.AddChild(heading);

        var figure = HBox(10);
        figure.AddChild(Label($"{end}", 54, Text, bold: true));
        figure.AddChild(Bottom(Label(total > 0 ? $"of {total} locations checked" : "locations checked", 22, TextMuted)));
        box.AddChild(figure);

        float before = total > 0 ? (float)start / total : 0f;
        float thisRun = total > 0 ? (float)(end - start) / total : 0f;
        box.AddChild(new RecapBar(new[] { (before, PanelBorder.Lightened(0.15f)), (thisRun, Gold) }, before + thisRun, 14f));

        var key = HBox(20);
        key.AddChild(KeyEntry(PanelBorder.Lightened(0.15f), $"{start} before this run"));
        key.AddChild(KeyEntry(Gold, $"+{end - start} this run"));
        box.AddChild(key);

        box.AddChild(new Control { SizeFlagsVertical = Control.SizeFlags.ExpandFill });
        var tiles = new GridContainer { Columns = 4 };
        tiles.AddThemeConstantOverride("h_separation", 12);
        var recap = data.Recap;
        tiles.AddChild(Tile($"{recap.Claims.Count}", "items claimed from the multiworld", Claim));
        tiles.AddChild(Tile($"{recap.GoldClaimed}", "AP gold claimed", Gold));
        tiles.AddChild(Tile($"{recap.DeathLinksSent}",
            $"DeathLinks sent, {recap.DeathLinksReceived} received", DeathText));
        tiles.AddChild(Tile($"{recap.SentOnUndoneBranch}", "checks sent on a branch a reload undid", Text));
        box.AddChild(tiles);
        return panel;
    }

    private static Control Tile(string value, string caption, Color color)
    {
        var tile = Panel(Card, Card, 10, 14f);
        tile.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        var box = VBox(4);
        box.AddChild(Label(value, 28, color, bold: true));
        box.AddChild(Wrapped(Label(caption, 13, TextMuted)));
        tile.AddChild(box);
        return tile;
    }

    // ---- Highlights: one fact per tile ----

    private static Control Highlights(ApRunRecap recap)
    {
        var section = Section("Highlights", "what this run meant for the multiworld");
        var grid = new GridContainer { Columns = 3 };
        grid.AddThemeConstantOverride("h_separation", 12);
        grid.AddThemeConstantOverride("v_separation", 12);
        foreach (var highlight in recap.Highlights)
            grid.AddChild(HighlightTile(highlight));
        section.AddChild(grid);
        return section;
    }

    private static Control HighlightTile(ApRecapHighlight highlight)
    {
        Color color = highlight.Kind switch
        {
            ApRecapHighlightKind.FirstKey => Progression,
            ApRecapHighlightKind.MostHelped => Claim,
            ApRecapHighlightKind.ClosestCall => DeathText,
            ApRecapHighlightKind.HomeCooking => Gold,
            ApRecapHighlightKind.BestSupplier => Useful,
            _ => TextMuted,
        };
        var tile = Panel(PanelColor, PanelBorder, 12, 14f);
        tile.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        var row = HBox(14);
        tile.AddChild(row);

        var badge = new Control { CustomMinimumSize = new Vector2(48f, 48f), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
        badge.Draw += () =>
        {
            badge.DrawStyleBox(Box(color with { A = 0.14f }, color with { A = 0.14f }, 10, 0f), new Rect2(Vector2.Zero, badge.Size));
            DrawGem(badge, badge.Size / 2f, 11f, color);
        };
        row.AddChild(badge);

        var text = VBox(2);
        text.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        text.AddChild(Label(highlight.Title, 17, color, bold: true));
        text.AddChild(Ellipsized(Label(highlight.Subject, 14, Text)));
        text.AddChild(Ellipsized(Label(highlight.Caption, 13, TextMuted)));
        row.AddChild(text);
        row.AddChild(Centered(Label(highlight.Value, 26, Text, bold: true)));
        return tile;
    }

    // ---- The climb: HP by floor with the checks sent below it ----

    private static Control Climb(ApRunRecapData data)
    {
        var section = Section("The climb", "HP by floor, with every check sent below it");
        var canvas = new ApRunGraphCanvas { CustomMinimumSize = new Vector2(0f, 400f) };
        canvas.Load(data.Floors, ApRunGraphCanvas.GroupEvents(data.Log, data.Floors.Count), data.Log.Outcome, interactive: false);
        section.AddChild(canvas);
        section.AddChild(Legend());
        return section;
    }

    private static Control Legend()
    {
        var row = new HFlowContainer();
        row.AddThemeConstantOverride("h_separation", 8);
        row.AddThemeConstantOverride("v_separation", 6);
        void Add(RunGraphMarker marker, Color color, string text)
        {
            row.AddChild(new RunGraphSwatch(marker, color));
            row.AddChild(Label(text, 14, TextMuted));
            row.AddChild(new Control { CustomMinimumSize = new Vector2(10f, 0f) });
        }

        // Gems are colored by what the checked location held for whoever receives it.
        foreach (var type in ApRunRecap.TypeOrder)
            Add(RunGraphMarker.Gem, ForItem(type), type.ToString());
        Add(RunGraphMarker.DashedRing, Useful, "Sent, then undone by a reload");
        Add(RunGraphMarker.Claim, Claim, "Item claimed");
        Add(RunGraphMarker.Cross, Death, "DeathLink");
        return row;
    }

    // ---- Who the checks helped, and what came back ----

    private static Control Multiworld(ApRunRecapData data)
    {
        var row = HBox(24);

        var sent = Section("Sent to the multiworld", "whose items this run found");
        sent.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        sent.SizeFlagsStretchRatio = 1.25f;
        var players = data.Recap.Players;
        if (players.Count == 0)
            sent.AddChild(Label("No checks were sent this run.", 16, TextMuted));
        int most = players.Count == 0 ? 1 : players.Max(p => p.Checks);
        foreach (var player in players)
            sent.AddChild(PlayerCard(player, data.Log.SlotName, most));
        row.AddChild(sent);

        var claimed = Section("Claimed this run", "items for you");
        claimed.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        var list = Panel(PanelColor, PanelBorder, 12, 16f);
        var rows = VBox(10);
        list.AddChild(rows);
        if (data.Recap.Claims.Count == 0)
            rows.AddChild(Label("Nothing was claimed from the multiworld.", 15, TextMuted));
        foreach (var claim in data.Recap.Claims)
        {
            var line = HBox(12);
            line.AddChild(new RunGraphSwatch(RunGraphMarker.Gem, ForItem(claim.Classification)));
            var item = Ellipsized(Label(claim.ItemName, 15, Text));
            item.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            line.AddChild(item);
            line.AddChild(Label(claim.IsSelf ? "your world" : $"from {claim.OtherPlayer}", 13, TextMuted));
            var floor = Label($"F{claim.Floor}", 13, TextFaint);
            floor.CustomMinimumSize = new Vector2(34f, 0f);
            floor.HorizontalAlignment = HorizontalAlignment.Right;
            line.AddChild(floor);
            rows.AddChild(line);
        }
        claimed.AddChild(list);
        row.AddChild(claimed);
        return row;
    }

    private static Control PlayerCard(ApRecapPlayer player, string slotName, int most)
    {
        var card = Panel(PanelColor, PanelBorder, 12, 16f);
        var box = VBox(10);
        card.AddChild(box);

        var top = HBox(10);
        top.AddChild(Label(player.IsSelf ? $"You ({slotName})" : player.Name, 19, player.IsSelf ? Gold : Text, bold: true));
        var game = Bottom(Ellipsized(Label(player.Game, 14, TextMuted)));
        game.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        top.AddChild(game);
        top.AddChild(Label($"{player.Checks}", 24, Text, bold: true));
        box.AddChild(top);

        var parts = ApRunRecap.TypeOrder
            .Where(type => player.ByType.GetValueOrDefault(type) > 0)
            .Select(type => ((float)player.ByType[type], ForItem(type)))
            .ToArray();
        box.AddChild(new RecapBar(parts, (float)player.Checks / most, 10f));

        var best = HBox(6);
        best.AddChild(Label("Best find:", 14, TextMuted));
        var bestItem = Ellipsized(Label($"{player.BestFind.ItemName}, floor {player.BestFind.Floor}", 14,
            ForItem(player.BestFind.Classification)));
        bestItem.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        best.AddChild(bestItem);
        box.AddChild(best);
        return card;
    }

    // ---- Deck and relics, from the game's run history ----

    private static readonly (string Title, Color Color, Func<CardType, bool> Matches)[] DeckGroups =
    {
        ("Attacks", new Color("#E06A5E"), type => type == CardType.Attack),
        ("Skills", new Color("#5BB878"), type => type == CardType.Skill),
        ("Powers", new Color("#6D9BE0"), type => type == CardType.Power),
        ("Other", new Color("#A77BDB"), type => type is not (CardType.Attack or CardType.Skill or CardType.Power)),
    };

    private static readonly Color Upgraded = new("#7FE0A0");

    private static Control DeckAndRelics(ApRunRecapData data)
    {
        var section = Section("Deck and relics", $"{data.Deck.Sum(c => c.Count)} cards, {data.Relics.Count} relics");
        var row = HBox(16);
        section.AddChild(row);

        foreach (var (title, color, matches) in DeckGroups)
        {
            var cards = data.Deck.Where(card => matches(card.Type)).ToList();
            if (cards.Count == 0)
                continue;
            var column = Panel(PanelColor, PanelBorder, 12, 16f);
            column.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            var list = VBox(6);
            column.AddChild(list);
            list.AddChild(Caps(title, 12, color, spacing: 3));
            foreach (var card in cards)
            {
                var line = HBox(8);
                var strip = new ColorRect { Color = color, CustomMinimumSize = new Vector2(4f, 14f), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
                line.AddChild(strip);
                // Upgraded cards are green, as the game shows them.
                var name = Ellipsized(Label(card.Upgraded ? card.Name + "+" : card.Name, 14, card.Upgraded ? Upgraded : Text));
                name.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
                line.AddChild(name);
                if (card.Count > 1)
                    line.AddChild(Label($"×{card.Count}", 14, TextMuted));
                list.AddChild(line);
            }
            row.AddChild(column);
        }

        var relics = Panel(PanelColor, PanelBorder, 12, 16f);
        relics.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        relics.SizeFlagsStretchRatio = 1.6f;
        var relicBox = VBox(10);
        relics.AddChild(relicBox);
        var relicTitle = HBox(12);
        relicTitle.AddChild(Caps("Relics", 12, new Color("#C9A44A"), spacing: 3));
        if (data.Relics.Any(r => r.FromArchipelago))
            relicTitle.AddChild(Label("AP marks Archipelago's own relics", 13, TextFaint));
        relicBox.AddChild(relicTitle);
        var flow = new HFlowContainer();
        flow.AddThemeConstantOverride("h_separation", 8);
        flow.AddThemeConstantOverride("v_separation", 8);
        foreach (var relic in data.Relics)
            flow.AddChild(RelicChip(relic));
        relicBox.AddChild(flow);
        row.AddChild(relics);
        return section;
    }

    private static Control RelicChip(RecapRelic relic)
    {
        var chip = Panel(Card, Card, 999, 4f);
        var style = (StyleBoxFlat)chip.GetThemeStylebox("panel");
        style.ContentMarginRight = 12f;
        var row = HBox(6);
        chip.AddChild(row);
        if (relic.Icon != null)
        {
            row.AddChild(new TextureRect
            {
                Texture = relic.Icon,
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                CustomMinimumSize = new Vector2(28f, 28f),
            });
        }
        row.AddChild(Centered(Label(relic.Name, 14, Text)));
        if (relic.FromArchipelago)
            row.AddChild(Centered(Label("AP", 11, Claim, bold: true)));
        return chip;
    }

    private static Control Footer(ApRunRecapData data)
    {
        var column = VBox(14);
        column.AddChild(new ColorRect { Color = PanelBorder, CustomMinimumSize = new Vector2(0f, 1f) });
        var row = HBox(12);
        var left = Label($"Seed …{Tail(data.Log.Seed)} · {data.Log.SlotName} · {CharacterName(data)} · Ascension {data.Ascension}", 14, TextFaint);
        left.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        row.AddChild(left);
        row.AddChild(Label("Slay the Spire 2 Archipelago", 14, TextFaint));
        column.AddChild(row);
        return column;
    }

    // ---- Small builders shared by the sections ----

    private static VBoxContainer Section(string title, string caption)
    {
        var section = VBox(14);
        var heading = HBox(12);
        heading.AddChild(Label(title, 26, Text, bold: true));
        heading.AddChild(Bottom(Label(caption, 15, TextMuted)));
        section.AddChild(heading);
        return section;
    }

    internal static PanelContainer Panel(Color background, Color border, int radius, float margin, int borderWidth = 1)
    {
        var panel = new PanelContainer();
        panel.AddThemeStyleboxOverride("panel", Box(background, border, radius, margin, borderWidth));
        return panel;
    }

    internal static Control Pill(string text, Color color)
    {
        var pill = Panel(color with { A = 0.18f }, color with { A = 0.18f }, 999, 3f);
        var style = (StyleBoxFlat)pill.GetThemeStylebox("panel");
        style.ContentMarginLeft = style.ContentMarginRight = 10f;
        pill.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        pill.AddChild(Caps(text, 12, color.Lightened(0.35f)));
        return pill;
    }

    private static Control KeyEntry(Color color, string text)
    {
        var entry = HBox(8);
        entry.AddChild(new ColorRect { Color = color, CustomMinimumSize = new Vector2(10f, 10f), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter });
        entry.AddChild(Label(text, 14, TextMuted));
        return entry;
    }

    private static HBoxContainer HBox(int separation)
    {
        var box = new HBoxContainer();
        box.AddThemeConstantOverride("separation", separation);
        return box;
    }

    private static VBoxContainer VBox(int separation)
    {
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", separation);
        return box;
    }

    private static T Centered<T>(T control) where T : Control
    {
        control.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        return control;
    }

    private static T Bottom<T>(T control) where T : Control
    {
        control.SizeFlagsVertical = Control.SizeFlags.ShrinkEnd;
        return control;
    }

    private static Label RightAligned(Label label)
    {
        label.HorizontalAlignment = HorizontalAlignment.Right;
        return label;
    }

    /// <summary>Lets a label shrink below its text width, cutting the text with an ellipsis.</summary>
    private static Label Ellipsized(Label label)
    {
        label.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        label.ClipText = true;
        return label;
    }

    private static Label Wrapped(Label label)
    {
        label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        return label;
    }

    internal static string CharacterName(ApRunRecapData data)
    {
        if (data.Character != null)
            return data.Character.Title.GetFormattedText();
        string id = data.Log.Character;
        return id.Length == 0 ? "Unknown" : char.ToUpperInvariant(id[0]) + id[1..].ToLowerInvariant();
    }

    internal static string FormatTime(long seconds)
    {
        var time = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return time.TotalHours >= 1 ? $"{(int)time.TotalHours}:{time:mm\\:ss}" : $"{time:m\\:ss}";
    }

    internal static string Tail(string seed) => seed.Length <= 4 ? seed : seed[^4..];
}

/// <summary>A rounded bar: colored parts in proportion to their weights, filling part of a faint track.</summary>
internal sealed partial class RecapBar : Control
{
    internal RecapBar(IReadOnlyList<(float Weight, Color Color)> parts, float fill, float height)
    {
        CustomMinimumSize = new Vector2(0f, height);
        MouseFilter = MouseFilterEnum.Ignore;
        Draw += () =>
        {
            int radius = (int)(height / 2f);
            DrawStyleBox(Box(Track, Track, radius, 0f), new Rect2(Vector2.Zero, Size));
            float total = parts.Sum(p => p.Weight);
            float width = Size.X * Math.Clamp(fill, 0f, 1f);
            if (total <= 0f || width <= 0f)
                return;
            float x = 0f;
            foreach (var (weight, color) in parts)
            {
                float partWidth = width * weight / total;
                if (partWidth > 0.5f)
                    DrawRect(new Rect2(x, 0f, Math.Max(1f, partWidth - 1f), Size.Y), color);
                x += partWidth;
            }
        };
    }
}
