using Godot;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs.History;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Runs;
using static StS2AP.UI.RunGraph.RunGraphStyle;

namespace StS2AP.UI.RunGraph;

/// <summary>
/// The recap's Choices tab: how the deck was built. Card rewards taken and passed over, with the
/// AP ones marked as far as the records allow, then campfires, relics, Ancients, shops and the
/// cards removed or transformed.
/// </summary>
internal sealed partial class ApRunChoicesView : ScrollContainer
{
    private const float SideWidth = 600f;
    private static readonly Color Uncommon = new("#8FB4FF");

    private readonly ApRunRecapData _data;
    private readonly List<RecapFloorEntry> _floors;
    private readonly Dictionary<int, int> _apRewards;

    internal ApRunChoicesView(ApRunRecapData data)
    {
        _data = data;
        _floors = data.FloorEntries().Where(f => f.Stats != null).ToList();
        _apRewards = ApCardRewardGroups.ApRewardsByFloor(data.Log, data.Floors.Count);
        SizeFlagsVertical = SizeFlags.ExpandFill;
        HorizontalScrollMode = ScrollMode.Disabled;

        var gutter = new MarginContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        gutter.AddThemeConstantOverride("margin_right", 16);
        AddChild(gutter);
        var page = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        page.AddThemeConstantOverride("separation", 22);
        gutter.AddChild(page);

        var rewards = CardRewards();
        page.AddChild(Tiles(rewards));
        var columns = new HBoxContainer();
        columns.AddThemeConstantOverride("separation", 24);
        page.AddChild(columns);

        var main = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        main.AddThemeConstantOverride("separation", 10);
        main.AddChild(CardRewardsSection(rewards));
        columns.AddChild(main);

        var side = new VBoxContainer { CustomMinimumSize = new Vector2(SideWidth, 0f) };
        side.AddThemeConstantOverride("separation", 18);
        side.AddChild(Campfires());
        AddIfAny(side, Relics());
        AddIfAny(side, Ancients());
        AddIfAny(side, Shops());
        AddIfAny(side, DeckEdits());
        columns.AddChild(side);
    }

    private static void AddIfAny(Container parent, Control? section)
    {
        if (section != null)
            parent.AddChild(section);
    }

    // ---- Card rewards ----

    /// <summary>A card reward as the tab shows it: the floor it was on and its cards, taken ones first.</summary>
    private sealed record Reward(RecapFloorEntry Floor, List<CardChoiceHistoryEntry> Cards)
    {
        internal bool Taken => Cards.Any(c => c.wasPicked);
    }

    private List<Reward> CardRewards()
    {
        var rewards = new List<Reward>();
        foreach (var floor in _floors)
        {
            var choices = floor.Stats!.CardChoices;
            var picked = choices.Select(c => c.wasPicked).ToList();
            // A shop's unbought stock is recorded as cards not picked: it is not a skipped reward.
            var groups = floor.Entry.MapPointType == MapPointType.Shop
                ? ApCardRewardGroups.SplitShop(picked)
                : ApCardRewardGroups.Split(picked);
            foreach (var group in groups)
                rewards.Add(new Reward(floor, choices.Skip(group.Start).Take(group.Count).ToList()));
        }
        return rewards;
    }

    private Control Tiles(List<Reward> rewards)
    {
        var tiles = new GridContainer { Columns = 4 };
        tiles.AddThemeConstantOverride("h_separation", 12);
        int taken = rewards.Count(r => r.Taken);
        tiles.AddChild(ApRunRecapView.Tile($"{rewards.Count}",
            $"card rewards: {taken} taken, {rewards.Count - taken} skipped", Text));
        // The log can hold claims the history lost to a reload on the same floor, so a floor
        // counts no more AP rewards than the history has rewards.
        int fromAp = rewards.GroupBy(r => r.Floor.Index).Sum(f => Math.Min(f.Count(), _apRewards.GetValueOrDefault(f.Key)));
        tiles.AddChild(ApRunRecapView.Tile($"{fromAp}", "of them AP card rewards", Claim));
        int relics = _floors.Sum(f => f.Stats!.RelicChoices.Count(r => r.wasPicked));
        tiles.AddChild(ApRunRecapView.Tile($"{relics}", "relics gained", Gold));
        int removed = _floors.Sum(f => f.Stats!.CardsRemoved.Count);
        int transformed = _floors.Sum(f => f.Stats!.CardsTransformed.Count);
        tiles.AddChild(ApRunRecapView.Tile($"{removed}", $"cards removed, {transformed} transformed", Text));
        return tiles;
    }

