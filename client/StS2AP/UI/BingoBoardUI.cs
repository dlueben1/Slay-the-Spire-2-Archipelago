using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.HoverTips;
using StS2AP.Bingo;
using StS2AP.Bingo.Detectors.Run;
using StS2AP.UI.RunGraph;
using static StS2AP.UI.RunGraph.RunGraphStyle;

namespace StS2AP.UI;

/// <summary>
/// The bingo board: the slot's squares in a size-by-size grid. Done squares get a daub, full
/// lines a gold stroke, and a square that would finish a line glows gold. Each square shows its
/// tier as one to three pips and, for a character's own squares, that character's icon.
/// </summary>
/// <remarks>
/// <para>
/// The board slides up from the bottom of the screen in one of two modes. A peek opens while the
/// pointer (or controller focus) rests on the bingo top-bar button, takes no game input and slides
/// away once the pointer leaves both the button and the board. A pinned board dims the screen and
/// stays until Back, a click outside it, its Close button or another press of the top-bar button.
/// Clicking a peek pins it in place.
/// </para>
/// <para>
/// Outside a run there is no top bar: Character Select's goal tracker (its Board button or the AP
/// menu hotkey) and the in-run AP menu's "Bingo board" row open the board pinned. It only reads
/// the board, so it is safe over a run.
/// </para>
/// </remarks>
internal sealed partial class BingoBoardUI : Control
{
    private const float MaxWindowWidth = 1180f;
    private const float MaxWindowHeight = 860f;
    // Keeps the board clear of the top bar, so the button it peeks from stays visible.
    private const float TopClearance = 150f;
    private const float BottomMargin = 28f;
    private const float SideMargin = 40f;
    private const float WindowPadding = 28f;
    private const float HeaderHeight = 64f;
    private const int SectionSeparation = 16;
    private const int CellSeparation = 10;

    private const double SlideInSeconds = 0.32;
    private const double SlideOutSeconds = 0.18;
    // How long a hover must rest on the top-bar button before the board peeks, so a pointer
    // crossing the top bar on its way to the deck or map doesn't flash it.
    private const double PeekHoverDelay = 0.12;
    // How long the pointer may be off both the button and the board before a peek slides away:
    // long enough to cross the gap between them.
    private const double PeekLeaveGrace = 0.25;
    // A pinned board closed from the keyboard or a controller gives focus back to the top-bar
    // button, which would peek again at once.
    private const double PeekCooldownAfterClose = 0.4;

    private const string PeekHookMeta = "ap_bingo_peek_hook";
    private const string RewardMenuButtonName = "ApBingoBoardButton";

    private static BingoBoardUI? _open;
    private static ulong _peekBlockedUntilMs;

    private BingoBoard _board = null!;
    private bool _pinned;
    // The top-bar button a peek belongs to; null for a board opened pinned from a menu.
    private Control? _anchor;
    // A peek opened by controller or keyboard focus stays while the button keeps focus; one opened
    // by hovering follows only the pointer, since a clicked button can keep focus long after.
    private bool _heldByFocus;
    private double _awayFor;

    private ColorRect _dim = null!;
    private PanelContainer _panel = null!;
    private VBoxContainer _column = null!;
    private Label _squareCount = null!;
    private BingoLineGems _lineGems = null!;
    private Button _close = null!;
    private Label _pinHint = null!;
    private Control _fightLegend = null!;
    private Control _actLegend = null!;
    private Control _runLegend = null!;
    private bool _fightRefreshQueued;
    // Null until the grid is built in Ready.
    private BingoLineStrokes? _strokes;
    private readonly List<BingoCellView> _cells = new();
    private HashSet<int>? _shownDone;

    // The overlay layer joins the tree a frame later, so "open" means created and not yet closing.
    internal static bool IsOpen => _open != null && IsInstanceValid(_open) && !_open.IsQueuedForDeletion();

    internal static bool IsPinned => IsOpen && _open!._pinned;

    /// <summary>
    /// Shows the board pinned. It also closes when <paramref name="owner"/>, the screen it was
    /// opened from, leaves the tree. <paramref name="returnFocus"/> gets the focus back when the
    /// board closes, so controller and keyboard players land where they opened it.
    /// </summary>
    internal static void Open(Node owner, Control? returnFocus = null)
    {
        if (IsOpen)
        {
            _open!.Pin();
            return;
        }

        BingoBoardUI? board = Create(owner, anchor: null);
        if (board == null)
            return;
        board.ReturnFocusOnClose(returnFocus);
        board.Pin();
    }

    /// <summary>Top-bar button press: pins a peek, closes a pinned board, otherwise opens one pinned.</summary>
    internal static void TogglePinned(Control button)
    {
        if (IsPinned)
        {
            Close();
            return;
        }
        if (IsOpen)
        {
            _open!.ReturnFocusOnClose(button.HasFocus() ? button : null);
            _open.Pin();
            return;
        }

        BingoBoardUI? board = Create(button, button);
        if (board == null)
            return;
        board.ReturnFocusOnClose(button.HasFocus() ? button : null);
        board.Pin();
    }

