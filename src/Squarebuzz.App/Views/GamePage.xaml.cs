using Squarebuzz.App.ViewModels;

namespace Squarebuzz.App.Views;

public partial class GamePage : ContentPage
{
    private readonly GameViewModel _viewModel;

    /// <summary>Cell the magnifier is centred on, or -1 when it is hidden.</summary>
    private int _magnifiedIndex = -1;

    /// <summary>Null until the first size is known, so the first layout always applies.</summary>
    private bool? _isWideLayout;

    public GamePage(GameViewModel viewModel)
    {
        InitializeComponent();

        _viewModel = viewModel;
        BindingContext = viewModel;

        // Beyond the usual leak (see PageLifecycle), disposing this ViewModel is what stops the
        // one-second clock when the player leaves with the back gesture instead of Quit.
        this.DisposeViewModelWhenPopped();

        // Board rendering is imperative by nature - a canvas redraw is not a binding - so the
        // ViewModel signals through events and the page translates them into draw calls.
        _viewModel.BoardChanged += OnBoardChanged;
        _viewModel.MistakeMade += OnMistakeMade;
        _viewModel.HintGranted += OnHintGranted;
        _viewModel.PuzzleSolved += OnPuzzleSolved;
        _viewModel.CellFilled += (_, index) => Board.PopCell(index);

        // Full Refresh, not RefreshCells: only the former re-reads the palette, which is the
        // point - the canvas snapshots its colours and a theme swap otherwise leaves the board
        // in the old ones.
        _viewModel.PaletteChanged += (_, _) => Board.Refresh();

        Board.CellPainted += OnCellPainted;
        Board.CrossGestureRecognised += OnCrossGestureRecognised;
        Board.TouchedCellChanged += OnTouchedCellChanged;

        // The board is sized from the space its host offers, not from its own width - see
        // BoardView.AvailableSize for why that distinction matters.
        BoardHost.SizeChanged += OnBoardHostSizeChanged;

        // The page's own size decides the arrangement, and it changes on rotation as well as at
        // first layout.
        SizeChanged += (_, _) => ApplyLayout();

        // The overlay has to follow the board's geometry, which changes with zoom and rotation -
        // and the screen-reader state, which can flip mid-game.
        Board.LayoutChanged += (_, _) => BuildCellOverlay();
        _viewModel.OverlayNeedChanged += (_, _) => BuildCellOverlay();
    }

    private void OnBoardHostSizeChanged(object? sender, EventArgs e)
    {
        if (BoardHost.Width > 0 && BoardHost.Height > 0)
        {
            Board.AvailableSize = new Size(BoardHost.Width, BoardHost.Height);
        }
    }