    private Control CardRewardsSection(List<Reward> rewards)
    {
        var section = ApRunRecapView.Section("Card rewards", "what was taken and what was passed over");
        var note = Label("The game records AP card rewards like its own, so a floor with both says how many were AP.", 14, TextFaint);
        note.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        section.AddChild(note);
        if (rewards.Count == 0)
        {
            section.AddChild(Label("This run saw no card rewards.", 16, TextMuted));
            return section;
        }

        int lastAct = -1;
        foreach (var floorRewards in rewards.GroupBy(r => r.Floor.Index))
        {
            var floor = floorRewards.First().Floor;
            if (floor.Act != lastAct)
            {
                section.AddChild(ApRunTimelineView.ActHeading(floor.Act));
                lastAct = floor.Act;
            }
            section.AddChild(RewardFloor(floor, floorRewards.ToList()));
        }
        return section;
    }

    private Control RewardFloor(RecapFloorEntry floor, List<Reward> rewards)
    {
        int ap = _apRewards.GetValueOrDefault(floor.Index);
        bool allAp = ap >= rewards.Count;

        var card = ApRunRecapView.Panel(PanelColor, ap > 0 ? Claim with { A = 0.35f } : PanelColor, 10, 0f);
        var style = (StyleBoxFlat)card.GetThemeStylebox("panel");
        style.ContentMarginLeft = style.ContentMarginRight = 16f;
        style.ContentMarginTop = style.ContentMarginBottom = 10f;

        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 16);
        card.AddChild(row);
        var graphFloor = _data.Floors[floor.Index];
        row.AddChild(new RecapRoomIcon(graphFloor.Icon, ApRunFightsView.TypeColor(floor.Entry.Rooms.LastOrDefault()?.RoomType ?? default) ?? PanelBorder, 40f));

