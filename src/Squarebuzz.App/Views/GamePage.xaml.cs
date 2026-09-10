using System.ComponentModel;
using Squarebuzz.App.Controls;
using Squarebuzz.Presentation.Services;
using Squarebuzz.Presentation.ViewModels;

namespace Squarebuzz.App.Views;

public partial class GamePage : ContentPage, IQueryAttributable
{
    private readonly GameViewModel _viewModel;
    private readonly GameLifecycle _lifecycle;

    /// <summary>Cell the magnifier is centred on, or -1 when it is hidden.</summary>
    private int _magnifiedIndex = -1;

    /// <summary>The last applied arrangement, including the controls' side in wide layouts.</summary>
    private (bool IsWide, bool ControlsOnRight)? _appliedLayout;

    /// <summary>True while the mistake shake is running, so a second one cannot overlap it.</summary>
    private bool _isShaking;

    /// <summary>Guards the minimap's un-dim timer, so a stale timer cannot undo a newer hint's dim.</summary>
    private int _hintOverlayGeneration;

    public GamePage(GameViewModel viewModel, GameLifecycle lifecycle)
    {
        InitializeComponent();

        _viewModel = viewModel;
        _lifecycle = lifecycle;
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
        // first layout. Settings can arrive later or change while Options covers this page.
        SizeChanged += (_, _) => ApplyLayout();
        _viewModel.PropertyChanged += OnLayoutSettingsChanged;

        // The overlay has to follow the board's geometry, which changes with zoom and rotation -
        // and the screen-reader state, which can flip mid-game.
        Board.LayoutChanged += (_, _) => BuildCellOverlay();
        _viewModel.OverlayNeedChanged += (_, _) => BuildCellOverlay();
        _viewModel.CellDescriptionsChanged += (_, _) => RefreshCellDescriptions();

        // The mini-map tracks the board's geometry and the scroll window over it.
        Board.LayoutChanged += (_, _) => UpdateMiniMap();
        BoardHost.Scrolled += (_, _) => UpdateMiniMapViewport();
    }

    /// <summary>
    /// Hands the route's parameters to the ViewModel. The forwarding exists because the
    /// ViewModel lives in the MAUI-free Presentation assembly and cannot implement Shell's
    /// <see cref="IQueryAttributable"/> itself.
    /// </summary>
    public void ApplyQueryAttributes(IDictionary<string, object> query) =>
        _viewModel.ApplyQueryAttributes(query);

    private void OnBoardHostSizeChanged(object? sender, EventArgs e)
    {
        if (BoardHost.Width > 0 && BoardHost.Height > 0)
        {
            Board.AvailableSize = new Size(BoardHost.Width, BoardHost.Height);
        }
    }

