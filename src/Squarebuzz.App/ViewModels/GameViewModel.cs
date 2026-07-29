using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Squarebuzz.App.Services;
using Squarebuzz.Core.Abstractions;
using Squarebuzz.Core.Model;
using Squarebuzz.Core.Solving;

namespace Squarebuzz.App.ViewModels;

/// <summary>
/// Drives the board screen: mode toggle, undo/redo, hints, the timer, and completion.
/// </summary>
/// <remarks>
/// Holds the <see cref="GameSession"/> but never reimplements its rules - every move goes
/// through the session so the domain stays the single source of truth for what is legal.
/// </remarks>
public partial class GameViewModel : ViewModelBase, IDisposable
{
    private readonly GameSessionFactory _sessions;
    private readonly ISettingsRepository _settingsRepository;
    private readonly IProgressRepository _progress;
    private readonly ILocalizationService _strings;
    private readonly IClock _clock;

    private IDispatcherTimer? _timer;
    private GameSettings _settings = GameSettings.Default;
    private bool _disposed;

    public GameViewModel(
        GameSessionFactory sessions,
        ISettingsRepository settingsRepository,
        IProgressRepository progress,
        ILocalizationService strings,
        IClock clock)
    {
        _sessions = sessions;
        _settingsRepository = settingsRepository;
        _progress = progress;
        _strings = strings;
        _clock = clock;

        // Every localised label on this screen is a computed property, so a language change is
        // one broadcast rather than a subscription per label.
        _strings.LanguageChanged += OnLanguageChanged;
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(ModeButtonText));
        OnPropertyChanged(nameof(UndoText));
        OnPropertyChanged(nameof(RedoText));
        OnPropertyChanged(nameof(HintText));
        OnPropertyChanged(nameof(RestartText));
    }

    /// <summary>Raised when the board data changed and the canvas needs redrawing.</summary>
    public event EventHandler? BoardChanged;

    /// <summary>Raised with a cell index when a fill was wrong, so the view can flash it.</summary>
    public event EventHandler<int>? MistakeMade;

    /// <summary>Raised with a cell index when a hint was granted.</summary>
    public event EventHandler<int>? HintGranted;

    [ObservableProperty]
    public partial GameSession? Session { get; private set; }

    [ObservableProperty]
    public partial string ElapsedText { get; private set; } = "0:00";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasToast))]
    public partial string Toast { get; private set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ModeButtonText))]
    public partial bool IsCrossMode { get; private set; }

    public bool HasToast => !string.IsNullOrEmpty(Toast);

    /// <summary>
    /// Label for the mode toggle. It names the mode the button switches <em>to</em>, which is
    /// the convention children read correctly - "Mark X" means "tapping will now mark X".
    /// </summary>
    public string ModeButtonText => IsCrossMode
        ? _strings.GetString("fill")
        : _strings.GetString("cross");

    /// <summary>
    /// Hints and mistakes as one line.
    /// </summary>
    /// <remarks>
    /// Composed here rather than assembled in XAML from several localised spans. Building it
    /// in the ViewModel keeps the markup trivial, avoids indexer bindings against a source
    /// other than the binding context, and makes the wording testable.
    /// </remarks>
    public string StatusText =>
        $"{_strings.GetString("hints")} {HintsRemaining}    {_strings.GetString("mistakes")} {Mistakes}";

    public string UndoText => _strings.GetString("undo");

    public string RedoText => _strings.GetString("redo");

    public string HintText => _strings.GetString("hint");

    public string RestartText => _strings.GetString("restart");

    [ObservableProperty]
    public partial bool IsSolved { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    public partial int HintsRemaining { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    public partial int Mistakes { get; private set; }

    [ObservableProperty]
    public partial int StarRating { get; private set; }

    [ObservableProperty]
    public partial bool CanUndo { get; private set; }

    [ObservableProperty]
    public partial bool CanRedo { get; private set; }

    [ObservableProperty]
    public partial string PuzzleName { get; private set; } = string.Empty;

    public bool ShowTimer => _settings.Helpers.ShowTimer;

    public int ZoomPercent => _settings.CellZoomPercent;

    public bool BigNumbers => _settings.BigNumbers;

    public TapBehaviour TapBehaviour => _settings.TapBehaviour;

    /// <summary>Starts a new puzzle from the player's saved preferences.</summary>
    public async Task StartAsync(NewGameOptions? options = null)
    {
        _settings = await _settingsRepository.LoadAsync();

        var effective = options ?? _settings.ToNewGameOptions();
        Session = _sessions.Create(effective with { Helpers = _settings.Helpers });

        IsCrossMode = false;
        Session.Mode = PaintMode.Fill;
        IsSolved = false;
        Toast = string.Empty;

        PuzzleName = Session.Puzzle.IsGenerated
            ? _strings.GetString("Puzzle_gen")
            : _strings.GetString($"Puzzle_{Session.Puzzle.Id}");

        SyncFromSession();
        StartTimer();

        BoardChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Applies a paint request from the board view.</summary>
    public void Paint(int index, CellState target)
    {
        if (Session is not { } session || IsSolved)
        {
            return;
        }

        var outcome = session.Paint(index, target);

        switch (outcome.Result)
        {
            case MoveResult.Mistake:
                ShowToast(_strings.GetString("mistakeMsg"));
                MistakeMade?.Invoke(this, index);
                break;

            case MoveResult.Applied when outcome.CompletedALine:
                ShowToast(_strings.GetString("lineDone"));
                break;

            default:
                break;
        }

        SyncFromSession();
        BoardChanged?.Invoke(this, EventArgs.Empty);

        if (outcome.SolvedPuzzle)
        {
            _ = HandleCompletionAsync();
        }
    }

    [RelayCommand]
    private void ToggleMode()
    {
        if (Session is not { } session)
        {
            return;
        }

        IsCrossMode = !IsCrossMode;
        session.Mode = IsCrossMode ? PaintMode.Cross : PaintMode.Fill;
    }

    [RelayCommand]
    private void Undo()
    {
        if (Session?.Undo() != true)
        {
            return;
        }

        SyncFromSession();
        BoardChanged?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void Redo()
    {
        if (Session?.Redo() != true)
        {
            return;
        }

        SyncFromSession();
        BoardChanged?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void UseHint()
    {
        if (Session is not { } session)
        {
            return;
        }

        if (session.HintsRemaining <= 0)
        {
            ShowToast(_strings.GetString("noHints"));
            return;
        }

        var hint = session.UseHint();

        if (hint is null)
        {
            return;
        }

        ShowToast(_strings.GetString("hintUsed"));
        HintGranted?.Invoke(this, hint.Index);

        SyncFromSession();
        BoardChanged?.Invoke(this, EventArgs.Empty);

        if (session.IsSolved)
        {
            _ = HandleCompletionAsync();
        }
    }

    [RelayCommand]
    private async Task RestartAsync()
    {
        await StartAsync(Session?.Origin);
    }

    private async Task HandleCompletionAsync()
    {
        if (Session is not { } session)
        {
            return;
        }

        StopTimer();
        IsSolved = true;
        ShowToast(_strings.GetString("solved"));

        try
        {
            await _progress.RecordCompletionAsync(new PuzzleCompletion(
                session.Puzzle.IsGenerated ? null : session.Puzzle.Id,
                session.StarRating,
                session.Elapsed,
                session.Puzzle.Solution.ToArray().Count(filled => filled),
                session.HintsUsed,
                _clock.Now));
        }
        catch (Exception)
        {
            // Losing a progress write must not spoil the win. The star total will simply be
            // short next launch, which is far better than an error dialog after a child wins.
        }
    }

    private void SyncFromSession()
    {
        if (Session is not { } session)
        {
            return;
        }

        HintsRemaining = session.HintsRemaining;
        Mistakes = session.Mistakes;
        StarRating = session.StarRating;
        CanUndo = session.CanUndo;
        CanRedo = session.CanRedo;
        IsSolved = session.IsSolved;
        UpdateElapsedText();
    }

    private void StartTimer()
    {
        StopTimer();

        var dispatcher = Application.Current?.Dispatcher;

        if (dispatcher is null)
        {
            return;
        }

        _timer = dispatcher.CreateTimer();
        _timer.Interval = TimeSpan.FromSeconds(1);
        _timer.IsRepeating = true;
        _timer.Tick += OnTimerTick;
        _timer.Start();
    }

    private void OnTimerTick(object? sender, EventArgs e)
    {
        if (Session is not { } session || session.IsSolved)
        {
            return;
        }

        session.Advance(TimeSpan.FromSeconds(1));
        UpdateElapsedText();
    }

    private void StopTimer()
    {
        if (_timer is null)
        {
            return;
        }

        _timer.Tick -= OnTimerTick;
        _timer.Stop();
        _timer = null;
    }

    private void UpdateElapsedText()
    {
        var elapsed = Session?.Elapsed ?? TimeSpan.Zero;

        // m:ss, matching the prototype's fmtTime - hours are unrealistic for one puzzle.
        ElapsedText = $"{(int)elapsed.TotalMinutes}:{elapsed.Seconds:00}";
    }

    private void ShowToast(string message)
    {
        Toast = message;

        // Clears itself, so no screen has to remember to tidy up after a transient message.
        _ = Task.Delay(1500).ContinueWith(
            _ => MainThread.BeginInvokeOnMainThread(() =>
            {
                if (Toast == message)
                {
                    Toast = string.Empty;
                }
            }),
            TaskScheduler.Default);
    }

    /// <summary>
    /// Stops the timer. Without this a ViewModel left behind by navigation keeps ticking and
    /// keeps itself alive through the dispatcher's handler list.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        StopTimer();
        _strings.LanguageChanged -= OnLanguageChanged;
        _disposed = true;

        GC.SuppressFinalize(this);
    }
}
