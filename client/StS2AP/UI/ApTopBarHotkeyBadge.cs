using Godot;
using MegaCrit.Sts2.addons.mega_text;
using MegaCrit.Sts2.Core.ControllerInput;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.CommonUi;

namespace StS2AP.UI;

/// <summary>
/// Shows the AP Loot Menu keybind next to the AP top-bar button in keyboard-only mode, the way
/// the game labels the Deck and Map buttons.
///
/// The game draws those labels from a <c>%HotkeyIcon</c> node inside each top-bar button, filled
/// from the game's own input actions. RitsuLib's button has no such node, and our keybind is a
/// RitsuLib runtime hotkey rather than a game action, so this copies the Deck button's badge and
/// writes our binding into it directly.
/// </summary>
internal sealed class ApTopBarHotkeyBadge
{
    private Control? _button;
    private Control? _badge;
    private Label? _label;
    private string? _shownText;
    private bool _attachFailed;

    /// <summary>
    /// Called every frame from the top-bar handler's visibility poll.
    /// </summary>
    public void Update(Control button)
    {
        if (!ReferenceEquals(_button, button))
        {
            // RitsuLib builds a new button for each run's top bar.
            _button = button;
            _badge = null;
            _label = null;
            _shownText = null;
            _attachFailed = false;
        }

        if (_badge == null || !GodotObject.IsInstanceValid(_badge))
        {
            if (_attachFailed || !TryAttach(button))
                return;
        }

        // The game also shows these badges with a controller, but it has no icon for our L3
        // binding, so the badge is keyboard-only.
        bool show = NControllerManager.Instance?.InputType == InputType.KeyboardOnlyMode;
        if (_badge!.Visible != show)
            _badge.Visible = show;
        if (!show)
            return;

        string text = DisplayText(ModSettingsRegistration.ApLootKeyboardBinding);
        if (text == _shownText)
            return;

        _shownText = text;
        if (_label is MegaLabel megaLabel)
            megaLabel.SetTextAutoSize(text);
        else
            _label!.Text = text;
    }

    private bool TryAttach(Control button)
    {
        // The Deck button may not be ready yet on the first frames; try again next frame.
        var deck = NRun.Instance?.GlobalUi?.TopBar?.Deck;
        if (deck == null || !deck.IsNodeReady())
            return false;

        var vanillaBadge = deck.GetNodeOrNull<Control>("%HotkeyIcon");
        if (vanillaBadge == null)
        {
            LogUtility.Warn("Could not find the Deck button's hotkey badge; the AP button will show no keybind.");
            _attachFailed = true;
            return false;
        }

        var badge = (Control)vanillaBadge.Duplicate();

        // NHotkeyIcon finds its parts by unique name when it enters the tree. Nodes the Deck
        // scene owned lose their owner in the copy, so give them to the copy to keep those
        // lookups working.
        AdoptOrphanedDescendants(badge, badge);

        var label = badge.FindChild("KeyboardLabel", recursive: true, owned: false) as Label;
        if (label == null)
        {
            LogUtility.Warn("The Deck button's hotkey badge has no KeyboardLabel; the AP button will show no keybind.");
            badge.QueueFree();
            _attachFailed = true;
            return false;
        }

        // Nothing calls NHotkeyIcon.UpdateInput on the copy, so set the keyboard look once here.
        if (badge.FindChild("ControllerIcon", recursive: true, owned: false) is CanvasItem controllerIcon)
            controllerIcon.Visible = false;
        if (badge.FindChild("KeyboardIcon", recursive: true, owned: false) is CanvasItem keyboardIcon)
            keyboardIcon.Visible = true;

        // Place it where the Deck badge sits relative to the Deck button.
        badge.Name = "ApHotkeyIcon";
        badge.MouseFilter = Control.MouseFilterEnum.Ignore;
        badge.Visible = false;
        button.AddChild(badge);
        badge.Position = vanillaBadge.GlobalPosition - deck.GlobalPosition;

        _badge = badge;
        _label = label;
        return true;
    }

    private static void AdoptOrphanedDescendants(Node node, Node owner)
    {
        foreach (var child in node.GetChildren())
        {
            if (child.Owner == null)
                child.Owner = owner;
            AdoptOrphanedDescendants(child, owner);
        }
    }

    /// <summary>
    /// Matches the game's badge text, which prints the key name and shortens Escape.
    /// </summary>
    private static string DisplayText(string binding) => binding == "Escape" ? "Esc" : binding;
}