    internal static void Toggle(Node owner)
    {
        if (IsOpen)
            Close();
        else
            Open(owner);
    }

    internal static void Close()
    {
        BingoBoardUI? board = _open;
        _open = null;
        if (board != null && IsInstanceValid(board) && !board.IsQueuedForDeletion())
            board.SlideOut();
    }

    /// <summary>Called on slot changes: a board from another slot must not stay open.</summary>
    internal static void RemoveUI() => Close();

    private static bool _refreshQueued;

    /// <summary>
    /// Restyles the open board at the end of the frame. Progress values change many times per
    /// card (damage, cards played), so they ask for one restyle instead of one each.
    /// </summary>
    internal static void RefreshSoon()
    {
        if (_refreshQueued || !IsOpen)
            return;
        _refreshQueued = true;
        Callable.From(() =>
        {
            _refreshQueued = false;
            Refresh();
        }).CallDeferred();
    }

    /// <summary>Restyles the open board after squares were checked.</summary>
    internal static void Refresh()
    {
        if (!IsOpen || _open == null)
            return;
        if (!ReferenceEquals(BingoState.Board, _open._board))
        {
            // The slot's settings were replaced (another slot, or the multiplayer host's frozen copy).
            Close();
            return;
        }
        _open.UpdateCells(animate: true);
    }

    /// <summary>
    /// Makes a top-bar button peek the board on hover and on focus, in place of its hover tip.
    /// Safe to call every frame: each button is hooked once.
    /// </summary>
    internal static void HookPeek(Control button)
    {
        if (button.HasMeta(PeekHookMeta))
            return;
        button.SetMeta(PeekHookMeta, true);

        // RitsuLib connected its own handlers first, so its hover tip already exists here.
        button.MouseEntered += () =>
        {
            NHoverTipSet.Remove(button);
            button.GetTree().CreateTimer(PeekHoverDelay).Timeout += () =>
            {
                if (IsInstanceValid(button) && IsPointerOver(button, grow: 0f))
                    Peek(button, byFocus: false);
            };
        };
        button.FocusEntered += () =>
        {
            NHoverTipSet.Remove(button);
            Peek(button, byFocus: true);
        };
    }

    private static void Peek(Control button, bool byFocus)
    {
        if (IsOpen || Time.GetTicksMsec() < _peekBlockedUntilMs || !button.IsVisibleInTree())
            return;
        if (Create(button, button) is { } board)
            board._heldByFocus = byFocus;
    }

    private static BingoBoardUI? Create(Node owner, Control? anchor)
    {
        if (BingoState.Board is not { } board)
            return null;

        var overlay = new BingoBoardUI { Name = "ApBingoBoard", _board = board, _anchor = anchor };
        overlay.Build();
        _open = overlay;
        ApOverlayLayer.Attach(overlay, owner, Close);
        return overlay;
    }

    /// <summary>
    /// Adds a "Bingo board" row at the top of the in-run AP menu's reward list on bingo slots. The
    /// AP menu's focus graph takes every child of the list, so the row is reachable with a
    /// controller or the keyboard like the rewards below it.
    /// </summary>
    internal static void AddRewardMenuButton(Control screen, Control rewardsContainer)
    {
        if (!BingoState.IsActive || rewardsContainer.GetNodeOrNull(RewardMenuButtonName) != null)
            return;

        Button button = ActionButton("Bingo board");
        button.Name = RewardMenuButtonName;
        button.CustomMinimumSize = new Vector2(360f, 56f);
        button.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
        button.TooltipText = "Show this slot's bingo board.";
        button.Pressed += () => Open(screen, button);
        rewardsContainer.AddChild(button);
        rewardsContainer.MoveChild(button, 0);
    }

    /// <summary>The AP menu's bingo row, if this screen has one.</summary>
    internal static Control? FindRewardMenuButton(Control rewardsContainer) =>
        rewardsContainer.GetNodeOrNull<Control>(RewardMenuButtonName);

    private static bool IsPointerOver(Control control, float grow) =>
        control.GetGlobalRect().Grow(grow).HasPoint(control.GetGlobalMousePosition());

    private void ReturnFocusOnClose(Control? returnFocus)
    {
        if (returnFocus == null)
            return;
        TreeExiting += () => Callable.From(() =>
        {
            if (IsInstanceValid(returnFocus) && returnFocus.IsVisibleInTree())
                returnFocus.GrabFocus();
        }).CallDeferred();
    }

    private void Pin()
    {
        if (_pinned)
            return;
        _pinned = true;

        // A pinned board owns the screen: clicks outside it close it instead of reaching the game.
        MouseFilter = MouseFilterEnum.Stop;
        _dim.MouseFilter = MouseFilterEnum.Stop;
        _dim.Visible = true;
        CreateTween().TweenProperty(_dim, "modulate:a", 1f, 0.2).From(0f);

        _close.Visible = true;
        _pinHint.Visible = false;
        ApOverlayLayer.BindBack(this, Close);

        // Controllers and keyboard-only mode need something focused; Close is the only button.
        if (IsInsideTree())
            _close.GrabFocus();
        else
            Ready += () => _close.GrabFocus();
    }

