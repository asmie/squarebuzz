using Squarebuzz.App.ViewModels;

namespace Squarebuzz.App.Views;

public partial class GamePage : ContentPage
{
    private readonly GameViewModel _viewModel;

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
            await _viewModel.StartAsync();
        }
    }

    private void OnCellPainted(object? sender, Controls.CellPaintedEventArgs e) =>
        _viewModel.Paint(e.Index, e.Target);

    private void OnBoardChanged(object? sender, EventArgs e) => Board.RefreshCells();

    private async void OnMistakeMade(object? sender, int index)
    {
        await HapticFeedbackSafely(HapticFeedbackType.LongPress);
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

        await HapticFeedbackSafely(HapticFeedbackType.Click);
        await Reveal.RevealAsync(TimeSpan.FromMilliseconds(700));
    }

    private async void OnCrossGestureRecognised(object? sender, EventArgs e) =>
        await HapticFeedbackSafely(HapticFeedbackType.Click);

    /// <summary>
    /// Haptics are unsupported on some platforms and desktop, and the API throws rather than
    /// no-ops. Feedback is a nicety, so failing to buzz must never interrupt play.
    /// </summary>
    private static Task HapticFeedbackSafely(HapticFeedbackType type)
    {
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

        return Task.CompletedTask;
    }
}
