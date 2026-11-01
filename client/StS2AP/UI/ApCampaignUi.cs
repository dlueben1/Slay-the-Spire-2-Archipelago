using Godot;
using MegaCrit.Sts2.Core.Models;
using StS2AP.UI.RunGraph;
using static StS2AP.UI.RunGraph.RunGraphStyle;

namespace StS2AP.UI;

/// <summary>
/// The shared look of the AP save pickers, in the same dark style as the run recap: a window with
/// a header strip, cards for saves, and buttons that light up on hover or focus.
/// </summary>
internal static class ApCampaignUi
{
    /// <summary>A centered window of the given size. Its content goes in the returned column.</summary>
    internal static VBoxContainer CreateWindow(Control parent, float width, float height)
    {
        var panel = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Stop };
        panel.SetAnchorsPreset(Control.LayoutPreset.Center);
        panel.OffsetLeft = -width / 2f; panel.OffsetRight = width / 2f;
        panel.OffsetTop = -height / 2f; panel.OffsetBottom = height / 2f;
        var style = Box(Page, PanelBorder, 16, 0f, borderWidth: 2);
        style.ShadowColor = new Color(0f, 0f, 0f, 0.6f);
        style.ShadowSize = 28;
        panel.AddThemeStyleboxOverride("panel", style);
        parent.AddChild(panel);

        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 0);
        panel.AddChild(column);
        return column;
    }

    /// <summary>
    /// The window's top strip: an optional picture, a small caption over the title, and whatever
    /// <paramref name="trailing"/> adds on the right.
    /// </summary>
    internal static Control CreateHeader(Control? picture, string caption, string title, Color titleColor, Control? trailing = null)
    {
        var header = new PanelContainer();
        var style = Box(PanelColor, PanelBorder, 0, 0f, borderWidth: 0);
        // Inside the window's 2-pixel border, so its corners sit just inside the window's own.
        style.CornerRadiusTopLeft = style.CornerRadiusTopRight = 14;
        style.BorderWidthBottom = 1;
        style.ContentMarginLeft = style.ContentMarginRight = 32f;
        style.ContentMarginTop = style.ContentMarginBottom = 18f;
        header.AddThemeStyleboxOverride("panel", style);

        var row = HBox(18);
        header.AddChild(row);
        if (picture != null)
            row.AddChild(picture);
        var names = VBox(2);
        names.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        names.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        names.AddChild(Caps(caption, 13, TextMuted, spacing: 3));
        names.AddChild(Label(title, 34, titleColor, bold: true));
        row.AddChild(names);
        if (trailing != null)
        {
            trailing.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
            row.AddChild(trailing);
        }
        return header;
    }

    /// <summary>A section heading with a muted caption beside it.</summary>
    internal static Control CreateSectionHeading(string title, string caption = "")
    {
        var row = HBox(12);
        row.AddChild(Label(title, 22, Text, bold: true));
        if (caption.Length > 0)
        {
            var note = CreateLabel(caption, 15, TextMuted);
            note.SizeFlagsVertical = Control.SizeFlags.ShrinkEnd;
            row.AddChild(note);
        }
        return row;
    }

    internal static Label CreateLabel(
        string text,
        int fontSize,
        Color? color = null,
        HorizontalAlignment alignment = HorizontalAlignment.Left)
    {
        var label = Label(text, fontSize, color ?? Text);
        label.HorizontalAlignment = alignment;
        label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        label.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        return label;
    }

    /// <summary>The recap's buttons: gold for the main action, red for deleting, dark otherwise.</summary>
    internal static Button CreateButton(string text, bool primary = false, bool danger = false)
    {
        Button button = primary ? ActionButton(text, Gold.Darkened(0.55f), Gold)
            : danger ? ActionButton(text, Death.Darkened(0.65f), Death.Darkened(0.2f))
            : ActionButton(text);
        button.CustomMinimumSize = new Vector2(150f, 48f);
        return button;
    }

    /// <summary>
    /// A whole card that acts as one button, for a save that loads on click. The card is
    /// <paramref name="height"/> tall, since a button does not take its size from its content.
    /// </summary>
    internal static Button CreateCard(Control content, float height, Color accent)
    {
        var card = new Button
        {
            FocusMode = Control.FocusModeEnum.All,
            CustomMinimumSize = new Vector2(0f, height),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        card.AddThemeStyleboxOverride("normal", Box(PanelColor, PanelBorder, 12, 0f));
        card.AddThemeStyleboxOverride("hover", Box(Card, accent, 12, 0f, borderWidth: 2));
        card.AddThemeStyleboxOverride("focus", Box(Card, accent, 12, 0f, borderWidth: 2));
        card.AddThemeStyleboxOverride("pressed", Box(Card.Darkened(0.2f), accent, 12, 0f, borderWidth: 2));
        card.AddThemeStyleboxOverride("disabled", Box(PanelColor with { A = 0.55f }, PanelBorder with { A = 0.5f }, 12, 0f));

        var margin = new MarginContainer();
        margin.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        foreach (string side in new[] { "left", "right", "top", "bottom" })
            margin.AddThemeConstantOverride($"margin_{side}", 14);
        margin.AddChild(content);
        card.AddChild(margin);
        // The card itself takes the clicks and the hover.
        IgnoreMouse(margin);
        return card;
    }

    /// <summary>A plain card that holds its own buttons.</summary>
    internal static PanelContainer CreatePanel(Color? border = null, float margin = 18f) =>
        ApRunRecapView.Panel(PanelColor, border ?? PanelBorder, 12, margin);

    internal static Control CreateHpBar(int hp, int maxHp)
    {
        float fraction = maxHp > 0 ? Math.Clamp((float)hp / maxHp, 0f, 1f) : 0f;
        Color color = ForHp(fraction);
        var row = HBox(8);
        row.AddChild(Centered(Caps("HP", 11, TextMuted)));
        row.AddChild(new RecapBar(new[] { (1f, color) }, fraction, 8f)
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
        });
        row.AddChild(Label($"{hp}/{maxHp}", 15, color, bold: true));
        return row;
    }

    /// <summary>The first <paramref name="max"/> relic icons, then how many more there are.</summary>
    internal static HBoxContainer CreateRelicStrip(IReadOnlyList<RecapRelic> relics, int max, float size = 26f)
    {
        var row = HBox(2);
        foreach (var relic in relics.Where(relic => relic.Icon != null).Take(max))
        {
            row.AddChild(new TextureRect
            {
                Texture = relic.Icon,
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                CustomMinimumSize = new Vector2(size, size),
                TooltipText = relic.Name,
            });
        }
        int more = relics.Count - Math.Min(max, relics.Count(relic => relic.Icon != null));
        if (more > 0)
            row.AddChild(Centered(Label($"+{more}", 13, TextMuted)));
        return row;
    }

    internal static CharacterModel? FindCharacter(string id) =>
        ModelDb.AllCharacters.FirstOrDefault(character => character.Id.Entry == id);

    internal static string CharacterName(CharacterModel? character, string fallbackId) =>
        character?.Title.GetFormattedText()
            ?? (fallbackId.Length == 0 ? "Unknown" : char.ToUpperInvariant(fallbackId[0]) + fallbackId[1..].ToLowerInvariant());

    /// <summary>"today 14:05", "yesterday 09:30" or "Oct 29, 22:38", in local time.</summary>
    internal static string FormatSaved(DateTimeOffset when)
    {
        var local = when.ToLocalTime();
        var days = (DateTime.Now.Date - local.Date).Days;
        return days switch
        {
            0 => $"today {local:HH:mm}",
            1 => $"yesterday {local:HH:mm}",
            _ => $"{local:MMM d, HH:mm}",
        };
    }

    internal static void IgnoreMouse(Control root)
    {
        root.MouseFilter = Control.MouseFilterEnum.Ignore;
        foreach (var child in root.GetChildren())
            if (child is Control control)
                IgnoreMouse(control);
    }

    internal static HBoxContainer HBox(int separation)
    {
        var box = new HBoxContainer();
        box.AddThemeConstantOverride("separation", separation);
        return box;
    }

    internal static VBoxContainer VBox(int separation)
    {
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", separation);
        return box;
    }

    internal static T Centered<T>(T control) where T : Control
    {
        control.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        return control;
    }

    internal static MarginContainer Padded(Control content, int horizontal, int vertical)
    {
        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_left", horizontal);
        margin.AddThemeConstantOverride("margin_right", horizontal);
        margin.AddThemeConstantOverride("margin_top", vertical);
        margin.AddThemeConstantOverride("margin_bottom", vertical);
        margin.AddChild(content);
        return margin;
    }
}
