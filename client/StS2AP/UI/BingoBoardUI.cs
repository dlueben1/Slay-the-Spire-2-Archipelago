using Godot;
using StS2AP.Bingo;
using StS2AP.UI.RunGraph;
using static StS2AP.UI.RunGraph.RunGraphStyle;

namespace StS2AP.UI;

/// <summary>
/// The bingo board overlay: the slot's squares in a size-by-size grid, done squares marked and
/// squares on a full line edged in gold, with how many lines the goal needs. Opened from the goal
/// tracker on Character Select (its Board button or the AP menu hotkey) or from the Bingo board
/// row of the in-run AP menu, and closed with Back, a click outside the board, the Close button
/// or the hotkey again. It only reads the board, so it is safe over a run.
/// </summary>
internal sealed partial class BingoBoardUI : Control
{
    private const float WindowWidth = 1240f;
    private const float WindowHeight = 960f;
    private const float WindowPadding = 32f;
    private const float HeaderHeight = 76f;
    private const float FooterHeight = 28f;
    private const int SectionSeparation = 18;
    private const int CellSeparation = 10;
    // Smallest text a square shrinks to, so long squares on a 6x6 board stay readable.
    private const int MinSquareFontSize = 11;

    private static readonly Color DoneFill = Claim.Darkened(0.55f);

    private static BingoBoardUI? _open;

    private BingoBoard _board = null!;
    private Label _subtitle = null!;
    private Button _close = null!;
    private readonly List<(BingoSquareInfo Square, PanelContainer Cell, Label Mark, Label Text)> _cells = new();

    private const string RewardMenuButtonName = "ApBingoBoardButton";

    // The overlay layer joins the tree a frame later, so "open" means created and not yet freed.
    internal static bool IsOpen => _open != null && IsInstanceValid(_open) && !_open.IsQueuedForDeletion();

    /// <summary>
    /// Shows the board over everything. It also closes when <paramref name="owner"/>, the screen it
    /// was opened from, leaves the tree. <paramref name="returnFocus"/> gets the focus back when the
    /// board closes, so controller and keyboard players land where they opened it.
    /// </summary>
    internal static void Open(Node owner, Control? returnFocus = null)
    {
        if (BingoState.Board is not { } board || IsOpen)
            return;

        var overlay = new BingoBoardUI { Name = "ApBingoBoard", _board = board };
        overlay.Build();
        _open = overlay;
        if (returnFocus != null)
        {
            overlay.TreeExiting += () => Callable.From(() =>
            {
                if (IsInstanceValid(returnFocus) && returnFocus.IsVisibleInTree())
                    returnFocus.GrabFocus();
            }).CallDeferred();
        }
        ApOverlayLayer.Show(overlay, owner, Close);
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

    internal static void Toggle(Node owner)
    {
        if (IsOpen)
            Close();
        else
            Open(owner);
    }

    internal static void Close()
    {
        if (_open != null && IsInstanceValid(_open))
            _open.QueueFree();
        _open = null;
    }

    /// <summary>Called on slot changes: a board from another slot must not stay open.</summary>
    internal static void RemoveUI() => Close();

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
        _open.UpdateCells();
    }

    private void Build()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop;

