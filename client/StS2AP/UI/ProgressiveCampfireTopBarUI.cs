using Godot;
using StS2AP.Models;
using STS2RitsuLib.CardPiles.Nodes;
using STS2RitsuLib.Interop.AutoRegistration;
using STS2RitsuLib.TopBar;

namespace StS2AP.UI;

/// <summary>
/// Shared implementation for the current-Act Progressive Rest and Progressive Smith indicators.
/// RitsuLib owns the top-bar placement, hover tip, and hover animation; these handlers only add
/// and update a non-interactive check/X badge over the registered icon.
/// It's probably overkill to use a TopBarButton but it also handles all the patching and ordering and automatic
/// positioning for us so I think it's more ergonomic
/// </summary>
public abstract class ProgressiveCampfireTopBarHandler(bool smith) : IModTopBarButtonHandler
{
    /// <summary>The indicators deliberately have no click action.</summary>
    public void OnClick(ModTopBarButtonContext ctx) { }

    public bool IsVisible(ModTopBarButtonContext ctx)
    {
        if (ctx.Player == null)
            return false;

        ArchipelagoSettings settings = ArchipelagoClient.Settings;
        if (!settings.Characters.TryGetValue(ctx.Player.Character.Id.Entry, out var character))
            return false;

        // Rest/Smith access treats any Act after Act 3 as Act 3, matching the rest-site patch.
        int act = Math.Min(ctx.Player.RunState.CurrentActIndex + 1, 3);
        bool enabled = !settings.CampfireSanity ||
            ArchipelagoClient.HasProgressiveCampfireAccess(character.CharOffset, act, smith);

        ProgressiveCampfireTopBarUI.UpdateBadge(ctx.Button, enabled);
        return true;
    }

    public bool IsOpen(ModTopBarButtonContext ctx) => false;

    public int GetCount(ModTopBarButtonContext ctx) => -1;
}

// RitsuLib qualifies owned button stems with the registered mod ID, so these buttons'
// static_hover_tips keys must use ARCHIPELAGO_TOPBARBUTTON_*, not an AP prefix.
[RegisterOwnedTopBarButton(
    "progressive_rest",
    IconPath = "res://images/relics/regal_pillow.png",
    ButtonOrder = 1)]
public sealed class ProgressiveRestTopBarHandler() : ProgressiveCampfireTopBarHandler(smith: false);

[RegisterOwnedTopBarButton(
    "progressive_smith",
    IconPath = "res://images/relics/whetstone.png",
    ButtonOrder = 2)]
public sealed class ProgressiveSmithTopBarHandler() : ProgressiveCampfireTopBarHandler(smith: true);

internal static class ProgressiveCampfireTopBarUI
{
    private const string BadgeName = "ArchipelagoCampfireStatusBadge";

    public static void UpdateBadge(NModCardPileButton? button, bool enabled)
    {
        if (button == null || !GodotObject.IsInstanceValid(button))
            return;

        var badge = button.GetNodeOrNull<CampfireStatusBadge>(BadgeName);
        if (badge == null)
        {
            badge = new CampfireStatusBadge
            {
                Name = BadgeName,
                Position = new Vector2(52f, 50f),
                Size = new Vector2(26f, 26f),
                CustomMinimumSize = new Vector2(26f, 26f),
                MouseFilter = Control.MouseFilterEnum.Ignore,
                ZIndex = 10,
            };
            button.AddChild(badge);
        }

        badge.SetEnabled(enabled);
    }
}

/// <summary>Draws a compact, asset-free check/X badge beside a RitsuLib top-bar icon.</summary>
internal sealed partial class CampfireStatusBadge : Control
{
    private static readonly Color OutlineColor = new(0.04f, 0.04f, 0.04f, 0.95f);
    private static readonly Color EnabledColor = new(0.18f, 0.72f, 0.28f, 1f);
    private static readonly Color DisabledColor = new(0.88f, 0.16f, 0.16f, 1f);
    private static readonly Color SymbolColor = new(1f, 1f, 1f, 1f);

    private bool _enabled;

    public void SetEnabled(bool enabled)
    {
        if (_enabled == enabled)
            return;

        _enabled = enabled;
        QueueRedraw();
    }

    public override void _Draw()
    {
        var center = Size / 2f;
        DrawCircle(center, 12f, OutlineColor);
        DrawCircle(center, 9.5f, _enabled ? EnabledColor : DisabledColor);

        if (_enabled)
        {
            DrawSymbolLine(new Vector2(6.5f, 13f), new Vector2(11f, 17.5f));
            DrawSymbolLine(new Vector2(11f, 17.5f), new Vector2(20f, 8f));
        }
        else
        {
            DrawSymbolLine(new Vector2(7.5f, 7.5f), new Vector2(18.5f, 18.5f));
            DrawSymbolLine(new Vector2(18.5f, 7.5f), new Vector2(7.5f, 18.5f));
        }
    }

    private void DrawSymbolLine(Vector2 from, Vector2 to)
    {
        DrawLine(from, to, OutlineColor, 5f, antialiased: true);
        DrawLine(from, to, SymbolColor, 2.5f, antialiased: true);
    }
}
