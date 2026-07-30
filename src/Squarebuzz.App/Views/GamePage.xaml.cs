using Squarebuzz.App.ViewModels;

namespace Squarebuzz.App.Views;

public partial class GamePage : ContentPage
{
    private readonly GameViewModel _viewModel;

    /// <summary>Cell the magnifier is centred on, or -1 when it is hidden.</summary>
    private int _magnifiedIndex = -1;

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
    }

    private void OnBoardHostSizeChanged(object? sender, EventArgs e)
    {
        if (BoardHost.Width > 0 && BoardHost.Height > 0)
        {
            Board.AvailableSize = new Size(BoardHost.Width, BoardHost.Height);
        }
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