    private void Build()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        // A peek takes no input outside its panel; Pin switches this on.
        MouseFilter = MouseFilterEnum.Ignore;

        _dim = new ColorRect { Color = Backdrop, MouseFilter = MouseFilterEnum.Ignore, Visible = false };
        _dim.SetAnchorsPreset(LayoutPreset.FullRect);
        _dim.GuiInput += input =>
        {
            if (_pinned && input is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left })
                Close();
        };
        AddChild(_dim);

        _panel = new PanelContainer { MouseFilter = MouseFilterEnum.Stop };
        var panelStyle = Box(Page, HeroBorder, 18, WindowPadding, borderWidth: 2);
        panelStyle.ShadowColor = new Color(0f, 0f, 0f, 0.65f);
        panelStyle.ShadowSize = 30;
        panelStyle.ShadowOffset = new Vector2(0f, 8f);
        _panel.AddThemeStyleboxOverride("panel", panelStyle);
        _panel.GuiInput += input =>
        {
            if (!_pinned && input is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left })
                Pin();
        };
        AddChild(_panel);

        // The header exists from the start because Pin, which runs before the overlay layer is in
        // the tree when the board opens pinned, shows its Close button.
        _column = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        _column.AddThemeConstantOverride("separation", SectionSeparation);
        _panel.AddChild(_column);
        _column.AddChild(BuildHeader());

        // The grid's size depends on the viewport, which is only known once the layer is in the tree.
        Ready += () =>
        {
            LayOut();
            SlideIn();
            UpdateCells(animate: false);
        };
        TreeEntered += () =>
        {
            GetTree().ProcessFrame += TrackPeekHover;
            CombatManager.Instance.History.Changed += QueueFightRefresh;
        };
        TreeExiting += () =>
        {
            GetTree().ProcessFrame -= TrackPeekHover;
            CombatManager.Instance.History.Changed -= QueueFightRefresh;
        };
    }

    /// <summary>
    /// A fight streak can break while the board is open (a hit on the enemies' turn while it is
    /// peeked). The combat history changes many times per card, so the restyle waits for the end
    /// of the frame and runs once.
    /// </summary>
    private void QueueFightRefresh()
    {
        if (_fightRefreshQueued)
            return;
        _fightRefreshQueued = true;
        Callable.From(() =>
        {
            _fightRefreshQueued = false;
            if (IsInstanceValid(this) && ReferenceEquals(_open, this))
                Refresh();
        }).CallDeferred();
    }

    private void LayOut()
    {
        Vector2 viewport = GetViewportRect().Size;
        float width = Math.Min(MaxWindowWidth, viewport.X - (2 * SideMargin));
        float height = Math.Min(MaxWindowHeight, viewport.Y - TopClearance - BottomMargin);
        _panel.Size = new Vector2(width, height);
        _panel.Position = new Vector2((viewport.X - width) / 2f, viewport.Y - height - BottomMargin);

        float gridWidth = width - (2 * WindowPadding);
        float gridHeight = height - (2 * WindowPadding) - HeaderHeight - SectionSeparation;
        _column.AddChild(BuildGrid(new Vector2(gridWidth, gridHeight)));
    }

    private Control BuildHeader()
    {
        var header = new HBoxContainer
        {
            CustomMinimumSize = new Vector2(0f, HeaderHeight),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        header.AddThemeConstantOverride("separation", 18);

        Label title = Label("Bingo", 40, Gold, bold: true);
        title.VerticalAlignment = VerticalAlignment.Center;
        header.AddChild(title);

        // Lines toward the goal as gems: the number that matters most, with no words to read.
        _lineGems = new BingoLineGems(_board.Lines)
        {
            SizeFlagsVertical = SizeFlags.ShrinkCenter,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        header.AddChild(_lineGems);

        _squareCount = Label("", 18, TextMuted);
        _squareCount.VerticalAlignment = VerticalAlignment.Center;
        _squareCount.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        header.AddChild(_squareCount);

        // Shown only while some square's streak is broken, so the colours explain themselves.
        _fightLegend = LegendEntry(BingoCellView.BrokenThisFightColor, "Next fight");
        _actLegend = LegendEntry(BingoCellView.BrokenThisActColor, "Next act");
        _runLegend = LegendEntry(BingoCellView.BrokenThisRunColor, "Next run");
        header.AddChild(_fightLegend);
        header.AddChild(_actLegend);
        header.AddChild(_runLegend);

        _pinHint = Caps("Click to keep open", 12, TextFaint);
        _pinHint.VerticalAlignment = VerticalAlignment.Center;
        header.AddChild(_pinHint);

        _close = ActionButton("Close");
        _close.Pressed += Close;
        _close.Visible = false;
        header.AddChild(_close);
        return header;
    }

    private static Control LegendEntry(Color color, string text)
    {
        var entry = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore, Visible = false };
        entry.AddThemeConstantOverride("separation", 6);
        entry.AddChild(new BrokenStreakBadge(color)
        {
            CustomMinimumSize = new Vector2(22f, 22f),
            SizeFlagsVertical = SizeFlags.ShrinkCenter,
            MouseFilter = MouseFilterEnum.Ignore,
        });
        Label label = Caps(text, 12, color);
        label.VerticalAlignment = VerticalAlignment.Center;
        entry.AddChild(label);
        return entry;
    }

    private Control BuildGrid(Vector2 area)
    {
        int size = _board.Size;
        var cellSize = new Vector2(
            (area.X - (CellSeparation * (size - 1))) / size,
            (area.Y - (CellSeparation * (size - 1))) / size);
        // Larger boards get smaller text; the cell shrinks it further when a square's text is long.
        int maxFontSize = size switch { <= 3 => 30, 4 => 26, 5 => 22, _ => 20 };

        var host = new Control { CustomMinimumSize = area, MouseFilter = MouseFilterEnum.Ignore };
        var grid = new GridContainer { Columns = size, MouseFilter = MouseFilterEnum.Ignore };
        grid.AddThemeConstantOverride("h_separation", CellSeparation);
        grid.AddThemeConstantOverride("v_separation", CellSeparation);
        host.AddChild(grid);

        foreach (BingoSquareInfo square in _board.Squares)
        {
            var cell = new BingoCellView(square, cellSize, maxFontSize, CharacterIconFor(square));
            grid.AddChild(cell);
            _cells.Add(cell);
        }

        _strokes = new BingoLineStrokes(_cells) { MouseFilter = MouseFilterEnum.Ignore };
        _strokes.SetAnchorsPreset(LayoutPreset.FullRect);
        host.AddChild(_strokes);
        // The strokes join square centres, which are only final once the grid has placed its squares.
        grid.SortChildren += _strokes.QueueRedraw;
        return host;
    }

    private void SlideIn()
    {
        float target = _panel.Position.Y;
        _panel.Position = _panel.Position with { Y = GetViewportRect().Size.Y + 12f };
        _panel.Modulate = new Color(1f, 1f, 1f, 0f);

        Tween tween = CreateTween().SetParallel();
        tween.TweenProperty(_panel, "position:y", target, SlideInSeconds)
            .SetTrans(Tween.TransitionType.Quint).SetEase(Tween.EaseType.Out);
        tween.TweenProperty(_panel, "modulate:a", 1f, SlideInSeconds * 0.6);
    }

    private void SlideOut()
    {
        if (_pinned)
            _peekBlockedUntilMs = Time.GetTicksMsec() + (ulong)(PeekCooldownAfterClose * 1000);

        // Input goes back to the game at once; only the picture lingers while it slides away.
        MouseFilter = MouseFilterEnum.Ignore;
        _dim.MouseFilter = MouseFilterEnum.Ignore;
        _panel.MouseFilter = MouseFilterEnum.Ignore;

        if (!IsInsideTree())
        {
            QueueFree();
            return;
        }

        Tween tween = CreateTween().SetParallel();
        tween.TweenProperty(_panel, "position:y", GetViewportRect().Size.Y + 12f, SlideOutSeconds)
            .SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.In);
        tween.TweenProperty(_panel, "modulate:a", 0f, SlideOutSeconds);
        if (_dim.Visible)
            tween.TweenProperty(_dim, "modulate:a", 0f, SlideOutSeconds);
        tween.Chain().TweenCallback(Callable.From(QueueFree));
    }

    /// <summary>Slides a peek away once the pointer has left both the button and the board for a moment.</summary>
    private void TrackPeekHover()
    {
        if (_pinned || !ReferenceEquals(_open, this))
            return;
        if (_anchor == null || !IsInstanceValid(_anchor) || !_anchor.IsVisibleInTree())
        {
            Close();
            return;
        }

        bool held = (_heldByFocus && _anchor.HasFocus())
            || IsPointerOver(_anchor, grow: 6f)
            || IsPointerOver(_panel, grow: 0f);
        _awayFor = held ? 0 : _awayFor + GetProcessDeltaTime();
        if (_awayFor > PeekLeaveGrace)
            Close();
    }

    private void UpdateCells(bool animate)
    {
        if (_strokes == null)
            return;

        bool[] done = _board.Squares.Select(BingoState.IsDone).ToArray();
        List<int[]> lines = BoardLines(_board.Size);
        List<int[]> fullLines = lines.Where(line => line.All(cell => done[cell])).ToList();
        HashSet<int> onFullLine = fullLines.SelectMany(line => line).ToHashSet();
        // The one square a line still lacks: finishing it completes that line.
        HashSet<int> finishesLine = lines
            .Select(line => line.Where(cell => !done[cell]).ToArray())
            .Where(missing => missing.Length == 1)
            .Select(missing => missing[0])
            .ToHashSet();

        StreakState[] streaks = _board.Squares.Select(BingoStreaks.StateOf).ToArray();
        for (int index = 0; index < _cells.Count; index++)
        {
            // Only squares done while the board is open pop in; the rest are simply there.
            bool newlyDone = animate && done[index] && _shownDone != null && !_shownDone.Contains(index);
            _cells[index].SetState(
                done[index], onFullLine.Contains(index), finishesLine.Contains(index), streaks[index],
                BingoProgress.For(_board.Squares[index].Key), newlyDone);
        }
        _fightLegend.Visible = streaks.Contains(StreakState.BrokenThisFight);
        _actLegend.Visible = streaks.Contains(StreakState.BrokenThisAct);
        _runLegend.Visible = streaks.Contains(StreakState.BrokenThisRun);
        _shownDone = Enumerable.Range(0, done.Length).Where(index => done[index]).ToHashSet();

        _strokes.SetLines(fullLines);
        int lineCount = fullLines.Count;
        _lineGems.SetDone(lineCount);
        _squareCount.Text = lineCount >= _board.Lines
            ? $"Goal complete  ·  {done.Count(d => d)}/{done.Length} squares"
            : $"{done.Count(d => d)}/{done.Length} squares";
    }

    /// <summary>Every row, column and both diagonals, as square indexes in order.</summary>
    private static List<int[]> BoardLines(int size)
    {
        var lines = new List<int[]>();
        for (int i = 0; i < size; i++)
        {
            int index = i;
            lines.Add(Enumerable.Range(0, size).Select(column => (index * size) + column).ToArray());
            lines.Add(Enumerable.Range(0, size).Select(row => (row * size) + index).ToArray());
        }
        lines.Add(Enumerable.Range(0, size).Select(i => (i * size) + i).ToArray());
        lines.Add(Enumerable.Range(0, size).Select(i => (i * size) + (size - 1 - i)).ToArray());
        return lines;
    }

    /// <summary>
    /// The icon of the character a character square belongs to (keys like <c>C-IR-E1</c>, text
    /// like "Ironclad: ..."), or null for shared squares and characters this game build lacks.
    /// </summary>
    private static (Texture2D Icon, string Text)? CharacterIconFor(BingoSquareInfo square)
    {
        if (!square.Key.StartsWith("C-", StringComparison.Ordinal))
            return null;
        int colon = square.Text.IndexOf(": ", StringComparison.Ordinal);
        if (colon <= 0)
            return null;

        string name = square.Text[..colon].Trim();
        CharacterModel? character = ModelDb.AllCharacters.FirstOrDefault(model =>
            string.Equals(model.Id.Entry, name, StringComparison.OrdinalIgnoreCase));
        if (character == null)
            return null;

        try
        {
            return character.IconTexture is { } icon ? (icon, square.Text[(colon + 2)..]) : null;
        }
        catch (Exception ex)
        {
            LogUtility.Warn($"[Bingo] No icon for {name}: {ex.Message}");
            return null;
        }
    }
}

