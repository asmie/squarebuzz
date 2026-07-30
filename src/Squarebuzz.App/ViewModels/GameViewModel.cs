using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Squarebuzz.App.Services;
using Squarebuzz.Core.Abstractions;
using Squarebuzz.Core.Model;

namespace Squarebuzz.App.ViewModels;

/// <summary>
/// Drives the board screen: mode toggle, undo/redo, hints, the timer, pausing and completion.
/// </summary>
/// <remarks>
/// Holds the <see cref="GameSession"/> but never reimplements its rules - every move goes
/// through the session so the domain stays the single source of truth for what is legal.
/// Pause and completion are overlays on this screen rather than separate routes, so the session
/// never has to be serialised across a navigation just to show a summary over the board.
/// </remarks>
public partial class GameViewModel : LocalizedViewModel, IQueryAttributable
{
    /// <summary>Route parameter naming the save to resume.</summary>
    public const string SaveIdParameter = "saveId";

    /// <summary>
    /// How often play is written to disk. Frequent enough that a crash or a task-kill costs
    /// only a few moves, rare enough that it never competes with drawing.
    /// </summary>
    private const int AutosaveEverySeconds = 15;

    private readonly GameSessionFactory _sessions;
    private readonly ISettingsRepository _settingsRepository;
    private readonly IProgressRepository _progress;
    private readonly ISaveGameRepository _saveGames;
    private readonly INavigationService _navigation;
    private readonly IClock _clock;

    private IDispatcherTimer? _timer;
    private GameSettings _settings = GameSettings.Default;

    /// <summary>Identity of this game's row in the save table, so autosaves replace rather than pile up.</summary>
    private Guid _saveId = Guid.NewGuid();

    private Guid? _pendingResumeId;
    private int _secondsSinceAutosave;

    public GameViewModel(
        GameSessionFactory sessions,
        ISettingsRepository settingsRepository,
        IProgressRepository progress,
        ISaveGameRepository saveGames,
        ILocalizationService strings,
        INavigationService navigation,
        IClock clock)
        : base(strings)
    {
        _sessions = sessions;
        _settingsRepository = settingsRepository;
        _progress = progress;
        _saveGames = saveGames;
        _navigation = navigation;
        _clock = clock;
    }

    /// <summary>Raised when the board data changed and the canvas needs redrawing.</summary>
    public event EventHandler? BoardChanged;

    /// <summary>Raised with a cell index when a fill was wrong, so the view can flash it.</summary>
    public event EventHandler<int>? MistakeMade;

    /// <summary>Raised with a cell index when a hint was granted.</summary>
    public event EventHandler<int>? HintGranted;

    /// <summary>Raised when the picture is finished, so the view can play the reveal.</summary>
    public event EventHandler? PuzzleSolved;

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

    [ObservableProperty]
    public partial bool IsSolved { get; private set; }

    [ObservableProperty]
    public partial bool IsPaused { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    public partial int HintsRemaining { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    public partial int Mistakes { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StarsText))]
    public partial int StarRating { get; private set; }

    [ObservableProperty]
    public partial bool CanUndo { get; private set; }

    [ObservableProperty]
    public partial bool CanRedo { get; private set; }

    [ObservableProperty]
    public partial string PuzzleName { get; private set; } = string.Empty;

    public bool HasToast => !string.IsNullOrEmpty(Toast);

    public bool ShowTimer => _settings.Helpers.ShowTimer;

    public int ZoomPercent => _settings.CellZoomPercent;

    public bool BigNumbers => _settings.BigNumbers;

    public TapBehaviour TapBehaviour => _settings.TapBehaviour;

    public bool ShowMagnifier => _settings.ShowMagnifier;

    /// <summary>
    /// Label for the mode toggle. It names the mode the button switches <em>to</em>, which is
    /// the convention children read correctly - "Mark X" means "tapping will now mark X".
    /// </summary>
    public string ModeButtonText => IsCrossMode ? T("fill") : T("cross");

    /// <summary>
    /// Hints and mistakes as one line, composed here rather than assembled in XAML from several
    /// localised spans - simpler markup, and the wording becomes testable.
    /// </summary>
    public string StatusText => $"{T("hints")} {HintsRemaining}    {T("mistakes")} {Mistakes}";

    public string UndoText => T("undo");

    public string RedoText => T("redo");

    public string HintText => T("hint");

    public string RestartText => T("restart");

