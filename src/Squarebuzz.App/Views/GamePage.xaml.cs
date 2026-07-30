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

        // Board rendering is imperative by nature - a canvas redraw is not a binding - so the
        // ViewModel signals through events and the page translates them into draw calls.
        _viewModel.BoardChanged += OnBoardChanged;
        _viewModel.MistakeMade += OnMistakeMade;
        _viewModel.HintGranted += OnHintGranted;
        _viewModel.PuzzleSolved += OnPuzzleSolved;

        Board.CellPainted += OnCellPainted;
        Board.CrossGestureRecognised += OnCrossGestureRecognised;
        Board.TouchedCellChanged += OnTouchedCellChanged;

        // The board is sized from the space its host offers, not from its own width - see
        // BoardView.AvailableSize for why that distinction matters.
        BoardHost.SizeChanged += OnBoardHostSizeChanged;

        // The page's own size decides the arrangement, and it changes on rotation as well as at
        // first layout.
        SizeChanged += (_, _) => ApplyLayout();
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
    }

    protected override async void OnDisappearing()
    {
        base.OnDisappearing();

        // Covers backgrounding and back-navigation, which are the routes out of the game that
        // no button handles.
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

    private void OnBoardChanged(object? sender, EventArgs e)
    {
        Board.RefreshCells();

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
        await Board.FlashMistakeAsync(index);
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