/// <summary>One square of the board: tier pips, an optional character icon, the text, and a daub once done.</summary>
internal sealed partial class BingoCellView : PanelContainer
{
    // Tier colors run cool to hot; the pip count says the same for colorblind players.
    private static readonly Color EasyTier = new("#7CC4FA");
    private static readonly Color MediumTier = new("#F5A524");
    private static readonly Color HardTier = new("#E5484D");
    private static readonly Color DoneFill = Card.Lerp(Claim, 0.16f);
    // Broken streaks: pink comes back next fight, violet next act, red only next run. Kept apart
    // from the tier pips' amber and from the done teal so neither reads as the other.
    internal static readonly Color BrokenThisFightColor = new("#F472B6");
    internal static readonly Color BrokenThisActColor = new("#A78BFA");
    internal static readonly Color BrokenThisRunColor = new("#F0525A");

    // Smallest text a square shrinks to, so long squares on a 6x6 board stay readable.
    private const int MinFontSize = 11;
    private const float TopRowHeight = 24f;
    // A square on a full line is finished business: its contents fade back behind the line's stroke.
    private const float FullLineContentAlpha = 0.38f;
    // A broken streak stays readable, since the player will want it again later.
    private const float BrokenContentAlpha = 0.6f;

    private readonly Control _content;
    private readonly Label _text;
    private readonly BingoDaub _daub;
    private readonly BrokenStreakMark _broken;
    private readonly Label _progressText;
    private readonly BingoProgressBar _progressBar;
    private readonly int _maxFontSize;
    private (bool Done, bool OnFullLine, bool FinishesLine, StreakState Streak, BingoProgress.Shown? Progress)? _state;