    public string PauseText => T("pause");

    public string PausedTitle => T("paused");

    public string ResumeText => T("resume");

    public string HowToText => T("howTo");

    public string QuitText => T("quit");

    public string SolvedTitle => T("solved");

    public string TimeLabel => T("time");

    public string NextPuzzleText => T("nextPuzzle");

    public string MenuText => T("menu");

    /// <summary>Filled stars up to the rating, hollow for the rest.</summary>
    public string StarsText => new string('★', StarRating) + new string('☆', Math.Max(0, 3 - StarRating));

    /// <summary>
    /// Picks up a <c>saveId</c> from the route, if the player arrived from Continue.
    /// </summary>
    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (query.TryGetValue(SaveIdParameter, out var raw)
            && Guid.TryParse(raw?.ToString(), out var id))
        {
            _pendingResumeId = id;
        }
    }

    /// <summary>
    /// Entry point for the page: resumes the save the route named, or starts a fresh puzzle.
    /// </summary>
    public async Task InitialiseAsync()
    {
        if (_pendingResumeId is { } id)
        {
            _pendingResumeId = null;

            if (await TryResumeAsync(id))
            {
                return;
            }

            // The save vanished or its picture no longer ships - fall through to a new game
            // rather than leaving the player on an empty board.
        }

        await StartAsync();
    }

    private async Task<bool> TryResumeAsync(Guid id)
    {
        try
        {
            _settings = await _settingsRepository.LoadAsync();

            var save = await _saveGames.GetAsync(id);

            if (save is null)
            {
                return false;
            }

            Session = _sessions.Restore(save, _settings.Helpers);
            _saveId = save.Id;
        }
        catch (Exception)
        {
            return false;
        }

        IsCrossMode = false;
        Session.Mode = PaintMode.Fill;
        IsPaused = false;
        Toast = string.Empty;

        PuzzleName = Session.Puzzle.IsGenerated
            ? T("Puzzle_gen")
            : T($"Puzzle_{Session.Puzzle.Id}");

        SyncFromSession();
        StartTimer();
        NotifySettingsDependentProperties();

        BoardChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>
    /// Writes the game in progress to the save table. Called on pause, on quit and on leaving the
    /// screen, plus periodically while playing.
    /// </summary>
    public async Task AutosaveAsync()
    {
        if (Session is not { } session || session.IsSolved || session.MoveCount == 0)
        {
            // Nothing worth keeping: an untouched board would clutter Continue with a game the
            // player never actually started.
            return;
        }

        try
        {
            await _saveGames.SaveAsync(SavedGame.FromSession(session, _saveId, _clock.Now));
        }
        catch (Exception)
        {
            // A failed autosave costs the player their place, but surfacing it mid-play would
            // be worse. The next autosave will most likely succeed.
        }

        _secondsSinceAutosave = 0;
    }

    /// <summary>Starts a new puzzle from the player's saved preferences.</summary>
    public async Task StartAsync(NewGameOptions? options = null)
    {
        try
        {
            _settings = await _settingsRepository.LoadAsync();
        }
        catch (Exception)
        {
            _settings = GameSettings.Default;
        }

        // A null seed means "surprise me", so replaying gives a different picture rather than
        // the same one over and over.
        var effective = (options ?? _settings.ToNewGameOptions()) with { Helpers = _settings.Helpers };
        Session = _sessions.Create(effective);

        // A new game is a new row: restarting must not overwrite the save it came from.
        _saveId = Guid.NewGuid();
        _secondsSinceAutosave = 0;

        IsCrossMode = false;
        Session.Mode = PaintMode.Fill;
        IsSolved = false;
        IsPaused = false;
        Toast = string.Empty;

        PuzzleName = Session.Puzzle.IsGenerated
            ? T("Puzzle_gen")
            : T($"Puzzle_{Session.Puzzle.Id}");

        SyncFromSession();
        StartTimer();

        NotifySettingsDependentProperties();

        BoardChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Applies a paint request from the board view.</summary>
    public void Paint(int index, CellState target)
    {
        if (Session is not { } session || IsSolved || IsPaused)
        {
            return;
        }

        var outcome = session.Paint(index, target);

        switch (outcome.Result)
        {
            case MoveResult.Mistake:
                ShowToast(T("mistakeMsg"));
                MistakeMade?.Invoke(this, index);
                break;

            case MoveResult.Applied when outcome.CompletedALine:
                ShowToast(T("lineDone"));
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
        if (Session is not { } session || IsPaused)
        {
            return;
        }

        if (session.HintsRemaining <= 0)
        {
            ShowToast(T("noHints"));
            return;
        }

        var hint = session.UseHint();

        if (hint is null)
        {
            return;
        }

        ShowToast(T("hintUsed"));
        HintGranted?.Invoke(this, hint.Index);

        SyncFromSession();
        BoardChanged?.Invoke(this, EventArgs.Empty);

        if (session.IsSolved)
        {
            _ = HandleCompletionAsync();
        }
    }

    [RelayCommand]
    private async Task PauseAsync()
    {
        if (IsSolved)
        {
            return;
        }

        IsPaused = true;
        StopTimer();

        // Pausing is the most likely moment for the player to walk away, so write now rather
        // than waiting for the next tick.
        await AutosaveAsync();
    }

    [RelayCommand]
    private void Resume()
    {
        IsPaused = false;
        StartTimer();
    }

    [RelayCommand]
    private async Task RestartAsync()
    {
        IsPaused = false;
        await StartAsync(SameSettingsFreshPuzzle());
    }

    /// <summary>A fresh puzzle with the same settings - the "Next" button on the win screen.</summary>
    [RelayCommand]
    private async Task NextPuzzleAsync() => await StartAsync(SameSettingsFreshPuzzle());

    /// <summary>
    /// The current options with the seed cleared, so a replay keeps the player's size, pack and
    /// challenge but draws a different picture. Null when there is no session yet, in which case
    /// <see cref="StartAsync"/> falls back to saved settings.
    /// </summary>
    private NewGameOptions? SameSettingsFreshPuzzle()
    {
        // Written out rather than as `Session?.Origin with { ... }`, which compiles but
        // dereferences a possibly-null value and would throw once Origin was ever null.
        var origin = Session?.Origin;

        return origin is null ? null : origin with { Seed = null };
    }

    [RelayCommand]
    private async Task QuitAsync()
    {
        StopTimer();
        await AutosaveAsync();
        await _navigation.ResetToAsync(Routes.Menu);
    }

    [RelayCommand]
    private async Task HowToAsync()
    {
        await _navigation.GoToAsync(Routes.HowTo, new Dictionary<string, object> { ["titleKey"] = "howToTitle" });
    }

    private async Task HandleCompletionAsync()
    {
        if (Session is not { } session)
        {
            return;
        }

        StopTimer();
        IsSolved = true;
        IsPaused = false;
        PuzzleSolved?.Invoke(this, EventArgs.Empty);

        try
        {
            // Drop the save first: a completed puzzle in Continue would be a dead end, and it
            // must go even if recording progress then fails.
            await _saveGames.DeleteAsync(_saveId);
        }
        catch (Exception)
        {
            // Leaves a stale save behind; the player can delete it from Continue.
        }

        try
        {
            await _progress.RecordCompletionAsync(new PuzzleCompletion(
                session.Puzzle.IsGenerated ? null : session.Puzzle.Id,
                session.StarRating,
                session.Elapsed,
                CountFilledCells(session.Puzzle),
                session.HintsUsed,
                _clock.Now));
        }
        catch (Exception)
        {
            // Losing a progress write must not spoil the win. The star total will simply be
            // short next launch, which is far better than an error dialog after a child wins.
        }
    }

    private static int CountFilledCells(Puzzle puzzle)
    {
        var count = 0;
        var solution = puzzle.Solution;

        for (var i = 0; i < solution.Length; i++)
        {
            if (solution[i])
            {
                count++;
            }
        }

        return count;
    }

    private void NotifySettingsDependentProperties()
    {
        OnPropertyChanged(nameof(ShowTimer));
        OnPropertyChanged(nameof(ZoomPercent));
        OnPropertyChanged(nameof(BigNumbers));
        OnPropertyChanged(nameof(TapBehaviour));
        OnPropertyChanged(nameof(ShowMagnifier));
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
        if (Session is not { } session || session.IsSolved || IsPaused)
        {
            return;
        }

        session.Advance(TimeSpan.FromSeconds(1));
        UpdateElapsedText();

        if (++_secondsSinceAutosave >= AutosaveEverySeconds)
        {
            _ = AutosaveAsync();
        }
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
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            StopTimer();
        }

        base.Dispose(disposing);
    }
}
