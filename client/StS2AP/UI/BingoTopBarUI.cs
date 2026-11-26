using StS2AP.Bingo;
using STS2RitsuLib.Interop.AutoRegistration;
using STS2RitsuLib.TopBar;

namespace StS2AP.UI;

/// <summary>
/// The in-run bingo button beside the Archipelago Rewards button, shown on bingo slots only.
/// Resting the pointer on it (or focusing it with a controller) peeks the board, which replaces
/// the button's hover tip; pressing it pins the board open. The count under the icon is the
/// number of full lines.
/// </summary>
// RitsuLib qualifies owned button stems with the registered mod ID, so this button's
// static_hover_tips keys must use ARCHIPELAGO_TOPBARBUTTON_BINGO, not an AP prefix.
[RegisterOwnedTopBarButton(
    "bingo",
    IconPath = "res://images/ui/bingo_icon.png",
    ButtonOrder = 1)]
public sealed class BingoTopBarHandler : IModTopBarButtonHandler
{
    public void OnClick(ModTopBarButtonContext ctx)
    {
        if (ctx.Button != null)
            BingoBoardUI.TogglePinned(ctx.Button);
    }

    public bool IsVisible(ModTopBarButtonContext ctx)
    {
        bool visible = ctx.Player != null && BingoState.IsActive;
        if (visible && ctx.Button != null)
            BingoBoardUI.HookPeek(ctx.Button);
        return visible;
    }

    public bool IsOpen(ModTopBarButtonContext ctx) => BingoBoardUI.IsPinned;

    public int GetCount(ModTopBarButtonContext ctx) => BingoState.IsActive ? BingoState.CompletedLines : -1;
}