    public BingoCellView(BingoSquareInfo square, Vector2 size, int maxFontSize, (Texture2D Icon, string Text)? character)
    {
        _maxFontSize = maxFontSize;
        CustomMinimumSize = size;
        MouseFilter = MouseFilterEnum.Ignore;

        // The daub sits behind the text so a done square stays readable.
        _daub = new BingoDaub { MouseFilter = MouseFilterEnum.Ignore, Visible = false };
        AddChild(_daub);
        _broken = new BrokenStreakMark { MouseFilter = MouseFilterEnum.Ignore, Visible = false };
        AddChild(_broken);

        var stack = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        stack.AddThemeConstantOverride("separation", 0);
        AddChild(stack);
        _content = stack;

        var topRow = new HBoxContainer
        {
            CustomMinimumSize = new Vector2(0f, TopRowHeight),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        if (character is { Icon: var icon })
        {
            topRow.AddChild(new TextureRect
            {
                Texture = icon,
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                CustomMinimumSize = new Vector2(TopRowHeight + 6f, TopRowHeight + 6f),
                SizeFlagsVertical = SizeFlags.ShrinkBegin,
                MouseFilter = MouseFilterEnum.Ignore,
            });
        }
        topRow.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Ignore });
        // Best so far on a "reach a number" square, beside the tier pips: small, since the bar
        // under the text already shows how far along it is.
        _progressText = Label("", 13, TextMuted, bold: true);
        _progressText.VerticalAlignment = VerticalAlignment.Center;
        _progressText.Visible = false;
        topRow.AddChild(_progressText);
        topRow.AddChild(new TierPips(square.Tier switch
        {
            "M" => (2, MediumTier),
            "H" => (3, HardTier),
            _ => (1, EasyTier),
        })
        {
            CustomMinimumSize = new Vector2(40f, TopRowHeight),
            MouseFilter = MouseFilterEnum.Ignore,
        });
        stack.AddChild(topRow);