        var dim = new ColorRect { Color = Backdrop, MouseFilter = MouseFilterEnum.Stop };
        dim.SetAnchorsPreset(LayoutPreset.FullRect);
        dim.GuiInput += input =>
        {
            if (input is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left })
                Close();
        };
        AddChild(dim);

        var panel = new PanelContainer { MouseFilter = MouseFilterEnum.Stop };
        panel.SetAnchorsPreset(LayoutPreset.Center);
        panel.OffsetLeft = -WindowWidth / 2f; panel.OffsetRight = WindowWidth / 2f;
        panel.OffsetTop = -WindowHeight / 2f; panel.OffsetBottom = WindowHeight / 2f;
        var panelStyle = Box(Page, PanelBorder, 16, WindowPadding, borderWidth: 2);
        panelStyle.ShadowColor = new Color(0f, 0f, 0f, 0.6f);
        panelStyle.ShadowSize = 28;
        panel.AddThemeStyleboxOverride("panel", panelStyle);
        AddChild(panel);

        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", SectionSeparation);
        panel.AddChild(column);
        column.AddChild(BuildHeader());
        column.AddChild(BuildGrid());

        var footer = Label("A square is done once its check is sent. Back closes the board.", 15, TextMuted);
        footer.CustomMinimumSize = new Vector2(0f, FooterHeight);
        column.AddChild(footer);

        UpdateCells();
        // Controllers and keyboard-only mode need something focused; Close is the only button.
        Ready += () => _close.GrabFocus();
    }

    private Control BuildHeader()
    {
        var header = new HBoxContainer { CustomMinimumSize = new Vector2(0f, HeaderHeight) };
        header.AddThemeConstantOverride("separation", 12);
        var titles = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        titles.AddChild(Label("Bingo", 36, Gold, bold: true));
        _subtitle = Label("", 18, TextMuted);
        titles.AddChild(_subtitle);
        header.AddChild(titles);

        _close = ActionButton("Close");
        _close.Pressed += Close;
        header.AddChild(_close);
        return header;
    }

    private Control BuildGrid()
    {
        int size = _board.Size;
        float gridWidth = WindowWidth - (2 * WindowPadding);
        float gridHeight = WindowHeight - (2 * WindowPadding) - HeaderHeight - FooterHeight - (2 * SectionSeparation);
        var cellSize = new Vector2(
            (gridWidth - (CellSeparation * (size - 1))) / size,
            (gridHeight - (CellSeparation * (size - 1))) / size);
        // Larger boards get smaller text; FitSquareText shrinks it further when a square's text is long.
        int maxFontSize = size switch { <= 3 => 30, 4 => 26, 5 => 22, _ => 20 };

        var grid = new GridContainer { Columns = size, MouseFilter = MouseFilterEnum.Ignore };
        grid.AddThemeConstantOverride("h_separation", CellSeparation);
        grid.AddThemeConstantOverride("v_separation", CellSeparation);

        foreach (BingoSquareInfo square in _board.Squares)
        {
            var cell = new PanelContainer { CustomMinimumSize = cellSize, MouseFilter = MouseFilterEnum.Ignore };
            var stack = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
            stack.AddThemeConstantOverride("separation", 2);
            cell.AddChild(stack);

            // Reserves its line even while empty, so the text area is the same in every cell.
            Label mark = Caps("", 12, Claim);
            mark.HorizontalAlignment = HorizontalAlignment.Center;
            mark.CustomMinimumSize = new Vector2(0f, 16f);
            stack.AddChild(mark);

            var text = new Label
            {
                Text = square.Text,
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                ClipText = true,
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                SizeFlagsVertical = SizeFlags.ExpandFill,
                MouseFilter = MouseFilterEnum.Ignore,
            };
            text.AddThemeFontOverride("font", Regular);
            text.AddThemeFontSizeOverride("font_size", maxFontSize);
            // The cell's size is only known after layout, so fit the text whenever the label is resized.
            text.Resized += () => FitSquareText(text, maxFontSize);
            stack.AddChild(text);

            grid.AddChild(cell);
            _cells.Add((square, cell, mark, text));
        }
        return grid;
    }

    /// <summary>
    /// Sets the largest font size at which a square's whole text fits its label. The game's
    /// MegaLabel estimated too small a height for these cells and let the last line be clipped,
    /// so this measures wrapped lines the way Label draws them: one font height per line plus
    /// the theme's line spacing between lines.
    /// </summary>
    private static void FitSquareText(Label label, int maxFontSize)
    {
        Vector2 area = label.Size;
        if (area.X < 1f || area.Y < 1f)
            return;

        Font font = label.GetThemeFont("font");
        int lineSpacing = label.GetThemeConstant("line_spacing");
        int fontSize = maxFontSize;
        while (fontSize > MinSquareFontSize
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

    private void UpdateCells()
    {
        HashSet<int> onFullLine = SquaresOnFullLines();
        for (int index = 0; index < _cells.Count; index++)
        {
            var (square, cell, mark, text) = _cells[index];
            bool done = BingoState.IsDone(square);
            Color border = onFullLine.Contains(index) ? Gold : done ? Claim : PanelBorder;
            cell.AddThemeStyleboxOverride("panel",
                Box(done ? DoneFill : Card, border, 10, 10f, borderWidth: done ? 3 : 1));
            mark.Text = done ? "Done" : "";
            text.AddThemeColorOverride("font_color", Text);
        }

        int lines = _board.CountCompletedLines(BingoState.IsDone);
        int squaresDone = _board.Squares.Count(BingoState.IsDone);
        string progress = lines >= _board.Lines
            ? $"Goal complete with {lines} lines"
            : $"{lines} of {_board.Lines} lines for the goal";
        _subtitle.Text = $"{progress}, {squaresDone} of {_board.Squares.Count} squares done";
    }

    /// <summary>Indexes of the squares that lie on at least one full row, column or diagonal.</summary>
    private HashSet<int> SquaresOnFullLines()
    {
        int size = _board.Size;
        bool[] done = _board.Squares.Select(BingoState.IsDone).ToArray();
        var lines = new List<int[]>();
        for (int i = 0; i < size; i++)
        {
            int index = i;
            lines.Add(Enumerable.Range(0, size).Select(column => (index * size) + column).ToArray());
            lines.Add(Enumerable.Range(0, size).Select(row => (row * size) + index).ToArray());
        }
        lines.Add(Enumerable.Range(0, size).Select(i => (i * size) + i).ToArray());
        lines.Add(Enumerable.Range(0, size).Select(i => (i * size) + (size - 1 - i)).ToArray());

        return lines.Where(line => line.All(cell => done[cell])).SelectMany(line => line).ToHashSet();
    }
}