        var box = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        box.AddThemeConstantOverride("separation", 4);
        var heading = new HBoxContainer();
        heading.AddThemeConstantOverride("separation", 8);
        var (title, kind) = ApRunTimelineView.Title(floor.Entry, graphFloor);
        heading.AddChild(Caps($"Floor {floor.Index + 1} · {kind}", 11, TextFaint));
        if (ap > 0)
            heading.AddChild(ApRunRecapView.Pill(allAp ? (rewards.Count == 1 ? "AP reward" : "AP rewards") : $"{ap} of {rewards.Count} from AP", Claim));
        box.AddChild(heading);
        if (title != kind)
            box.AddChild(Label(title, 16, TextMuted));
        foreach (var reward in rewards)
            box.AddChild(RewardLine(reward));
        row.AddChild(box);
        return card;
    }

    private static RichTextLabel RewardLine(Reward reward)
    {
        var text = Paragraph(17);
        var taken = reward.Cards.Where(c => c.wasPicked).ToList();
        var passed = reward.Cards.Where(c => !c.wasPicked).ToList();
        if (taken.Count > 0)
        {
            text.PushColor(TextMuted);
            text.AddText("Took ");
            text.Pop();
            AddCards(text, taken, bold: true);
            if (passed.Count > 0)
            {
                text.PushColor(TextFaint);
                text.AddText("   over   ");
                text.Pop();
                AddCards(text, passed, bold: false, dim: true);
            }
        }
        else
        {
            text.PushColor(TextMuted);
            text.AddText("Skipped ");
            text.Pop();
            AddCards(text, passed, bold: false, dim: true);
        }
        return text;
    }

    private static void AddCards(RichTextLabel text, List<CardChoiceHistoryEntry> cards, bool bold, bool dim = false)
    {
        for (int i = 0; i < cards.Count; i++)
        {
            if (i > 0)
            {
                text.PushColor(TextFaint);
                text.AddText(", ");
                text.Pop();
            }
            var (name, color) = Card(cards[i].Card);
            if (bold)
                text.PushBold();
            text.PushColor(dim ? color.Lerp(TextFaint, 0.45f) : color);
            text.AddText(name);
            text.Pop();
            if (bold)
                text.Pop();
        }
    }

    /// <summary>A card's name with its upgrade, in its rarity's color.</summary>
    private static (string Name, Color Color) Card(SerializableCard card)
    {
        if (card.Id == null)
            return ("a card", Text);
        var model = SaveUtil.CardOrDeprecated(card.Id);
        string name = card.CurrentUpgradeLevel > 0 ? model.Title + "+" : model.Title;
        return (name, RarityColor(model.Rarity));
    }

    private static Color RarityColor(CardRarity rarity) => rarity switch
    {
        CardRarity.Uncommon => Uncommon,
        CardRarity.Rare => Gold,
        CardRarity.Ancient => Progression,
        CardRarity.Curse => DeathText,
        _ => Text,
    };

    // ---- Campfires ----

    /// <summary>Rests against smiths, which matter because AP hands out both as progressive items.</summary>
    private Control Campfires()
    {
        var (panel, box) = SidePanel("Campfires", "rests against smiths");
        var visits = _floors
            .Where(f => f.Entry.MapPointType == MapPointType.RestSite || f.Stats!.RestSiteChoices.Count > 0)
            .ToList();
        var choices = visits.SelectMany(f => f.Stats!.RestSiteChoices).Where(c => c != "NOTHING").ToList();
        int rests = choices.Count(c => c == "HEAL");
        int smiths = choices.Count(c => c == "SMITH");
        int checks = choices.Count(IsCampfireCheck);
        int other = choices.Count - rests - smiths - checks;

        var counts = new HBoxContainer();
        counts.AddThemeConstantOverride("separation", 28);
        counts.AddChild(Count($"{rests}", rests == 1 ? "rest" : "rests", Claim));
        counts.AddChild(Count($"{smiths}", smiths == 1 ? "smith" : "smiths", Gold));
        if (checks > 0)
            counts.AddChild(Count($"{checks}", checks == 1 ? "campfire check" : "campfire checks", Progression));
        if (other > 0)
            counts.AddChild(Count($"{other}", "other", Text));
        box.AddChild(counts);
        if (choices.Count > 0)
            box.AddChild(new RecapBar(new[] { ((float)rests, Claim), ((float)smiths, Gold), ((float)checks, Progression), ((float)other, TextMuted) }, 1f, 10f));

        if (visits.Count == 0)
            box.AddChild(Label("No campfires this run.", 15, TextMuted));
        foreach (var visit in visits)
        {
            var stats = visit.Stats!;
            var line = Paragraph(15);
            line.PushColor(TextFaint);
            line.AddText($"Floor {visit.Index + 1}   ");
            line.Pop();
            var parts = new List<(string Text, Color Color)>();
            string upgraded = string.Join(", ", stats.UpgradedCards.Select(ApRunTimelineView.CardName));
            foreach (string choice in stats.RestSiteChoices.Where(c => c != "NOTHING"))
            {
                parts.Add(choice switch
                {
                    "HEAL" => (stats.HpHealed > 0 ? $"Rested, +{stats.HpHealed} HP" : "Rested", Claim),
                    "SMITH" => (upgraded.Length > 0 ? $"Smithed {upgraded}" : "Smithed", Gold),
                    _ when IsCampfireCheck(choice) => ("Campfire check", Progression),
                    _ => (ApRunTimelineView.RestChoice(choice), Text),
                });
            }
            if (parts.Count == 0)
                parts.Add(("Left without a choice", TextMuted));
            for (int i = 0; i < parts.Count; i++)
            {
                if (i > 0)
                {
                    line.PushColor(TextFaint);
                    line.AddText("  ·  ");
                    line.Pop();
                }
                line.PushColor(parts[i].Color);
                line.AddText(parts[i].Text);
                line.Pop();
            }
            box.AddChild(line);
        }
        return panel;
    }

    /// <summary>AP's campfire check option is recorded under the type of item it held.</summary>
    private static bool IsCampfireCheck(string choice) => choice is "PROGRESSION" or "USEFUL" or "FILLER" or "TRAP";

    // ---- Relics and Ancients ----

    private Control? Relics()
    {
        // Shops list their unbought relics as not picked; those are stock, not skipped choices.
        var floors = _floors
            .Where(f => f.Stats!.RelicChoices.Any(r => r.wasPicked || f.Entry.MapPointType != MapPointType.Shop))
            .ToList();
        if (floors.Count == 0)
            return null;
        var (panel, box) = SidePanel("Relics", "taken and passed over");
        foreach (var floor in floors)
        {
            bool shop = floor.Entry.MapPointType == MapPointType.Shop;
            var line = Paragraph(15);
            line.PushColor(TextFaint);
            line.AddText($"Floor {floor.Index + 1}   ");
            line.Pop();
            bool first = true;
            foreach (var choice in floor.Stats!.RelicChoices.Where(r => r.wasPicked))
            {
                AddRelic(line, ApRunRecapData.ToRelic(choice.choice), dim: false, ref first);
            }
            var skipped = shop ? new List<ModelChoiceHistoryEntry>() : floor.Stats.RelicChoices.Where(r => !r.wasPicked).ToList();
            if (skipped.Count > 0)
            {
                line.PushColor(TextFaint);
                line.AddText(first ? "Skipped " : "   over   ");
                line.Pop();
                first = true;
                foreach (var choice in skipped)
                    AddRelic(line, ApRunRecapData.ToRelic(choice.choice), dim: true, ref first);
            }
            box.AddChild(line);
        }
        return panel;
    }

    private static void AddRelic(RichTextLabel line, RecapRelic relic, bool dim, ref bool first)
    {
        if (!first)
        {
            line.PushColor(TextFaint);
            line.AddText(", ");
            line.Pop();
        }
        first = false;
        if (relic.Icon != null)
        {
            line.AddImage(relic.Icon, 22, 22, dim ? new Color(1f, 1f, 1f, 0.45f) : Colors.White, InlineAlignment.Center);
            line.AddText(" ");
        }
        Color color = relic.FromArchipelago ? Claim : Text;
        line.PushColor(dim ? color.Lerp(TextFaint, 0.45f) : color);
        line.AddText(relic.Name);
        line.Pop();
    }

    private Control? Ancients()
    {
        var floors = _floors.Where(f => f.Stats!.AncientChoices.Count > 0).ToList();
        if (floors.Count == 0)
            return null;
        var (panel, box) = SidePanel("Ancients", "the blessing chosen and the ones left");
        foreach (var floor in floors)
        {
            var (title, _) = ApRunTimelineView.Title(floor.Entry, _data.Floors[floor.Index]);
            var line = Paragraph(15);
            line.PushColor(TextFaint);
            line.AddText($"Floor {floor.Index + 1} · {title}   ");
            line.Pop();
            var chosen = floor.Stats!.AncientChoices.Where(c => c.WasChosen).Select(c => ApRunTimelineView.Formatted(c.Title)).Where(t => t.Length > 0).ToList();
            var others = floor.Stats.AncientChoices.Where(c => !c.WasChosen).Select(c => ApRunTimelineView.Formatted(c.Title)).Where(t => t.Length > 0).ToList();
            line.PushBold();
            line.PushColor(Progression);
            line.AddText(chosen.Count > 0 ? string.Join(", ", chosen) : "Nothing chosen");
            line.Pop();
            line.Pop();
            if (others.Count > 0)
            {
                line.PushColor(TextFaint);
                line.AddText("   over   " + string.Join(", ", others));
                line.Pop();
            }
            box.AddChild(line);
        }
        return panel;
    }

    // ---- Shops and deck edits ----

    private Control? Shops()
    {
        var shops = _floors.Where(f => f.Entry.MapPointType == MapPointType.Shop).ToList();
        if (shops.Count == 0)
            return null;
        var (panel, box) = SidePanel("Shops", "what was bought; the rest was only offered");
        foreach (var shop in shops)
        {
            var stats = shop.Stats!;
            var line = Paragraph(15);
            line.PushColor(TextFaint);
            line.AddText($"Floor {shop.Index + 1}   ");
            line.Pop();
            line.PushColor(Gold);
            line.AddText(stats.GoldSpent > 0 ? $"{stats.GoldSpent} gold   " : "Bought nothing   ");
            line.Pop();

            // Bought cards are the cards gained there that were not taken from a reward.
            var rewardCards = stats.CardChoices.Where(c => c.wasPicked).Select(c => c.Card.Id).ToList();
            var bought = new List<string>();
            foreach (var card in stats.CardsGained)
            {
                int match = rewardCards.IndexOf(card.Id);
                if (match >= 0)
                    rewardCards.RemoveAt(match);
                else
                    bought.Add(Card(card).Name);
            }
            bought.AddRange(stats.BoughtRelics.Select(id => ApRunRecapData.ToRelic(id).Name));
            bought.AddRange(stats.BoughtPotions.Select(PotionName));
            if (stats.CardsRemoved.Count > 0)
                bought.Add("removed " + string.Join(", ", stats.CardsRemoved.Select(c => Card(c).Name)));
            line.PushColor(Text);
            line.AddText(string.Join(", ", bought));
            line.Pop();

            var inRewards = ApCardRewardGroups.SplitShop(stats.CardChoices.Select(c => c.wasPicked).ToList())
                .SelectMany(g => Enumerable.Range(g.Start, g.Count))
                .ToHashSet();
            var offered = stats.CardChoices.Where((_, i) => !inRewards.Contains(i)).Select(c => Card(c.Card).Name).ToList();
            if (offered.Count > 0)
            {
                line.PushColor(TextFaint);
                line.AddText((bought.Count > 0 ? "   ·   " : "") + "offered " + string.Join(", ", offered));
                line.Pop();
            }
            box.AddChild(line);
        }
        return panel;
    }

    private static string PotionName(ModelId id)
    {
        try
        {
            return SaveUtil.PotionOrDeprecated(id).Title.GetFormattedText();
        }
        catch (Exception)
        {
            return "a potion";
        }
    }

    private Control? DeckEdits()
    {
        var floors = _floors
            .Where(f => f.Entry.MapPointType != MapPointType.Shop
                && (f.Stats!.CardsRemoved.Count > 0 || f.Stats.CardsTransformed.Count > 0))
            .ToList();
        if (floors.Count == 0)
            return null;
        var (panel, box) = SidePanel("Deck edits", "cards removed and transformed outside shops");
        foreach (var floor in floors)
        {
            var stats = floor.Stats!;
            var line = Paragraph(15);
            line.PushColor(TextFaint);
            line.AddText($"Floor {floor.Index + 1}   ");
            line.Pop();
            var parts = new List<string>();
            if (stats.CardsRemoved.Count > 0)
                parts.Add("Removed " + string.Join(", ", stats.CardsRemoved.Select(c => Card(c).Name)));
            foreach (var transform in stats.CardsTransformed)
                parts.Add($"{Card(transform.OriginalCard).Name} became {Card(transform.FinalCard).Name}");
            line.AddText(string.Join("  ·  ", parts));
            box.AddChild(line);
        }
        return panel;
    }

    // ---- Small builders ----

    private static (Control Panel, VBoxContainer Box) SidePanel(string title, string caption)
    {
        var panel = ApRunRecapView.Panel(PanelColor, PanelBorder, 14, 18f);
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 10);
        panel.AddChild(box);
        var heading = new HBoxContainer();
        heading.AddThemeConstantOverride("separation", 10);
        heading.AddChild(Label(title, 21, Text, bold: true));
        var sub = Label(caption, 14, TextMuted);
        sub.SizeFlagsVertical = SizeFlags.ShrinkEnd;
        heading.AddChild(sub);
        box.AddChild(heading);
        return (panel, box);
    }

    private static Control Count(string value, string caption, Color color)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 8);
        row.AddChild(Label(value, 30, color, bold: true));
        var label = Label(caption, 15, TextMuted);
        label.SizeFlagsVertical = SizeFlags.ShrinkEnd;
        row.AddChild(label);
        return row;
    }

    /// <summary>One wrapping line of colored parts; separate labels in a flow container would be squeezed to nothing.</summary>
    internal static RichTextLabel Paragraph(int size)
    {
        var text = new RichTextLabel
        {
            FitContent = true,
            ScrollActive = false,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        text.AddThemeFontOverride("normal_font", Regular);
        text.AddThemeFontOverride("bold_font", Bold);
        text.AddThemeFontSizeOverride("normal_font_size", size);
        text.AddThemeFontSizeOverride("bold_font_size", size);
        text.AddThemeColorOverride("default_color", Text);
        return text;
    }
}