        _text = new Label
        {
            Text = character?.Text ?? square.Text,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            ClipText = true,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        _text.AddThemeFontOverride("font", Regular);
        _text.AddThemeFontSizeOverride("font_size", maxFontSize);
        _text.AddThemeColorOverride("font_color", Text);
        // The cell's size is only known after layout, so fit the text whenever the label is resized.
        _text.Resized += () => FitText(_text, _maxFontSize);
        stack.AddChild(_text);
        // Keeps the text block centred in the cell: the top row is balanced by the same space
        // below, which also holds the progress bar.
        _progressBar = new BingoProgressBar
        {
            CustomMinimumSize = new Vector2(0f, TopRowHeight * 0.5f),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        stack.AddChild(_progressBar);
    }

    internal void SetState(
        bool done, bool onFullLine, bool finishesLine, StreakState streak, BingoProgress.Shown? progress, bool popIn)
    {
        if (_state == (done, onFullLine, finishesLine, streak, progress) && !popIn)
            return;
        _state = (done, onFullLine, finishesLine, streak, progress);

        // Progress only matters while the square is still to do. The value for the turn, fight,
        // act or run in progress is shown plainly; with none, the best so far is shown dimmed.
        bool showProgress = !done && progress is { Target: > 0 } shown && (shown.Live || shown.Value > 0);
        _progressText.Visible = showProgress;
        bool live = showProgress && progress!.Value.Live;
        _progressBar.SetFraction(showProgress ? (float)progress!.Value.Value / progress.Value.Target : 0f, dimmed: !live);
        if (showProgress)
        {
            _progressText.Text = $"{progress!.Value.Value}/{progress.Value.Target}";
            _progressText.AddThemeColorOverride("font_color", live ? Text : new Color(TextMuted, 0.55f));
        }

        Color? brokenColor = streak switch
        {
            StreakState.BrokenThisFight => BrokenThisFightColor,
            StreakState.BrokenThisAct => BrokenThisActColor,
            StreakState.BrokenThisRun => BrokenThisRunColor,
            _ => null,
        };

        StyleBoxFlat style;
        if (brokenColor is { } broken)
        {
            style = RunGraphStyle.Box(Card.Darkened(0.15f), broken.Darkened(0.1f), 12, 10f, borderWidth: 2);
        }
        else if (onFullLine)
        {
            style = RunGraphStyle.Box(DoneFill.Darkened(0.35f), Gold.Darkened(0.25f), 12, 10f, borderWidth: 2);
        }
        else if (done)
        {
            style = RunGraphStyle.Box(DoneFill, Claim.Darkened(0.2f), 12, 10f, borderWidth: 2);
        }
        else if (finishesLine)
        {
            // One square from a line: a warm glow so it reads as "chase this one" without words.
            style = RunGraphStyle.Box(Card, Gold.Darkened(0.1f), 12, 10f, borderWidth: 2);
            style.ShadowColor = new Color(Gold, 0.28f);
            style.ShadowSize = 10;
        }
        else
        {
            style = RunGraphStyle.Box(Card, PanelBorder, 12, 10f, borderWidth: 1);
        }
        AddThemeStyleboxOverride("panel", style);

        _broken.Visible = brokenColor != null;
        if (brokenColor is { } markColor)
            _broken.SetColor(markColor);

        float contentAlpha = onFullLine ? FullLineContentAlpha : brokenColor != null ? BrokenContentAlpha : 1f;
        _content.Modulate = new Color(1f, 1f, 1f, contentAlpha);
        // SelfModulate, because PopIn animates the daub's Modulate.
        _daub.SelfModulate = new Color(1f, 1f, 1f, contentAlpha);
        _daub.Visible = done;
        if (done && popIn)
            _daub.PopIn();
    }

    /// <summary>
    /// Sets the largest font size at which the square's whole text fits its label. The game's
    /// MegaLabel estimated too small a height for these cells and let the last line be clipped,
    /// so this measures wrapped lines the way Label draws them: one font height per line plus
    /// the theme's line spacing between lines.
    /// </summary>
    private static void FitText(Label label, int maxFontSize)
    {
        Vector2 area = label.Size;
        if (area.X < 1f || area.Y < 1f)
            return;

        Font font = label.GetThemeFont("font");
        int lineSpacing = label.GetThemeConstant("line_spacing");
        int fontSize = maxFontSize;
        while (fontSize > MinFontSize
            && WrappedTextHeight(font, label.Text, area.X, fontSize, lineSpacing) > area.Y)
        {
            fontSize--;
        }

        if (label.GetThemeFontSize("font_size") != fontSize)
            label.AddThemeFontSizeOverride("font_size", fontSize);
    }

    private static float WrappedTextHeight(Font font, string text, float width, int fontSize, int lineSpacing)
    {
        var paragraph = new TextParagraph { Width = width };
        paragraph.BreakFlags = TextServer.LineBreakFlag.Mandatory
            | TextServer.LineBreakFlag.WordBound
            | TextServer.LineBreakFlag.Adaptive;
        paragraph.AddString(text, font, fontSize);
        int lines = paragraph.GetLineCount();
        return (lines * font.GetHeight(fontSize)) + (Math.Max(0, lines - 1) * lineSpacing);
    }
}

/// <summary>A done square's mark: a translucent dauber blot behind the text and a small check badge.</summary>
internal sealed partial class BingoDaub : Control
{
    public BingoDaub() => Draw += DrawDaub;