    private void OnLayoutSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is null or "" or nameof(GameViewModel.IsWideControlsOnRight))
        {
            ApplyLayout();
        }
    }

    /// <summary>
    /// Applies the phone or wide-landscape layout, with controls on the chosen hand's side.
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
    /// The controls sit beside the board on the side the player's hand is on. The mini-map and
    /// magnifier remain in the board's column when those columns swap.
    /// </para>
    /// </remarks>
    private void ApplyLayout()
    {
        // Below this the two-column split leaves the board narrower than it is tall, which is
        // worse than stacking. The threshold is the doc's 900, in device-independent units.
        const double WideThreshold = 900;
        const double ControlsWidth = 320;

        var wide = Width >= WideThreshold && Width > Height;
        var layout = (IsWide: wide, ControlsOnRight: wide && _viewModel.IsWideControlsOnRight);

        if (_appliedLayout == layout)
        {
            return;
        }

        _appliedLayout = layout;
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
            Place(MiniMap, row: 1, column: 0, columnSpan: 1);
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
        var controlsFirst = !layout.ControlsOnRight;

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
        Place(MiniMap, row: 1, column: boardColumn, columnSpan: 1);
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

        // Register before awaiting initialization so a window stop during loading can keep
        // the new session suspended. Window resume also refreshes accessibility state.
        _lifecycle.Show(_viewModel);

        if (_viewModel.Session is null)
        {
            // Resumes the save named by the route, or starts a fresh puzzle.
            await _viewModel.InitialiseAsync();
        }
        else
        {
            // Returning from How to play or Options: apply its settings to the existing board.
            await _viewModel.RefreshSettingsAsync();
        }
    }

    protected override async void OnDisappearing()
    {
        base.OnDisappearing();

        // This covers page navigation; App forwards actual window background/resume events.
        await _lifecycle.HideAsync(_viewModel);
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
    /// see <see cref="Squarebuzz.Presentation.Services.IAccessibilityState"/>.
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

                // The square's description is now stale, and a screen reader reads whatever the
                // element says at the moment it is focused. Refreshing it is OnBoardChanged's
                // job, which TapCell reaches for every move it applies - doing it here as well
                // just re-described four hundred buttons twice.
                cell.Clicked += (_, _) => _viewModel.TapCell(index);

                CellOverlay.Add(cell, column, row);
            }
        }

        CellOverlay.IsVisible = true;
    }

    /// <summary>Re-reads every square's description after a move or language change, preserving focus.</summary>
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

        if (MiniMap.IsVisible)
        {
            MiniMap.UpdateCells(_viewModel.Session);
        }

        // TouchedCellChanged fires before the move is applied, so the magnifier's first snapshot
        // is pre-paint. Re-reading it here is what makes it show the cell as it now is rather
        // than as it was a moment ago.
        if (_magnifiedIndex >= 0 && MagnifierPanel.IsVisible)
        {
            Magnifier.ShowCell(_magnifiedIndex);
        }
    }

    /// <summary>
    /// Shows the overview map only when it earns its corner: a 15-and-up grid whose drawn size
    /// exceeds the scroll window, which is exactly when the player loses sight of parts of it.
    /// </summary>
    private void UpdateMiniMap()
    {
        var puzzle = _viewModel.Session?.Puzzle;
        var scrollable = Board.Width > BoardHost.Width + 1 || Board.Height > BoardHost.Height + 1;
        var wanted = puzzle is { Width: >= 15 } && scrollable;

        if (MiniMap.IsVisible != wanted)
        {
            MiniMap.IsVisible = wanted;
        }

        if (!wanted)
        {
            return;
        }

        MiniMap.UpdateCells(_viewModel.Session);
        UpdateMiniMapViewport();
    }

    private void UpdateMiniMapViewport()
    {
        if (!MiniMap.IsVisible || Board.Width <= 0 || Board.Height <= 0)
        {
            return;
        }

        MiniMap.UpdateViewport(
            BoardHost.ScrollX / Board.Width,
            BoardHost.ScrollY / Board.Height,
            Math.Min(1, BoardHost.Width / Board.Width),
            Math.Min(1, BoardHost.Height / Board.Height));
    }

    private void OnMistakeMade(object? sender, int index)
    {
        Buzz(HapticFeedbackType.LongPress);

        // The design's "no" gesture: four quick beats of ±7, alongside the warn tint. Never a
        // modal, never a sound - a slip should cost a beat, not a telling-off.
        if (!Services.MotionPreferences.ReduceMotion)
        {
            _ = ShakeBoardAsync();
        }

        Board.FlashMistake(index);
    }

    /// <summary>
    /// The "no" gesture. One at a time, and it always puts the board back where it found it.
    /// </summary>
    /// <remarks>
    /// Started without being awaited, so a second mistake arriving mid-shake used to start a
    /// second sequence against the same <c>TranslationX</c>. The two then interleaved, and
    /// whichever finished first left its own final value behind - so a run of mistakes could
    /// leave the board sitting seven units off-centre for the rest of the game. A drag across a
    /// row of wrong squares produces exactly that run, several times a minute.
    /// </remarks>
    private async Task ShakeBoardAsync()
    {
        if (_isShaking)
        {
            return;
        }

        _isShaking = true;

        try
        {
            await Board.TranslateToAsync(-7, 0, 50, Easing.Linear);
            await Board.TranslateToAsync(7, 0, 100, Easing.Linear);
            await Board.TranslateToAsync(-7, 0, 100, Easing.Linear);
            await Board.TranslateToAsync(7, 0, 100, Easing.Linear);
            await Board.TranslateToAsync(0, 0, 50, Easing.Linear);
        }
        finally
        {
            // Belt and braces: navigating away mid-shake cancels the animation part-way, and a
            // board left translated would still be translated when the page is next shown.
            Board.TranslationX = 0;
            _isShaking = false;
        }
    }

    private async void OnHintGranted(object? sender, int index)
    {
        // Bring the cell on screen first: on a scrolling board a hint could land entirely out
        // of view, and the player paid for it. The ring only starts once the camera has arrived.
        await ScrollHintedCellIntoViewAsync(index);
        Board.ShowHint(index);
    }

    /// <summary>
    /// Centres the hinted cell in the scroll window, and dims the minimap out of the way when
    /// the cell ends up underneath it.
    /// </summary>
    private async Task ScrollHintedCellIntoViewAsync(int index)
    {
        var layout = Board.CurrentLayout;

        if (layout.Columns == 0 || layout.CellSize <= 0)
        {
            return;
        }

        // Same test UpdateMiniMap uses: a board that fits its window has nothing to scroll.
        var scrollable = Board.Width > BoardHost.Width + 1 || Board.Height > BoardHost.Height + 1;

        var column = index % layout.Columns;
        var row = index / layout.Columns;
        var (cellX, cellY) = layout.CellOrigin(column, row);

        var targetX = BoardHost.ScrollX;
        var targetY = BoardHost.ScrollY;

        if (scrollable)
        {
            targetX = Math.Clamp(
                cellX + (layout.CellSize / 2) - (BoardHost.Width / 2),
                0,
                Math.Max(0, Board.Width - BoardHost.Width));
            targetY = Math.Clamp(
                cellY + (layout.CellSize / 2) - (BoardHost.Height / 2),
                0,
                Math.Max(0, Board.Height - BoardHost.Height));

            await BoardHost.ScrollToAsync(targetX, targetY, animated: !Services.MotionPreferences.ReduceMotion);
        }

        DimMiniMapIfCoveringCell(cellX - targetX, cellY - targetY, layout.CellSize);
    }

    /// <summary>
    /// Drops the minimap to near-transparent for the hint's display window when the hinted cell
    /// sits behind it. Dimmed rather than hidden: visibility belongs to UpdateMiniMap, and the
    /// two must not fight.
    /// </summary>
    private void DimMiniMapIfCoveringCell(double viewportX, double viewportY, double cellSize)
    {
        if (!MiniMap.IsVisible)
        {
            return;
        }

        // The minimap's corner, inflated by its margin, in the same viewport coordinates.
        var mapRegion = new Rect(BoardHost.Width - 112, BoardHost.Height - 112, 112, 112);
        var cellRect = new Rect(viewportX, viewportY, cellSize, cellSize);

        if (!mapRegion.IntersectsWith(cellRect))
        {
            return;
        }

        MiniMap.Opacity = 0.15;

        var generation = ++_hintOverlayGeneration;
        Dispatcher.DispatchDelayed(BoardView.HintDisplayDuration, () =>
        {
            if (generation == _hintOverlayGeneration)
            {
                MiniMap.Opacity = 1;
            }
        });
    }

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