    /// <summary>
    /// Switches between the phone layout and the wide-landscape one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The design doc's scaling note is the rule: "Above 900 px in landscape the play column
    /// becomes a row ... Cell size is computed from the free rectangle, not hard-coded." Moving
    /// the controls beside the board rather than beneath it is what gives the board the full
    /// height, and on a tablet in landscape that is the difference between a cramped grid and a
    /// comfortable one.
    /// </para>
    /// <para>
    /// Done in code because MAUI has no media queries, and by rearranging one visual tree rather
    /// than toggling between two: the board is a stateful control holding the live session, so
    /// there must only ever be one of it.
    /// </para>
    /// <para>
    /// Deviation worth knowing about: the doc puts a mini-map and clue helpers in the right-hand
    /// column and keeps the toolbar at the bottom edge. Neither of those exists yet, and on a
    /// tablet the whole board is visible at once so a mini-map would show nothing new. The
    /// controls go there instead, on the side the player's hand is on.
    /// </para>
    /// </remarks>
    private void ApplyLayout()
    {
        // Below this the two-column split leaves the board narrower than it is tall, which is
        // worse than stacking. The threshold is the doc's 900, in device-independent units.
        const double WideThreshold = 900;
        const double ControlsWidth = 320;

        var wide = Width >= WideThreshold && Width > Height;

        if (_isWideLayout == wide)
        {
            return;
        }

        _isWideLayout = wide;
        _viewModel.IsWideLayout = wide;

        if (!wide)
        {
            PlayLayout.ColumnDefinitions = [new ColumnDefinition(GridLength.Star)];
            PlayLayout.RowDefinitions =
            [
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Star),
                new RowDefinition(GridLength.Auto),
            ];

            Place(StatusBar, row: 0, column: 0, columnSpan: 1);
            Place(BoardHost, row: 1, column: 0, columnSpan: 1);
            Place(MagnifierPanel, row: 1, column: 0, columnSpan: 1);
            Place(ControlsPanel, row: 2, column: 0, columnSpan: 1);

            ControlsPanel.WidthRequest = -1;
            ControlsPanel.VerticalOptions = LayoutOptions.End;

            ActionGrid.ColumnDefinitions =
                [new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star),
                 new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star)];
            ActionGrid.RowDefinitions = [new RowDefinition(GridLength.Auto)];

            return;
        }

        // Controls take the side the hand is on, which is the same setting that orders them.
        var controlsFirst = !_viewModel.IsWideControlsOnRight;

        PlayLayout.ColumnDefinitions = controlsFirst
            ? [new ColumnDefinition(ControlsWidth), new ColumnDefinition(GridLength.Star)]
            : [new ColumnDefinition(GridLength.Star), new ColumnDefinition(ControlsWidth)];

        PlayLayout.RowDefinitions =
            [new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Star)];

        var controlsColumn = controlsFirst ? 0 : 1;
        var boardColumn = controlsFirst ? 1 : 0;

        Place(StatusBar, row: 0, column: 0, columnSpan: 2);
        Place(BoardHost, row: 1, column: boardColumn, columnSpan: 1);
        Place(MagnifierPanel, row: 1, column: boardColumn, columnSpan: 1);
        Place(ControlsPanel, row: 1, column: controlsColumn, columnSpan: 1);

        ControlsPanel.WidthRequest = ControlsWidth;
        ControlsPanel.VerticalOptions = LayoutOptions.Center;

        ActionGrid.ColumnDefinitions =
            [new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star)];
        ActionGrid.RowDefinitions =
            [new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Auto)];
    }

    private static void Place(View view, int row, int column, int columnSpan)
    {
        Grid.SetRow(view, row);
        Grid.SetColumn(view, column);
        Grid.SetColumnSpan(view, columnSpan);
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        if (_viewModel.Session is null)
        {
            // Resumes the save named by the route, or starts a fresh puzzle.
            await _viewModel.InitialiseAsync();
        }
        else
        {
            // Coming back from backgrounding or from a page pushed over the game - How to play,
            // or Options, whose changes must show on the board right away.
            await _viewModel.RefreshSettingsAsync();
            _viewModel.ResumeClock();
        }
    }

    protected override async void OnDisappearing()
    {
        base.OnDisappearing();

        // Covers backgrounding and back-navigation, which are the routes out of the game that
        // no button handles. The clock stops first: time on the home screen or on a page pushed
        // over the game is not time spent playing, and it must not count against a timed trial
        // or the parental screen-time limit.
        _viewModel.SuspendClock();
        await _viewModel.AutosaveAsync();
    }

    private void OnCellPainted(object? sender, Controls.CellPaintedEventArgs e) =>
        _viewModel.Paint(e.Index, e.Target);

    private void OnTouchedCellChanged(object? sender, Controls.TouchedCellEventArgs e)
    {
        if (!_viewModel.ShowMagnifier)
        {
            return;
        }

        if (e.Index < 0)
        {
            _magnifiedIndex = -1;
            MagnifierPanel.IsVisible = false;
            return;
        }

        // Sit opposite the hand, so the panel is never the thing the palm is covering.
        MagnifierPanel.HorizontalOptions = e.IsInLeftHalf ? LayoutOptions.End : LayoutOptions.Start;
        MagnifierPanel.IsVisible = true;

        _magnifiedIndex = e.Index;
        Magnifier.ShowCell(e.Index);
    }

    /// <summary>
    /// Builds one focusable button per square, over the board, so a screen-reader user can reach
    /// the puzzle at all - a canvas offers nothing to focus.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Only when a screen reader is actually running. A 20x20 grid is four hundred buttons, which
    /// is worth building for the player who cannot otherwise play and pure waste for everyone else;
    /// see <see cref="Services.IAccessibilityState"/>.
    /// </para>
    /// <para>
    /// Alignment comes from the board's own <c>BoardLayout</c> rather than from a second
    /// calculation: the overlay is inset by the clue gutters and then given one row and column per
    /// cell at exactly the drawn cell size. Two independent computations of the same geometry would
    /// drift the first time either changed.
    /// </para>
    /// </remarks>
    private void BuildCellOverlay()
    {
        if (!_viewModel.NeedsCellOverlay || _viewModel.Session is not { } session)
        {
            CellOverlay.IsVisible = false;
            CellOverlay.Clear();
            return;
        }

        var layout = Board.CurrentLayout;
        var puzzle = session.Puzzle;

        if (layout.CellSize <= 0)
        {
            return;
        }

        CellOverlay.Clear();
        CellOverlay.RowDefinitions.Clear();
        CellOverlay.ColumnDefinitions.Clear();

        // Sit exactly over the grid, leaving the clue gutters to the canvas.
        CellOverlay.Margin = new Thickness(layout.RowGutterWidth, layout.ColumnGutterHeight, 0, 0);
        CellOverlay.HorizontalOptions = LayoutOptions.Start;
        CellOverlay.VerticalOptions = LayoutOptions.Start;

        for (var row = 0; row < puzzle.Height; row++)
        {
            CellOverlay.RowDefinitions.Add(new RowDefinition(layout.CellSize));
        }

        for (var column = 0; column < puzzle.Width; column++)
        {
            CellOverlay.ColumnDefinitions.Add(new ColumnDefinition(layout.CellSize));
        }

        for (var row = 0; row < puzzle.Height; row++)
        {
            for (var column = 0; column < puzzle.Width; column++)
            {
                var index = (row * puzzle.Width) + column;

                var cell = new Button
                {
                    BackgroundColor = Colors.Transparent,
                    BorderWidth = 0,
                    CornerRadius = 0,
                    Padding = 0,
                    Text = string.Empty,
                };

                SemanticProperties.SetDescription(cell, _viewModel.DescribeCell(index));

                cell.Clicked += (_, _) =>
                {
                    _viewModel.TapCell(index);

                    // The square's description is now stale, and a screen reader reads whatever the
                    // element says at the moment it is focused - so it is refreshed immediately
                    // rather than waiting for the next rebuild.
                    RefreshCellDescriptions();
                };

                CellOverlay.Add(cell, column, row);
            }
        }

        CellOverlay.IsVisible = true;
    }

    /// <summary>Re-reads every square's description after a move, without rebuilding the overlay.</summary>
    private void RefreshCellDescriptions()
    {
        if (!CellOverlay.IsVisible)
        {
            return;
        }

        for (var i = 0; i < CellOverlay.Children.Count; i++)
        {
            if (CellOverlay.Children[i] is View view)
            {
                SemanticProperties.SetDescription(view, _viewModel.DescribeCell(i));
            }
        }
    }

    private void OnBoardChanged(object? sender, EventArgs e)
    {
        Board.RefreshCells();
        RefreshCellDescriptions();

        // TouchedCellChanged fires before the move is applied, so the magnifier's first snapshot
        // is pre-paint. Re-reading it here is what makes it show the cell as it now is rather
        // than as it was a moment ago.
        if (_magnifiedIndex >= 0 && MagnifierPanel.IsVisible)
        {
            Magnifier.ShowCell(_magnifiedIndex);
        }
    }

    private async void OnMistakeMade(object? sender, int index)
    {
        Buzz(HapticFeedbackType.LongPress);

        // The design's "no" gesture: four quick beats of ±7, alongside the warn tint. Never a
        // modal, never a sound - a slip should cost a beat, not a telling-off.
        if (!Services.MotionPreferences.ReduceMotion)
        {
            _ = ShakeBoardAsync();
        }

        await Board.FlashMistakeAsync(index);
    }

    private async Task ShakeBoardAsync()
    {
        await Board.TranslateToAsync(-7, 0, 50, Easing.Linear);
        await Board.TranslateToAsync(7, 0, 100, Easing.Linear);
        await Board.TranslateToAsync(-7, 0, 100, Easing.Linear);
        await Board.TranslateToAsync(7, 0, 100, Easing.Linear);
        await Board.TranslateToAsync(0, 0, 50, Easing.Linear);
    }

    private void OnHintGranted(object? sender, int index) => Board.ShowHint(index);

    private async void OnPuzzleSolved(object? sender, EventArgs e)
    {
        if (_viewModel.Session is not { } session)
        {
            return;
        }

        // The reveal is the reward, so it is animated rather than snapped in.
        Reveal.Puzzle = session.Puzzle;

        Buzz(HapticFeedbackType.Click);
        await Reveal.RevealAsync(TimeSpan.FromMilliseconds(700));
    }

    private void OnCrossGestureRecognised(object? sender, EventArgs e) =>
        Buzz(HapticFeedbackType.Click);

    /// <summary>
    /// Buzzes, if the player asked for it.
    /// </summary>
    /// <remarks>
    /// The setting check belongs here rather than at each call site: every buzz in the game goes
    /// through this method, so there is no way to add a new one that quietly ignores the switch.
    /// Haptics are also unsupported on desktop and some platforms, and the API throws rather than
    /// no-opping - feedback is a nicety, so failing to buzz must never interrupt play.
    /// </remarks>
    private void Buzz(HapticFeedbackType type)
    {
        if (!_viewModel.HapticsEnabled)
        {
            return;
        }

        try
        {
            HapticFeedback.Default.Perform(type);
        }
        catch (FeatureNotSupportedException)
        {
        }
        catch (PermissionException)
        {
        }
    }
}