    /// <summary>Stamps the daub in: it lands large and settles, like a dauber pressed onto the card.</summary>
    internal void PopIn()
    {
        PivotOffset = Size / 2f;
        Scale = new Vector2(1.6f, 1.6f);
        Modulate = new Color(1f, 1f, 1f, 0f);
        Tween tween = CreateTween().SetParallel();
        tween.TweenProperty(this, "scale", Vector2.One, 0.38)
            .SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
        tween.TweenProperty(this, "modulate:a", 1f, 0.18);
    }

    private void DrawDaub()
    {
        Vector2 center = Size / 2f;
        float radius = Math.Min(Size.X, Size.Y) * 0.42f;
        DrawCircle(center, radius, new Color(Claim, 0.2f));
        DrawArc(center, radius, 0f, Mathf.Tau, 48, new Color(Claim, 0.55f), 3f, antialiased: true);

        // The check badge in the bottom-right corner, where the text rarely reaches.
        var badge = new Vector2(Size.X - 12f, Size.Y - 12f);
        DrawCircle(badge, 11f, Ink);
        DrawCircle(badge, 9f, Claim);
        DrawPolyline(
            [badge + new Vector2(-4.5f, 0f), badge + new Vector2(-1.2f, 3.5f), badge + new Vector2(4.8f, -3.8f)],
            Ink, 2.6f, antialiased: true);
    }
}

/// <summary>
/// A broken streak's mark, behind the square's text: faint diagonal stripes across the square
/// and a barred badge in the corner where a done square has its check.
/// </summary>
internal sealed partial class BrokenStreakMark : Control
{
    private const float StripeSpacing = 16f;
    private Color _color;

    public BrokenStreakMark()
    {
        // The stripes run past the edges; clipping keeps them inside the square.
        ClipContents = true;
        Draw += DrawMark;
    }

    internal void SetColor(Color color)
    {
        if (_color == color)
            return;
        _color = color;
        QueueRedraw();
    }

    private void DrawMark()
    {
        var stripe = new Color(_color, 0.13f);
        for (float x = -Size.Y; x < Size.X; x += StripeSpacing)
            DrawLine(new Vector2(x, Size.Y), new Vector2(x + Size.Y, 0f), stripe, 5f, antialiased: true);

        BrokenStreakBadge.DrawBadge(this, new Vector2(Size.X - 12f, Size.Y - 12f), _color);
    }
}

/// <summary>A small barred circle in a broken streak's colour, also used by the header's legend.</summary>
internal sealed partial class BrokenStreakBadge : Control
{
    private readonly Color _color;

    public BrokenStreakBadge(Color color)
    {
        _color = color;
        Draw += () => DrawBadge(this, Size / 2f, _color);
    }

