using Godot;
using STS2RitsuLib.CardPiles.Nodes;
using STS2RitsuLib.Interop.AutoRegistration;
using STS2RitsuLib.TopBar;
using StS2AP.Utils;

namespace StS2AP.UI;

/// <summary>
/// Displays the local player's Archipelago connection state without competing with the reward
/// count on the adjacent AP Rewards button.
/// </summary>
[RegisterOwnedTopBarButton(
    "connection",
    IconPath = "res://images/APIcon.png",
    ButtonOrder = 1)]
public sealed class ArchipelagoConnectionTopBarHandler : IModTopBarButtonHandler
{
    public void OnClick(ModTopBarButtonContext ctx) { }

    public bool IsVisible(ModTopBarButtonContext ctx)
    {
        bool visible = ApConnectionIndicatorPolicy.ShouldShow(
            playerBound: ctx.Player != null,
            settingsAvailable: ArchipelagoClient.Settings != null,
            isLocalMultiplayerGuest: MultiplayerSupport.IsLocalGuest
        );
        if (!visible || ctx.Button == null)
            return visible;

        ApConnectionIndicatorState state = CurrentState();
        ArchipelagoConnectionTopBarUI.UpdateIcon(ctx.Button, state);
        return true;
    }

    public bool IsOpen(ModTopBarButtonContext ctx) => false;

    public int GetCount(ModTopBarButtonContext ctx) => -1;

    private static ApConnectionIndicatorState CurrentState() =>
        ApConnectionIndicatorPolicy.Resolve(
            connected: ArchipelagoClient.IsConnected,
            connecting: ArchipelagoClient.State == ConnectionState.Connecting,
            reconnecting: ArchipelagoClient.State == ConnectionState.Reconnecting
                || ApReconnectController.IsActive
        );
}

internal static class ArchipelagoConnectionTopBarUI
{
    private const string IconName = "ArchipelagoConnectionStatusIcon";

    public static void UpdateIcon(
        NModCardPileButton button,
        ApConnectionIndicatorState state)
    {
        if (!GodotObject.IsInstanceValid(button))
            return;

        var icon = button.GetNodeOrNull<ArchipelagoConnectionStatusIcon>(IconName);
        if (icon == null)
        {
            icon = new ArchipelagoConnectionStatusIcon
            {
                Name = IconName,
                MouseFilter = Control.MouseFilterEnum.Ignore,
                ZIndex = 10,
            };
            button.AddChild(icon);
            icon.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

            // RitsuLib still uses this texture in the hover tip. Hide only its top-bar copy so
            // the state glyph can use the full icon area without the AP logo showing through.
            CanvasItem? registeredIcon = button.GetNodeOrNull<CanvasItem>("Control/Icon");
            if (registeredIcon != null)
                registeredIcon.Visible = false;
        }

        icon.SetState(state);
    }
}

/// <summary>Draws linked, reconnecting, and broken-link states without external icon assets.</summary>
internal sealed partial class ArchipelagoConnectionStatusIcon : Control
{
    private static readonly Color OutlineColor = new(0.04f, 0.04f, 0.04f, 0.95f);
    private static readonly Color ConnectedColor = new(0.18f, 0.72f, 0.28f, 1f);
    private static readonly Color ConnectingColor = new(0.92f, 0.58f, 0.12f, 1f);
    private static readonly Color DisconnectedColor = new(0.88f, 0.16f, 0.16f, 1f);
    private static readonly Color SymbolColor = new(1f, 1f, 1f, 1f);
    private static readonly Color FaintSymbolColor = new(1f, 1f, 1f, 0.28f);

    private ApConnectionIndicatorState? _state;
    private float _spinnerAngle;

    public void SetState(ApConnectionIndicatorState state)
    {
        if (_state == state)
            return;

        _state = state;
        bool isTrying = state is ApConnectionIndicatorState.Connecting
            or ApConnectionIndicatorState.Reconnecting;
        SetProcess(isTrying);
        QueueRedraw();
    }

    public override void _Process(double delta)
    {
        _spinnerAngle = Mathf.PosMod(
            _spinnerAngle + (float)delta * Mathf.Tau * 0.7f,
            Mathf.Tau
        );
        QueueRedraw();
    }

    public override void _Draw()
    {
        float scale = MathF.Min(Size.X, Size.Y) / 80f;
        if (scale <= 0f)
            return;

        var center = Size / 2f;
        ApConnectionIndicatorState state = _state ?? ApConnectionIndicatorState.Disconnected;
        Color background = state switch
        {
            ApConnectionIndicatorState.Connected => ConnectedColor,
            ApConnectionIndicatorState.Connecting or ApConnectionIndicatorState.Reconnecting =>
                ConnectingColor,
            _ => DisconnectedColor,
        };

        DrawCircle(center, 29f * scale, OutlineColor);
        DrawCircle(center, 25.5f * scale, background);

        switch (state)
        {
            case ApConnectionIndicatorState.Connected:
                DrawConnectedLink(center, scale, SymbolColor);
                break;
            case ApConnectionIndicatorState.Connecting:
            case ApConnectionIndicatorState.Reconnecting:
                DrawConnectedLink(center, scale * 0.72f, FaintSymbolColor, outlined: false);
                DrawSpinner(center, scale);
                break;
            default:
                DrawBrokenLink(center, scale);
                break;
        }
    }

