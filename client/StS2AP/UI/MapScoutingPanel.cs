using Godot;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using StS2AP.Utils;

namespace StS2AP.UI;

/// <summary>
/// The Map Sight bonus's Scouting list under the map legend: the next fights, the next elite,
/// what the next ? rooms turn into, each kind in the order the player will meet them, and, in a
/// run with Blitz merchants, which merchant the nearest shops hold. It is a
/// child of the legend, so it slides in, fades and hides with the legend's own hotkey. Its text
/// copies the legend's font and ink, and each row uses the legend's icon for that kind of room.
/// </summary>
internal static class MapScoutingPanel
{
    private const string PanelName = "ApScoutingPanel";
    // From the bottom of the legend's paper, which is the legend node itself.
    private const float GapBelowLegend = 4f;
    private const float RowSeparation = 2f;
    // Rows are a little smaller than the legend's own entries, so the list reads as a footnote.
    private const float RowFontScale = 0.85f;

    /// <summary>Builds, fills or removes the panel to match the current run and Map Sight bonus.</summary>
    internal static void Refresh(NMapScreen screen)
    {
        Control legend = screen._mapLegend;
        VBoxContainer? panel = legend.GetNodeOrNull<VBoxContainer>(PanelName);
        if (!MapScouting.IsActive || RunManager.Instance.State is not { } runState)
        {
            panel?.QueueFree();
            return;
        }

        if (panel == null)
        {
            panel = new VBoxContainer { Name = PanelName, MouseFilter = Control.MouseFilterEnum.Ignore };
            panel.AddThemeConstantOverride("separation", (int)RowSeparation);
            legend.AddChild(panel);
        }
        foreach (Node child in panel.GetChildren())
        {
            panel.RemoveChild(child);
            child.QueueFree();
        }
        Fill(panel, screen, runState);
        // The legend lays out its own children first; the panel then goes under the paper.
        Callable.From(() => PlaceBelowLegend(legend, panel)).CallDeferred();
    }

    private static void Fill(VBoxContainer panel, NMapScreen screen, RunState runState)
    {
        Label? legendHeader = screen._mapLegend.GetNodeOrNull<Label>("Header");
        panel.AddChild(TextLike(legendHeader, MapScouting.Text("header"), 1f));

        AddRows(panel, screen, "EnemyLegendItem",
            MapScouting.UpcomingFights(runState, MapScouting.FightsShown).Select(Title));
        AddRows(panel, screen, "EliteLegendItem",
            MapScouting.UpcomingElites(runState, MapScouting.ElitesShown).Select(Title));
        AddRows(panel, screen, "UnknownLegendItem",
            MapScouting.UpcomingUnknownRooms(runState, MapScouting.UnknownRoomsShown).Select(MapScouting.RoomName));
        // Nearest shops that differ share one row; the tip on each shop says which is which.
        IReadOnlyList<string> merchants = MapScouting.NextMerchants(runState);
        if (merchants.Count > 0)
            AddRows(panel, screen, "MerchantLegendItem", [string.Join($" {MapScouting.Text("or")} ", merchants)]);
    }

    private static string Title(EncounterModel encounter) => encounter.Title.GetFormattedText();

    /// <summary>One row per entry, each with the legend icon for its kind of room.</summary>
    private static void AddRows(VBoxContainer panel, NMapScreen screen, string legendItemName, IEnumerable<string> entries)
    {
        Control? legendItem = screen._legendItems.GetNodeOrNull<Control>(legendItemName);
        TextureRect? legendIcon = legendItem?.GetNodeOrNull<TextureRect>("Icon");
        Label? legendLabel = legendItem?.GetNodeOrNull<Label>("MegaLabel");
        foreach (string entry in entries)
        {
            Label label = TextLike(legendLabel, entry, RowFontScale);
            var row = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
            row.AddThemeConstantOverride("separation", 8);
            row.AddChild(IconLike(legendIcon, label.GetThemeFontSize("font_size")));
            row.AddChild(label);
            panel.AddChild(row);
        }
    }

    /// <summary>A plain label in a legend label's font, size, ink and outline.</summary>
    private static Label TextLike(Label? source, string text, float sizeScale)
    {
        var label = new Label { Text = text, MouseFilter = Control.MouseFilterEnum.Ignore };
        if (source == null)
            return label;
        label.AddThemeFontOverride("font", source.GetThemeFont("font"));
        label.AddThemeFontSizeOverride("font_size",
            Math.Max(12, (int)Math.Round(source.GetThemeFontSize("font_size") * sizeScale)));
        label.AddThemeColorOverride("font_color", source.GetThemeColor("font_color"));
        label.AddThemeColorOverride("font_outline_color", source.GetThemeColor("font_outline_color"));
        label.AddThemeConstantOverride("outline_size", source.GetThemeConstant("outline_size"));
        label.Modulate = source.Modulate;
        label.SelfModulate = source.SelfModulate;
        return label;
    }

    /// <summary>The legend's icon for a kind of room, scaled to sit beside a row's text.</summary>
    private static Control IconLike(TextureRect? source, int fontSize)
    {
        float side = fontSize * 1.3f;
        var icon = new TextureRect
        {
            Texture = source?.Texture,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            CustomMinimumSize = new Vector2(side, side),
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        if (source != null)
        {
            icon.Modulate = source.Modulate;
            icon.SelfModulate = source.SelfModulate;
        }
        return icon;
    }

    // The legend node is the paper texture, so its own height is where the paper ends. Its
    // LegendItems box reaches past that, so the panel can't go under its lowest child.
    private static void PlaceBelowLegend(Control legend, Control panel)
    {
        if (!GodotObject.IsInstanceValid(panel) || !GodotObject.IsInstanceValid(legend))
            return;
        Control? items = legend.GetNodeOrNull<Control>("LegendItems");
        panel.Position = new Vector2(items?.Position.X ?? 0f, legend.Size.Y + GapBelowLegend);
    }
}
