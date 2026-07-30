using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Squarebuzz.App.Services;
using Squarebuzz.Core.Abstractions;
using Squarebuzz.Core.Model;
using Squarebuzz.Core.Progression;

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

    /// <summary>Route parameter naming a specific picture to play, as the Gallery does.</summary>
    public const string PuzzleIdParameter = "puzzleId";

    /// <summary>Route parameter marking this game as today's daily puzzle.</summary>
    public const string DailyParameter = "daily";

    /// <summary>
    /// How often play is written to disk. Frequent enough that a crash or a task-kill costs
    /// only a few moves, rare enough that it never competes with drawing.
    /// </summary>
    private const int AutosaveEverySeconds = 15;

    private readonly GameSessionFactory _sessions;
    private readonly ISettingsRepository _settingsRepository;
    private readonly IProgressRepository _progress;
    private readonly ISaveGameRepository _saveGames;
    private readonly IPuzzleRepository _puzzles;
    private readonly INavigationService _navigation;
    private readonly IClock _clock;
    private readonly IScreenTimeMonitor _screenTime;
    private readonly IAudioService _audio;
    private readonly INarrationService _narration;

    private IDispatcherTimer? _timer;
    private GameSettings _settings = GameSettings.Default;

    /// <summary>Identity of this game's row in the save table, so autosaves replace rather than pile up.</summary>
    private Guid _saveId = Guid.NewGuid();

    private Guid? _pendingResumeId;
    private string? _pendingPuzzleId;
    private bool _isDaily;
    private int _secondsSinceAutosave;

    public GameViewModel(
        GameSessionFactory sessions,
        ISettingsRepository settingsRepository,
        IProgressRepository progress,
        ISaveGameRepository saveGames,
        IPuzzleRepository puzzles,
        ILocalizationService strings,
        INavigationService navigation,
        IClock clock,
        IScreenTimeMonitor screenTime,
        IAudioService audio,
        INarrationService narration)
        : base(strings)
    {
        _sessions = sessions;
        _settingsRepository = settingsRepository;
        _progress = progress;
        _saveGames = saveGames;
        _puzzles = puzzles;
        _navigation = navigation;
        _clock = clock;
        _screenTime = screenTime;
        _audio = audio;
        _narration = narration;
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
    [NotifyPropertyChangedFor(nameof(ElapsedDescription))]
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

    /// <summary>
    /// The break reminder, shown once when the parent-set screen-time limit is reached.
    /// </summary>
    /// <remarks>
    /// Deliberately a modal overlay rather than a toast, and it stops the clock while it is up.
    /// A message a child can play straight through is not a reminder. It is not a lockout
    /// either - "A little longer" resumes - because the limit is guidance, not a punishment.
    /// </remarks>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BreakBody))]
    public partial bool IsBreakReminderOpen { get; private set; }

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

    /// <summary>Read by the page before every buzz, so the Haptics switch is actually obeyed.</summary>
    public bool HapticsEnabled => _settings.Haptics;

    /// <summary>
    /// Column for each action button, so the row can be ordered for the player's hand -
    /// "Buttons on: Left / Right".
    /// </summary>
    /// <remarks>
    /// <para>
    /// Undo is the button a child reaches for most, so it belongs under the thumb rather than
    /// across the screen from it: right-handed puts it at the right-hand end. Reordering the row
    /// is the whole effect - our layout is full-width, so there is no cluster to move to the
    /// other side as the prototype's was.
    /// </para>
    /// <para>
    /// Done by binding <c>Grid.Column</c> rather than by setting <c>FlowDirection</c> on the row.
    /// FlowDirection reads better in markup and does reverse the columns, but only when it is set
    /// before the grid lays out; changing it afterwards left the buttons where they were, so
    /// switching hands mid-game did nothing. Only a screenshot showed that.
    /// </para>
    /// </remarks>
    public int UndoColumn => IsRightHanded ? 3 : 0;

    public int RedoColumn => IsRightHanded ? 2 : 1;

    public int HintColumn => IsRightHanded ? 1 : 2;

    public int RestartColumn => IsRightHanded ? 0 : 3;

    private bool IsRightHanded => _settings.Handedness == Handedness.Right;

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

    public string BreakTitle => T("breakTitle");

    /// <summary>
    /// The reminder names the figure the parent set, so the child is told a real number rather
    /// than "a while". Rounded up, because "0 minutes" would be nonsense at the moment it fires.
    /// </summary>
    public string BreakBody => Strings.Format(
        "breakBody",
        Math.Max(1, (int)Math.Ceiling(_screenTime.Played.TotalMinutes)));

    public string BreakKeepText => T("breakKeep");

    public string BreakStopText => T("breakStop");

    public string SolvedTitle => T("solved");

    public string TimeLabel => T("time");

    public string NextPuzzleText => T("nextPuzzle");

    public string MenuText => T("menu");

    /// <summary>Filled stars up to the rating, hollow for the rest.</summary>
    public string StarsText => new string('★', StarRating) + new string('☆', Math.Max(0, 3 - StarRating));

    /// <summary>Star row read as words, since "★★☆" is not something a screen reader can say.</summary>
    public string StarsDescription => Strings.Format("a11yStarRating", StarRating);

    /// <summary>
    /// What a screen reader is told about the board.
    /// </summary>
    /// <remarks>
    /// A <c>GraphicsView</c> contributes nothing to the accessibility tree - the board is simply
    /// absent from it - so this is the whole of what a screen reader can convey about the puzzle.
    /// It says so, too: promising a playable board and then providing no way to reach a square
    /// would be worse than admitting the limit. See the README on what is still missing.
    /// </remarks>
    public string BoardDescription
    {
        get
        {
            if (Session is not { } session)
            {
                return string.Empty;
            }

            var total = CountFilledCells(session.Puzzle);

            return Strings.Format(
                       "a11yBoard",
                       session.Puzzle.Width,
                       session.Puzzle.Height,
                       session.FilledCount,
                       total)
                   + " " + T("a11yBoardNote");
        }
    }

    /// <summary>The timer as a sentence; "5:26" alone is read as a pair of numbers.</summary>
    public string ElapsedDescription => Strings.Format("a11yTime", ElapsedText);

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

        if (query.TryGetValue(PuzzleIdParameter, out var puzzleId)
            && puzzleId?.ToString() is { Length: > 0 } picked)
        {
            _pendingPuzzleId = picked;
        }

        if (query.ContainsKey(DailyParameter))
        {
            _isDaily = true;
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

        if (_isDaily)
        {
            _settings = await LoadSettingsSafelyAsync();

            // Seeded from today's date, so it is the same puzzle for everyone and survives a
            // restart. See DailyPuzzle.
            await StartAsync(DailyPuzzle.OptionsFor(_clock.Today, _settings.Helpers));
            return;
        }

        if (_pendingPuzzleId is { } chosen)
        {
            _pendingPuzzleId = null;

            // The chosen picture overrides size and pack; those are still carried so the save
            // record and a later "Next" keep the player's other preferences.
            _settings = await LoadSettingsSafelyAsync();
            await StartAsync(_settings.ToNewGameOptions() with { PuzzleId = chosen, Seed = null });
            return;
        }

        await StartAsync();
    }

    private async Task<GameSettings> LoadSettingsSafelyAsync()
    {
        try
        {
            return await _settingsRepository.LoadAsync();
        }
        catch (Exception)
        {
            // Defaults get the player into a game; Options can put things right.
            return GameSettings.Default;
        }
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
        IsBreakReminderOpen = false;
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
        _settings = await LoadSettingsSafelyAsync();

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
        IsBreakReminderOpen = false;
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
        if (Session is not { } session || IsSolved || IsPaused || IsBreakReminderOpen)
        {
            return;
        }

        var outcome = session.Paint(index, target);

        switch (outcome.Result)
        {
            case MoveResult.Mistake:
                ShowToast(T("mistakeMsg"));
                MistakeMade?.Invoke(this, index);

                // No sound here on purpose - see GameSound.
                break;

            case MoveResult.Applied when outcome.CompletedALine:
                ShowToast(T("lineDone"));

                // The line sound instead of the cell sound, not as well as: the completion is
                // the more informative of the two, and both at once is just noise.
                _audio.Play(GameSound.LineComplete);
                break;

            case MoveResult.Applied:
                _audio.Play(SoundFor(target));
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

    /// <summary>The sound for a mark that was just applied. Clearing a cell gets its own.</summary>
    private static GameSound SoundFor(CellState target) => target switch
    {
        CellState.Filled => GameSound.Fill,
        CellState.Crossed => GameSound.Cross,
        _ => GameSound.Erase,
    };

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
        _audio.Play(GameSound.Hint);
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

    /// <summary>Dismisses the break reminder and carries on playing.</summary>
    [RelayCommand]
    private void DismissBreakReminder() => IsBreakReminderOpen = false;

    [RelayCommand]
    private async Task RestartAsync()
    {
        IsPaused = false;
        await StartAsync(SameSettingsFreshPuzzle());
    }

    /// <summary>A fresh puzzle with the same settings - the "Next" button on the win screen.</summary>
    [RelayCommand]
    private async Task NextPuzzleAsync()
    {
        // Moving on from the daily means leaving it behind: the next puzzle is an ordinary one,
        // and must not be recorded as today's daily.
        _isDaily = false;

        await StartAsync(SameSettingsFreshPuzzle());
    }

    /// <summary>
    /// The current options with the seed cleared, so a replay keeps the player's size, pack and
    /// challenge but draws a different picture. Null when there is no session yet, in which case
    /// <see cref="StartAsync"/> falls back to saved settings.
    /// </summary>
    private NewGameOptions? SameSettingsFreshPuzzle()
    {
        // Restarting the daily has to give back the same puzzle - it is *today's* picture, and
        // handing out a different one would also let a different picture be recorded as the
        // daily. Only an ordinary game gets a new seed.
        if (_isDaily)
        {
            return DailyPuzzle.OptionsFor(_clock.Today, _settings.Helpers);
        }

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
        await _navigation.GoToAsync(Routes.HowTo);
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

        _audio.Play(GameSound.Win);

        // The picture's name is the reward, so it is said as well as the congratulation - the
        // whole point of the puzzle was finding out what it was.
        var solved = $"{SolvedTitle} {PuzzleName}. {StarsDescription}";

        _narration.Speak(solved);
        Announce(solved);

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

        var completion = new PuzzleCompletion(
            session.Puzzle.IsGenerated ? null : session.Puzzle.Id,
            session.StarRating,
            session.Elapsed,
            CountFilledCells(session.Puzzle),
            session.HintsUsed,
            _clock.Now)
        {
            Size = session.Puzzle.Width,
            PackId = session.Puzzle.Pack,
            Mistakes = session.Mistakes,
            IsDaily = _isDaily,
        };

        try
        {
            var progress = await _progress.RecordCompletionAsync(completion);

            await AwardTrophiesAsync(completion, progress);
        }
        catch (Exception)
        {
            // Losing a progress write must not spoil the win. The star total will simply be
            // short next launch, which is far better than an error dialog after a child wins.
        }
    }

    /// <summary>
    /// Awards whatever the completion just earned. Evaluated from a snapshot taken after the
    /// completion was recorded, so streak and running totals are already up to date.
    /// </summary>
    private async Task AwardTrophiesAsync(PuzzleCompletion completion, PlayerProgress progress)
    {
        var solved = await _progress.GetSolvedPuzzlesAsync();
        var alreadyEarned = (await _progress.GetTrophiesAsync()).Select(t => t.Trophy).ToHashSet();

        var newlyEarned = TrophyEvaluator.Evaluate(new TrophyContext(
            completion,
            progress,
            solved,
            _puzzles.Puzzles,
            alreadyEarned));

        var today = _clock.Today;

        foreach (var trophy in newlyEarned)
        {
            await _progress.AwardTrophyAsync(trophy, today);
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
        OnPropertyChanged(nameof(BoardDescription));
        OnPropertyChanged(nameof(HapticsEnabled));
        OnPropertyChanged(nameof(UndoColumn));
        OnPropertyChanged(nameof(RedoColumn));
        OnPropertyChanged(nameof(HintColumn));
        OnPropertyChanged(nameof(RestartColumn));
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

        // The board's accessible description carries the filled count, so it goes stale on every
        // move unless it is raised here - and "2 of 17" while the board is nearly finished is
        // worse than no description at all. Nothing on screen shows this, so only a dump of the
        // accessibility tree catches it.
        OnPropertyChanged(nameof(BoardDescription));
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
        if (Session is not { } session || session.IsSolved || IsPaused || IsBreakReminderOpen)
        {
            return;
        }

        var second = TimeSpan.FromSeconds(1);

        session.Advance(second);
        UpdateElapsedText();

        // Counted here rather than in the monitor's own timer so that only time actually spent
        // playing counts - the guards above are exactly the cases that should not.
        if (_screenTime.Add(second))
        {
            IsBreakReminderOpen = true;

            var reminder = $"{BreakTitle} {BreakBody}";

            _narration.Speak(reminder);
            Announce(reminder);

            // Nothing about a break should risk the board, so this is a save point too.
            _ = AutosaveAsync();
        }

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

    /// <summary>
    /// Sends <paramref name="text"/> to the platform screen reader.
    /// </summary>
    /// <remarks>
    /// A no-op when no screen reader is running, so this is safe to call unconditionally - and
    /// unlike narration it is deliberately not tied to the Voice narration setting, because the
    /// player's screen reader is their choice rather than ours to switch off.
    /// </remarks>
    private static void Announce(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        try
        {
            SemanticScreenReader.Default.Announce(text);
        }
        catch (Exception)
        {
            // Not every platform implements it, and an announcement is never worth a crash.
        }
    }

    private void ShowToast(string message)
    {
        Toast = message;

        // Every transient message in the game goes through here, so narrating it once at the
        // funnel covers "line done", "oops" and "hint used" without three separate calls that
        // a fourth message could later be added alongside and forget.
        _narration.Speak(message);

        // And the same message to whatever screen reader the player is using. A toast that
        // appears and fades is invisible to one otherwise: nothing takes focus, so nothing is
        // read. Announce is a no-op when no screen reader is running.
        Announce(message);

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