    private void DrawConnectedLink(
        Vector2 center,
        float scale,
        Color color,
        bool outlined = true)
    {
        const float rotation = -Mathf.Pi / 4f;
        var direction = new Vector2(Mathf.Cos(rotation), Mathf.Sin(rotation));
        Vector2 firstCenter = center - direction * 7f * scale;
        Vector2 secondCenter = center + direction * 7f * scale;

        if (outlined)
        {
            DrawEllipse(firstCenter, 12.5f * scale, 7.5f * scale, rotation, OutlineColor, 7f * scale);
            DrawEllipse(secondCenter, 12.5f * scale, 7.5f * scale, rotation, OutlineColor, 7f * scale);
        }
        DrawEllipse(firstCenter, 12.5f * scale, 7.5f * scale, rotation, color, 3.5f * scale);
        DrawEllipse(secondCenter, 12.5f * scale, 7.5f * scale, rotation, color, 3.5f * scale);
    }

    private void DrawBrokenLink(Vector2 center, float scale)
    {
        const float rotation = -Mathf.Pi / 4f;
        var direction = new Vector2(Mathf.Cos(rotation), Mathf.Sin(rotation));
        Vector2 firstCenter = center - direction * 12f * scale;
        Vector2 secondCenter = center + direction * 12f * scale;

        DrawEllipseSegment(
            firstCenter,
            12f * scale,
            7f * scale,
            rotation,
            0.72f,
            Mathf.Tau - 0.72f,
            OutlineColor,
            7f * scale
        );
        DrawEllipseSegment(
            secondCenter,
            12f * scale,
            7f * scale,
            rotation,
            Mathf.Pi + 0.72f,
            Mathf.Pi * 3f - 0.72f,
            OutlineColor,
            7f * scale
        );
        DrawEllipseSegment(
            firstCenter,
            12f * scale,
            7f * scale,
            rotation,
            0.72f,
            Mathf.Tau - 0.72f,
            SymbolColor,
            3.5f * scale
        );
        DrawEllipseSegment(
            secondCenter,
            12f * scale,
            7f * scale,
            rotation,
            Mathf.Pi + 0.72f,
            Mathf.Pi * 3f - 0.72f,
            SymbolColor,
            3.5f * scale
        );

        Vector2 perpendicular = new(-direction.Y, direction.X);
        DrawOutlinedLine(
            center + perpendicular * 2f * scale,
            center + perpendicular * 7f * scale,
            scale
        );
        DrawOutlinedLine(
            center - perpendicular * 2f * scale,
            center - perpendicular * 7f * scale,
            scale
        );
    }

    private void DrawSpinner(Vector2 center, float scale)
    {
        const float sweep = Mathf.Tau * 0.72f;
        float radius = 20f * scale;
        DrawArc(
            center,
            radius,
            _spinnerAngle,
            _spinnerAngle + sweep,
            36,
            OutlineColor,
            7f * scale,
            antialiased: true
        );
        DrawArc(
            center,
            radius,
            _spinnerAngle,
            _spinnerAngle + sweep,
            36,
            SymbolColor,
            3.5f * scale,
            antialiased: true
        );

        float endAngle = _spinnerAngle + sweep;
        var end = center + new Vector2(Mathf.Cos(endAngle), Mathf.Sin(endAngle)) * radius;
        DrawCircle(end, 1.75f * scale, SymbolColor);
    }

    private void DrawOutlinedLine(Vector2 from, Vector2 to, float scale)
    {
        DrawLine(from, to, OutlineColor, 6f * scale, antialiased: true);
        DrawLine(from, to, SymbolColor, 3f * scale, antialiased: true);
    }

    private void DrawEllipse(
        Vector2 center,
        float radiusX,
        float radiusY,
        float rotation,
        Color color,
        float width) =>
        DrawEllipseSegment(
            center,
            radiusX,
            radiusY,
            rotation,
            0f,
            Mathf.Tau,
            color,
            width
        );

    private void DrawEllipseSegment(
        Vector2 center,
        float radiusX,
        float radiusY,
        float rotation,
        float startAngle,
        float endAngle,
        Color color,
        float width)
    {
        const int segments = 32;
        float rotationCos = Mathf.Cos(rotation);
        float rotationSin = Mathf.Sin(rotation);
        Vector2 previous = EllipsePoint(startAngle);

        for (int i = 1; i <= segments; i++)
        {
            float angle = Mathf.Lerp(startAngle, endAngle, i / (float)segments);
            Vector2 current = EllipsePoint(angle);
            DrawLine(previous, current, color, width, antialiased: true);
            previous = current;
        }

        Vector2 EllipsePoint(float angle)
        {
            float x = Mathf.Cos(angle) * radiusX;
            float y = Mathf.Sin(angle) * radiusY;
            return center + new Vector2(
                x * rotationCos - y * rotationSin,
                x * rotationSin + y * rotationCos
            );
        }
    }
}
