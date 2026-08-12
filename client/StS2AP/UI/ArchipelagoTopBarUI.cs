using Godot;
using STS2RitsuLib.CardPiles.Nodes;
using STS2RitsuLib.Interop.AutoRegistration;
using STS2RitsuLib.TopBar;

namespace StS2AP.UI;

/// <summary>
/// RitsuLib-backed Archipelago Rewards top-bar button. RitsuLib owns placement, tooltip,
/// count-badge rendering, and hover/click/open tweens; this handler supplies AP behavior.
/// ButtonOrder, lower number = closer to BaseDeck
/// </summary>
[RegisterOwnedTopBarButton(
    "rewards",
    IconPath = "res://images/APIcon.png",
    ButtonOrder = 0)]
public sealed class ArchipelagoRewardsTopBarHandler : IModTopBarButtonHandler
{
    // Animation stuff
    private const float OpenRockAngle = 0.12f;
    private const float OpenRockDuration = 0.3f;
    private const float SettleDuration = 0.5f;

    private NModCardPileButton? _button;
    private Tween? _openTween;
    private bool _wasOpen;

    public void OnClick(ModTopBarButtonContext ctx)
    {
        LogUtility.Info("Toggling Archipelago Rewards UI...");

        ArchipelagoRewardUI.Toggle();
    }

    public bool IsVisible(ModTopBarButtonContext ctx)
    {
        // basically unless things have gotten horribly wrong, this will stay true, mainly used for tweening
        bool visible = ctx.Player != null && ArchipelagoClient.Settings != null;
        if (visible && ctx.Button != null)
            UpdateOpenTween(ctx.Button, ArchipelagoRewardUI.IsOpen);
        return visible;
    }

    public bool IsOpen(ModTopBarButtonContext ctx) => ArchipelagoRewardUI.IsOpen;

    // -1 means to hide the number. It's RitsuLib's magic number for their TopBarButtons
    public int GetCount(ModTopBarButtonContext ctx)
    {
        int count = ArchipelagoClient.GetAvailableRewardCount();
        return count > 0 ? Math.Min(count, 999) : -1;
    }

    /// <summary>
    /// RitsuLib 0.4.53 wires IsOpenWhen into its definition but does not poll it from the
    /// action-button node, so retain the advertised open-state rocking behavior locally.
    /// RitsuLib still owns the independent hover and click tweens on the icon itself.
    /// </summary>
    private void UpdateOpenTween(NModCardPileButton button, bool isOpen)
    {
        if (_button != button)
        {
            if (_openTween != null && GodotObject.IsInstanceValid(_openTween))
                _openTween.Kill();

            _button = button;
            _openTween = null;
            _wasOpen = false;
        }

        if (_wasOpen == isOpen)
            return;

        _wasOpen = isOpen;
        if (_openTween != null && GodotObject.IsInstanceValid(_openTween))
            _openTween.Kill();

        button.PivotOffset = button.Size / 2f;
        _openTween = button.CreateTween();

        if (isOpen)
        {
            _openTween.SetLoops();
            _openTween.TweenProperty(button, "rotation", -OpenRockAngle, OpenRockDuration)
                .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
            _openTween.TweenProperty(button, "rotation", OpenRockAngle, OpenRockDuration)
                .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
        }
        else
        {
            _openTween.TweenProperty(button, "rotation", 0f, SettleDuration)
                .SetTrans(Tween.TransitionType.Spring).SetEase(Tween.EaseType.Out);
        }
    }
}
