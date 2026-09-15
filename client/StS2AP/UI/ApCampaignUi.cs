using Godot;

namespace StS2AP.UI;

internal static class ApCampaignUi
{
    internal static Label CreateLabel(
        string text,
        int fontSize,
        HorizontalAlignment alignment = HorizontalAlignment.Left)
    {
        var label = new Label
        {
            Text = text,
            HorizontalAlignment = alignment,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        label.AddThemeFontSizeOverride("font_size", fontSize);
        label.AddThemeColorOverride("font_color", new Color(0.98f, 0.94f, 0.84f));
        return label;
    }

    internal static Button CreateButton(
        string text,
        bool primary = false,
        bool danger = false)
    {
        var button = new Button
        {
            Text = text,
            CustomMinimumSize = new Vector2(190f, 48f),
            FocusMode = Control.FocusModeEnum.All,
        };
        Color normal = danger
            ? new Color(0.46f, 0.16f, 0.1f)
            : primary
                ? new Color(0.13f, 0.48f, 0.53f)
                : new Color(0.29f, 0.16f, 0.09f);
        button.AddThemeStyleboxOverride("normal", CreateButtonStyle(normal));
        button.AddThemeStyleboxOverride("hover", CreateButtonStyle(normal.Lightened(0.12f)));
        button.AddThemeStyleboxOverride("pressed", CreateButtonStyle(normal.Darkened(0.12f)));
        button.AddThemeStyleboxOverride("disabled", CreateButtonStyle(new Color(0.08f, 0.08f, 0.08f, 0.7f)));
        button.AddThemeFontSizeOverride("font_size", 19);
        button.AddThemeColorOverride("font_color", new Color(0.98f, 0.94f, 0.84f));
        return button;
    }

    internal static StyleBoxFlat CreatePanelStyle()
    {
        var style = new StyleBoxFlat
        {
            BgColor = new Color(0.31f, 0.22f, 0.15f, 0.98f),
            BorderColor = new Color(0.17f, 0.1f, 0.06f),
            ShadowColor = new Color(0f, 0f, 0f, 0.65f),
            ShadowSize = 20,
        };
        style.SetBorderWidthAll(3);
        style.SetCornerRadiusAll(12);
        style.SetContentMarginAll(20f);
        return style;
    }

    internal static StyleBoxFlat CreateButtonStyle(Color color)
    {
        var style = new StyleBoxFlat
        {
            BgColor = color,
            BorderColor = new Color(0.17f, 0.1f, 0.06f),
        };
        style.SetBorderWidthAll(2);
        style.SetCornerRadiusAll(10);
        style.SetContentMarginAll(9f);
        return style;
    }

}