    internal static void DrawBadge(CanvasItem canvas, Vector2 center, Color color)
    {
        canvas.DrawCircle(center, 11f, Ink);
        canvas.DrawCircle(center, 9f, color);
        canvas.DrawLine(center + new Vector2(-4.5f, 4.5f), center + new Vector2(4.5f, -4.5f), Ink, 2.8f, antialiased: true);
    }
}

/// <summary>
/// A thin bar under a square's text: how close the current value (or, dimmed, the best so far)
/// is to the target.
/// </summary>
internal sealed partial class BingoProgressBar : Control
{
    private const float Thickness = 4f;
    private float _fraction;
    private bool _dimmed;

    public BingoProgressBar() => Draw += DrawBar;

    internal void SetFraction(float fraction, bool dimmed)
    {
        fraction = Math.Clamp(fraction, 0f, 1f);
        if (Mathf.IsEqualApprox(_fraction, fraction) && _dimmed == dimmed)
            return;
        _fraction = fraction;
        _dimmed = dimmed;
        QueueRedraw();
    }

    private void DrawBar()
    {
        if (_fraction <= 0f)
            return;
        float width = Size.X * 0.7f;
        var track = new Rect2((Size.X - width) / 2f, (Size.Y - Thickness) / 2f, width, Thickness);
        DrawRect(track, Track);
        DrawRect(track with { Size = new Vector2(width * _fraction, Thickness) }, new Color(Claim, _dimmed ? 0.35f : 0.85f));
    }
}

/// <summary>One to three pips in the tier's color, in a square's top-right corner.</summary>
internal sealed partial class TierPips : Control
{
    private readonly int _count;
    private readonly Color _color;

    public TierPips((int Count, Color Color) tier)
    {
        (_count, _color) = tier;
        Draw += DrawPips;
    }

    private void DrawPips()
    {
        const float radius = 4.5f;
        const float gap = 4f;
        float y = Size.Y / 2f;
        for (int i = 0; i < _count; i++)
        {
            float x = Size.X - radius - 2f - (i * ((2 * radius) + gap));
            DrawCircle(new Vector2(x, y), radius + 1.5f, Ink);
            DrawCircle(new Vector2(x, y), radius, _color);
        }
    }
}

/// <summary>The header's line counter: one gem per line the goal needs, lit as lines fill.</summary>
internal sealed partial class BingoLineGems : Control
{
    private const float Radius = 11f;
    private const float Gap = 8f;
    private readonly int _needed;
    private int _done;

    public BingoLineGems(int needed)
    {
        _needed = needed;
        CustomMinimumSize = new Vector2((needed * ((2 * Radius) + Gap)) + 4f, (2 * Radius) + 6f);
        TooltipText = $"Lines toward the goal: {needed} needed";
        Draw += DrawGems;
    }

    internal void SetDone(int done)
    {
        if (_done == done)
            return;
        _done = done;
        QueueRedraw();
    }

    private void DrawGems()
    {
        float y = Size.Y / 2f;
        for (int i = 0; i < _needed; i++)
        {
            var center = new Vector2(Radius + 2f + (i * ((2 * Radius) + Gap)), y);
            if (i < _done)
            {
                DrawGem(this, center, Radius, Gold);
            }
            else
            {
                DrawCircle(center, Radius, Track);
                DrawArc(center, Radius, 0f, Mathf.Tau, 32, TextFaint, 2f, antialiased: true);
            }
        }
    }
}

/// <summary>Gold strokes through the board's full lines, drawn over the squares like a marker.</summary>
internal sealed partial class BingoLineStrokes : Control
{
    private readonly List<BingoCellView> _cells;
    private List<int[]> _lines = new();

    public BingoLineStrokes(List<BingoCellView> cells)
    {
        _cells = cells;
        Draw += DrawStrokes;
    }

    internal void SetLines(List<int[]> lines)
    {
        _lines = lines;
        QueueRedraw();
    }

    private void DrawStrokes()
    {
        foreach (int[] line in _lines)
        {
            Control first = _cells[line[0]];
            Control last = _cells[line[^1]];
            Vector2 from = CenterOf(first);
            Vector2 to = CenterOf(last);
            // Run a little past the end squares' centres so the stroke reads as crossing the whole line.
            Vector2 overshoot = (to - from).Normalized() * Math.Min(first.Size.X, first.Size.Y) * 0.3f;
            from -= overshoot;
            to += overshoot;

            DrawLine(from, to, new Color(Ink, 0.55f), 11f, antialiased: true);
            DrawLine(from, to, new Color(Gold, 0.95f), 6f, antialiased: true);
            DrawCircle(from, 3f, new Color(Gold, 0.95f));
            DrawCircle(to, 3f, new Color(Gold, 0.95f));
        }
    }

    private Vector2 CenterOf(Control cell) =>
        GetGlobalTransform().AffineInverse() * (cell.GetGlobalRect().Position + (cell.Size / 2f));
}
