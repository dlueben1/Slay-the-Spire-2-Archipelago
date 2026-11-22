using Godot;
using MegaCrit.Sts2.Core.ControllerInput;
using MegaCrit.Sts2.Core.Nodes.CommonUi;

namespace StS2AP.UI.RunGraph;

/// <summary>
/// Hosts the run-graph overlays above the whole game. A child of the game-over screen inherits that
/// screen's layering, and clicks there landed below the buttons they were aimed at; a canvas layer
/// on the scene root draws and takes input in plain screen space.
/// </summary>
internal static class ApOverlayLayer
{
    private static CanvasLayer? _layer;

    /// <summary>
    /// Shows <paramref name="overlay"/> on top of everything and closes it with the game's Back
    /// input. It also closes when <paramref name="owner"/>, the screen that opened it, goes away.
    /// </summary>
    internal static void Show(Control overlay, Node owner, Action close)
    {
        EnsureLayer().AddChild(overlay);

        void CloseWithOwner() => close();
        owner.TreeExiting += CloseWithOwner;
        overlay.TreeExiting += () =>
        {
            if (GodotObject.IsInstanceValid(owner))
                owner.TreeExiting -= CloseWithOwner;
        };
        BindBack(overlay, close);
    }

    /// <summary>
    /// The game turns Escape and the controller's back button into its own actions and gives them
    /// to the newest hotkey binding, which is how its popups close. Blocking first keeps every other
    /// hotkey, such as the pause menu or a back button, from reaching the screen underneath.
    /// The bindings last while <paramref name="overlay"/> is in the tree.
    /// </summary>
    internal static void BindBack(Control overlay, Action close)
    {
        var hotkeys = NHotkeyManager.Instance;
        if (hotkeys == null)
            return;
        hotkeys.AddBlockingScreen(overlay);
        overlay.TreeExiting += () =>
        {
            if (GodotObject.IsInstanceValid(hotkeys))
                hotkeys.RemoveBlockingScreen(overlay);
        };
        BindHotkey(overlay, MegaInput.cancel, close);
        BindHotkey(overlay, MegaInput.pauseAndBack, close);
    }

    /// <summary>
    /// Gives one of the game's inputs to <paramref name="overlay"/> while it is open. Call it after
    /// <see cref="Show"/>, so the binding is newer than the overlay's block on the screens below.
    /// </summary>
    internal static void BindHotkey(Control overlay, string input, Action action)
    {
        var hotkeys = NHotkeyManager.Instance;
        if (hotkeys == null)
            return;
        hotkeys.PushHotkeyPressedBinding(input, action);
        overlay.TreeExiting += () =>
        {
            if (GodotObject.IsInstanceValid(hotkeys))
                hotkeys.RemoveHotkeyPressedBinding(input, action);
        };
    }

    private static CanvasLayer EnsureLayer()
    {
        if (_layer != null && GodotObject.IsInstanceValid(_layer))
            return _layer;
        _layer = new CanvasLayer { Layer = 100, Name = "ApRunGraphLayer" };
        // Deferred, since overlays open from inside other nodes' signal handlers.
        ((SceneTree)Engine.GetMainLoop()).Root.CallDeferred(Node.MethodName.AddChild, _layer);
        return _layer;
    }
}
