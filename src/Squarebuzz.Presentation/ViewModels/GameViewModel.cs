using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Squarebuzz.Presentation.Navigation;
using Squarebuzz.Presentation.Services;
using Squarebuzz.Core.Abstractions;
using Squarebuzz.Core.Generation;
using Squarebuzz.Core.Model;
using Squarebuzz.Core.Progression;

namespace Squarebuzz.Presentation.ViewModels;

/// <summary>Coordinates the board, input, timer, pause and completion views.</summary>
/// <remarks>
/// GameSession owns the rules. GamePage forwards Shell route parameters here through
/// ApplyQueryAttributes so Presentation does not depend on MAUI.
/// </remarks>
public partial class GameViewModel : LocalizedViewModel
{
    /// <summary>Route parameter naming the save to resume.</summary>
    public const string SaveIdParameter = "saveId";

    /// <summary>Route parameter naming a specific picture to play, as the Gallery does.</summary>
    public const string PuzzleIdParameter = "puzzleId";

    /// <summary>Route parameter marking this game as today's daily puzzle.</summary>
    public const string DailyParameter = "daily";

    /// <summary>Route parameter naming the Timed Trial tier to run.</summary>
    public const string TimedTierParameter = "tier";

    /// <summary>Route parameter naming the campaign level to play.</summary>
    public const string LevelParameter = "level";

    /// <summary>Typed options selected for a Quick Game, independent of saved preferences.</summary>
    public const string NewGameOptionsParameter = "options";

    private readonly GameSessionFactory _sessions;
    private readonly ISettingsRepository _settingsRepository;
    private readonly IProgressRepository _progress;
    private readonly GameSaveService _saves;
    private readonly IPersistenceDiagnostics _diagnostics;
    private bool _settingsLoadFailed;
    private bool _progressLoadFailed;
    private Guid? _failedResumeId;
    private readonly GameCompletionService _completions;
    private readonly IPuzzleRepository _puzzles;
    private readonly INavigationService _navigation;
    private readonly IClock _clock;
    private readonly GameTimeTracker _time;
    private readonly IAudioService _audio;
    private readonly INarrationService _narration;
    private readonly IAccessibilityState _accessibility;
    private readonly IThemeService _theme;
    private readonly IUiThread _uiThread;
    private readonly IGameTimerFactory _timers;
    private readonly IScreenReader _screenReader;

    private IGameTimer? _timer;
    private bool _isClockSuspended;
    private GameSettings _settings = GameSettings.Default;

    private Guid? _pendingResumeId;
    private string? _pendingPuzzleId;
    private bool _pendingDaily;
    private TimedTier? _pendingTier;
    private int? _pendingLevel;
    private NewGameOptions? _pendingOptions;

    /// <summary>Identifies the most recent toast, so only its own dismissal takes effect.</summary>
    private int _toastToken;

    public GameViewModel(
        GameSessionFactory sessions,
        ISettingsRepository settingsRepository,
        IProgressRepository progress,
        GameSaveService saves,
        IPuzzleRepository puzzles,
        ILocalizationService strings,
        INavigationService navigation,
        IClock clock,
        GameTimeTracker time,
        IAudioService audio,
        INarrationService narration,
        IAccessibilityState accessibility,
        IThemeService theme,
        IUiThread uiThread,
        IGameTimerFactory timers,
        IScreenReader screenReader,
        GameCompletionService completions,
        IPersistenceDiagnostics? diagnostics = null)
        : base(strings)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentNullException.ThrowIfNull(settingsRepository);
        ArgumentNullException.ThrowIfNull(progress);
        ArgumentNullException.ThrowIfNull(saves);
        ArgumentNullException.ThrowIfNull(puzzles);
        ArgumentNullException.ThrowIfNull(navigation);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(audio);
        ArgumentNullException.ThrowIfNull(narration);
        ArgumentNullException.ThrowIfNull(accessibility);
        ArgumentNullException.ThrowIfNull(theme);
        ArgumentNullException.ThrowIfNull(uiThread);
        ArgumentNullException.ThrowIfNull(timers);
        ArgumentNullException.ThrowIfNull(screenReader);
        ArgumentNullException.ThrowIfNull(completions);

        _sessions = sessions;
        _settingsRepository = settingsRepository;
        _progress = progress;
        _saves = saves;
        _diagnostics = diagnostics ?? NullPersistenceDiagnostics.Instance;
        _puzzles = puzzles;
        _navigation = navigation;
        _clock = clock;
        _time = time;
        _audio = audio;
        _narration = narration;
        _accessibility = accessibility;
        _theme = theme;
        _uiThread = uiThread;
        _timers = timers;
        _screenReader = screenReader;
        _completions = completions;
        _saves.Changed += OnPersistenceChanged;
        _completions.Changed += OnPersistenceChanged;

        // Redraw when the active theme changes. Dispose removes this subscription.
        _theme.Changed += OnThemeChanged;

        // Update the accessible cell overlay when screen-reader state changes.
        _accessibility.ScreenReaderStateChanged += OnScreenReaderStateChanged;
    }

    /// <summary>Raised when the board data changed and the canvas needs redrawing.</summary>
    public event EventHandler? BoardChanged;

    /// <summary>Raised when the palette changed, so the canvas re-reads its colours.</summary>
    public event EventHandler? PaletteChanged;

    /// <summary>Raised when <see cref="NeedsCellOverlay"/> changed, so the page rebuilds it.</summary>
    public event EventHandler? OverlayNeedChanged;

    /// <summary>Raised when cell descriptions need translating, without rebuilding their controls.</summary>
    public event EventHandler? CellDescriptionsChanged;

    /// <summary>Raised with a cell index when a fill was wrong, so the view can flash it.</summary>
    public event EventHandler<int>? MistakeMade;

    /// <summary>Raised with a cell index when a fill landed, so the view can pop it.</summary>
    public event EventHandler<int>? CellFilled;

    /// <summary>Raised with a cell index when a hint was granted.</summary>
    public event EventHandler<int>? HintGranted;

    /// <summary>Raised when the picture is finished, so the view can play the reveal.</summary>
    public event EventHandler? PuzzleSolved;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PuzzleName))]
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

    /// <summary>Also refreshes SolvedTimeText when the win overlay opens.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SolvedTimeText))]
    [NotifyPropertyChangedFor(nameof(HasGameOverlay))]
    public partial bool IsSolved { get; private set; }

    /// <summary>Shows the timeout overlay independently of the solved overlay.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasGameOverlay))]
    public partial bool IsTimeUp { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasGameOverlay))]
    public partial bool IsPaused { get; private set; }

    /// <summary>Shows the screen-time reminder and pauses play until it is dismissed.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BreakBody))]
    [NotifyPropertyChangedFor(nameof(HasGameOverlay))]
    public partial bool IsBreakReminderOpen { get; private set; }

    /// <summary>Hides the covered board and controls from accessibility navigation.</summary>
    public bool HasGameOverlay => IsPaused || IsSolved || IsTimeUp || IsBreakReminderOpen;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    public partial int HintsRemaining { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    public partial bool HasUnlimitedHints { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    public partial int Mistakes { get; private set; }

    /// <summary>Hints spent this game, for the win overlay's tiles.</summary>
    [ObservableProperty]
    public partial int HintsUsed { get; private set; }

    /// <summary>Fraction of the picture filled in, 0..1, for the status bar's ring.</summary>
    [ObservableProperty]
    public partial double Progress { get; private set; }

    /// <summary>Refreshes the star display and its accessible description.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StarsText))]
    [NotifyPropertyChangedFor(nameof(StarsDescription))]
    public partial int StarRating { get; private set; }

    [ObservableProperty]
    public partial bool CanUndo { get; private set; }

    [ObservableProperty]
    public partial bool CanRedo { get; private set; }

    /// <summary>Whether the hint button should look pressable: hints left and the game live.</summary>
    [ObservableProperty]
    public partial bool CanUseHint { get; private set; }

    public string PuzzleName => NameForPuzzle();

    public bool HasToast => !string.IsNullOrEmpty(Toast);

    /// <summary>Trials always display the countdown; ordinary games follow the timer preference.</summary>
    public bool ShowTimer => Session is { IsTimed: true } || _settings.Helpers.ShowTimer;

    public int ZoomPercent => _settings.CellZoomPercent;

    public bool BigNumbers => _settings.BigNumbers;

    public TapBehaviour TapBehaviour => _settings.TapBehaviour;

    public bool ShowMagnifier => _settings.ShowMagnifier;

    /// <summary>
    /// True when the page should build its per-cell accessibility overlay over the board.
    /// </summary>
    /// <remarks>
    /// Cheap to read - the state behind it is cached, which matters because the board summary
    /// consults it on every painted cell.
    /// </remarks>
    public bool NeedsCellOverlay => _accessibility.IsScreenReaderActive;

    /// <summary>Current haptics preference, checked by the page before each vibration.</summary>
    public bool HapticsEnabled => _settings.Haptics;

    /// <summary>Places action buttons according to handedness.</summary>
    /// <remarks>
    /// Bind Grid.Column explicitly: changing FlowDirection after the first layout does not
    /// reliably reorder existing children.
    /// </remarks>
    public int UndoColumn => ColumnFor(UndoOrder);

    public int RedoColumn => ColumnFor(RedoOrder);

    public int HintColumn => ColumnFor(HintOrder);

    public int RestartColumn => ColumnFor(RestartOrder);

    public int UndoRow => RowFor(UndoOrder);

    public int RedoRow => RowFor(RedoOrder);

    public int HintRow => RowFor(HintOrder);

    public int RestartRow => RowFor(RestartOrder);

    /// <summary>Uses a side column for controls on wide landscape screens.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(
        nameof(UndoColumn), nameof(RedoColumn), nameof(HintColumn), nameof(RestartColumn),
        nameof(UndoRow), nameof(RedoRow), nameof(HintRow), nameof(RestartRow))]
    public partial bool IsWideLayout { get; set; }

    /// <summary>The controls column follows the selected hand in the wide layout.</summary>
    public bool IsWideControlsOnRight => IsRightHanded;

    // Position of each button in reading order once handedness has been applied.
    private int UndoOrder => IsRightHanded ? 3 : 0;

    private int RedoOrder => IsRightHanded ? 2 : 1;

    private int HintOrder => IsRightHanded ? 1 : 2;

    private int RestartOrder => IsRightHanded ? 0 : 3;

    // One row of four across the bottom, or a 2x2 block in the side column - where four buttons
    // in a row would leave each of them too narrow for its label.
    private int ColumnFor(int order) => IsWideLayout ? order % 2 : order;

    private int RowFor(int order) => IsWideLayout ? order / 2 : 0;

    private bool IsRightHanded => _settings.Handedness == Handedness.Right;

    /// <summary>Names the mark mode selected by the next toggle activation.</summary>
    public string ModeButtonText => IsCrossMode ? T("fill") : T("cross");

    /// <summary>
    /// Whether the mode toggle belongs on screen at all.
    /// </summary>
    /// <remarks>
    /// The accessible cell buttons use activation rather than the canvas's long-press gesture,
    /// so screen-reader users need the toggle even with hold-to-cross selected in Options.
    /// </remarks>
    public bool ShowModeButton => _settings.TapBehaviour == TapBehaviour.ModeButton || NeedsCellOverlay;

    /// <summary>Localised helper counters. Each counter is present only while its helper is enabled.</summary>
    public string StatusText
    {
        get
        {
            var parts = new List<string>(2);

            if (ShowHints)
            {
                parts.Add($"{T("hints")} {(HasUnlimitedHints ? "∞" : HintsRemaining.ToString(CultureInfo.CurrentCulture))}");
            }

            if (ShowMistakes)
            {
                parts.Add($"{T("mistakes")} {Mistakes}");
            }

            return string.Join("    ", parts);
        }
    }

    /// <summary>Whether mistakes are being counted and shown - the "warn on mistakes" helper.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    public partial bool ShowMistakes { get; private set; } = true;

    /// <summary>Whether hints are available at all in this game - the "allow hints" helper.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    public partial bool ShowHints { get; private set; } = true;

    public string UndoText => T("undo");

    public string RedoText => T("redo");

    public string HintText => T("hint");

    public string RestartText => T("restart");

    public string PauseText => T("pause");

    public string PausedTitle => T("paused");

    public string ResumeText => T("resume");

    public string HowToText => T("howTo");

    public string OptionsText => T("options");

    public string QuitText => T("quit");

    public string BreakTitle => T("breakTitle");

    /// <summary>Shows the configured break interval, rounded up to whole minutes.</summary>
    public string BreakBody => Strings.Format(
        "breakBody",
        Math.Max(1, (int)Math.Ceiling(_time.Played.TotalMinutes)));

    public string BreakKeepText => T("breakKeep");

    public string BreakStopText => T("breakStop");

    public string TimeUpTitle => T("timeUp");

    public string TimeUpBody => T("timeUpBody");

    public string TryAgainText => T("tryAgain");

    /// <summary>The campaign level being played, or null. Lives in the session's origin, so it
    /// survives a resume from Continue without any state of this ViewModel's own.</summary>
    private int? CurrentLevel => Session?.Origin?.Level;

    public string SolvedTitle =>
        CurrentLevel is { } level ? Strings.Format("levelDone", level) : T("solved");

    public string TimeLabel => T("time");

    public string HintsLabel => T("hints");

    public string MistakesLabel => T("mistakes");

    /// <summary>Elapsed solve time for the win overlay, including timed trials.</summary>
    /// <remarks>
    /// IsSolved raises this computed property when the overlay opens; ElapsedText is the
    /// remaining countdown during a trial.
    /// </remarks>
    public string SolvedTimeText
    {
        get
        {
            var elapsed = Session?.Elapsed ?? TimeSpan.Zero;

            return $"{(int)elapsed.TotalMinutes}:{elapsed.Seconds:00}";
        }
    }

    public string NextPuzzleText => T(CurrentLevel is null ? "nextPuzzle" : "nextLevel");

    /// <summary>Hidden only on the last level's win screen, where there is nothing to go to.</summary>
    public bool ShowNextButton => !IsCampaignComplete;

    /// <summary>True on the win screen of level 600 - the one game with no "next".</summary>
    public bool IsCampaignComplete => CurrentLevel is { } level && level >= LevelCatalog.LevelCount;

    public string AllLevelsDoneText => T("allLevelsDone");

    public string MenuText => T("menu");

    /// <summary>Filled stars up to the rating, hollow for the rest.</summary>
    public string StarsText => new string('★', StarRating) + new string('☆', Math.Max(0, 3 - StarRating));

    /// <summary>Star row read as words, since "★★☆" is not something a screen reader can say.</summary>
    public string StarsDescription => Strings.Format("a11yStarRating", StarRating);

    /// <summary>Accessible board summary, including whether individual cells are available.</summary>
    /// <remarks>
    /// The canvas has no accessible children. GamePage.BuildCellOverlay adds the per-cell controls.
    /// </remarks>
    public string BoardDescription
    {
        get
        {
            if (Session is not { } session)
            {
                return string.Empty;
            }

            var summary = Strings.Format(
                "a11yBoard",
                session.Puzzle.Width,
                session.Puzzle.Height,
                session.FilledCount,
                session.Puzzle.PictureCellCount);

            // Describe unavailable cells only when the accessible overlay is absent.
            return NeedsCellOverlay ? summary : $"{summary} {T("a11yBoardNote")}";
        }
    }

    /// <summary>Announces the cell position, mark, row clue and column clue.</summary>
    public string DescribeCell(int index)
    {
        if (Session is not { } session || index < 0 || index >= session.Puzzle.CellCount)
        {
            return string.Empty;
        }

        var puzzle = session.Puzzle;
        var column = index % puzzle.Width;
        var row = index / puzzle.Width;

        var state = session[index] switch
        {
            CellState.Filled => T("a11yCellFilled"),
            CellState.Crossed => T("a11yCellCrossed"),
            _ => T("a11yCellEmpty"),
        };

        return Strings.Format(
            "a11yCell",
            row + 1,
            column + 1,
            state,
            Describe(puzzle.RowClues[row]),
            Describe(puzzle.ColumnClues[column]));
    }

    /// <summary>Clue runs as spoken numbers; a blank line reads as "none" rather than "zero".</summary>
    private string Describe(LineClues clues) =>
        clues.IsBlank ? T("a11yClueNone") : string.Join(" ", clues);

    /// <summary>Announces elapsed time in ordinary games and remaining time in trials.</summary>
    public string ElapsedDescription => Strings.Format(
        Session is { IsTimed: true } ? "a11yTimeLeft" : "a11yTime",
        ElapsedText);

    /// <summary>
    /// Picks up the requested save, game mode, picture or typed Quick Game options.
    /// </summary>
    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (query.TryGetValue(NewGameOptionsParameter, out var options) && options is NewGameOptions selected)
        {
            _pendingOptions = selected;
        }

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

        if (query.TryGetValue(TimedTierParameter, out var tier)
            && int.TryParse(tier?.ToString(), out var tierNumber))
        {
            _pendingTier = TimedTrial.Find(tierNumber);
        }

        if (query.TryGetValue(LevelParameter, out var level)
            && int.TryParse(level?.ToString(), out var levelNumber)
            && levelNumber >= 1
            && levelNumber <= LevelCatalog.LevelCount)
        {
            _pendingLevel = levelNumber;
        }

        if (query.ContainsKey(DailyParameter))
        {
            _pendingDaily = true;
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

            if (await TryResumeAsync(id) || _failedResumeId is not null)
            {
                return;
            }

            // Only a missing row falls through to a new game; read/rebuild failures expose retry.
        }

        if (_pendingTier is { } tier)
        {
            _pendingTier = null;
            await StartAsync(settings => tier.ToOptions(settings.Helpers));
            return;
        }

        if (_pendingLevel is { } level)
        {
            _pendingLevel = null;

            // The catalog decides everything about a level; the settings only lend the
            // player's helper preferences.
            await StartAsync(settings => LevelCatalog.Get(level, _puzzles.Puzzles).ToOptions(settings.Helpers));
            return;
        }

        if (_pendingDaily)
        {
            _pendingDaily = false;

            // Seeded from today's date, so it is the same puzzle for everyone and survives a
            // restart. See DailyPuzzle.
            await StartAsync(settings => DailyPuzzle.OptionsFor(_clock.Today, settings.Helpers));
            return;
        }

        if (_pendingPuzzleId is { } chosen)
        {
            _pendingPuzzleId = null;

            // The chosen picture overrides size and pack; those are still carried so the save
            // record and a later "Next" keep the player's other preferences.
            await StartAsync(settings => settings.ToNewGameOptions() with { PuzzleId = chosen, Seed = null });
            return;
        }

        var options = _pendingOptions;
        _pendingOptions = null;
        await StartAsync(options);
    }

    /// <summary>Applies settings changed while the game page was covered.</summary>
    public async Task RefreshSettingsAsync()
    {
        if (Session is not { } session)
        {
            // InitialiseAsync loads settings itself; refreshing before it runs is wasted I/O.
            return;
        }

        _settings = await LoadSettingsSafelyAsync();

        // Apply helper changes to the active session as well as the view.
        session.ApplyHelpers(_settings.Helpers);

        ResetModeIfButtonHidden();

        SyncFromSession();
        NotifySettingsDependentProperties();
    }

    /// <summary>Loads earned packs only when selecting a picture from the Surprise pack.</summary>
    private async Task<IReadOnlySet<string>?> LoadUnlockedPacksAsync(NewGameOptions options)
    {
        var isWildcard = _puzzles.Packs.Any(p =>
            p.IsWildcard && string.Equals(p.Id, options.PackId, StringComparison.Ordinal));

        if (!isWildcard)
        {
            _progressLoadFailed = false;
            OnPersistenceChanged(this, EventArgs.Empty);
            return null;
        }

        try
        {
            var solved = await _progress.GetSolvedPuzzlesAsync();
            _progressLoadFailed = false;
            OnPersistenceChanged(this, EventArgs.Empty);

            return PackUnlocks.UnlockedPackIds(
                _puzzles.Packs,
                _puzzles.Puzzles,
                [.. solved.Select(s => s.PuzzleId)]);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            ReportReadFailure(PersistenceOperation.LoadProgress, error);
            _progressLoadFailed = true;
            OnPersistenceChanged(this, EventArgs.Empty);
            // Unreadable progress must not stop a game starting; the shipped locks still apply.
            return null;
        }
    }

    private async Task<GameSettings> LoadSettingsSafelyAsync()
    {
        try
        {
            var settings = await _settingsRepository.LoadAsync();
            _settingsLoadFailed = false;
            OnPersistenceChanged(this, EventArgs.Empty);
            return settings;
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            ReportReadFailure(PersistenceOperation.LoadSettings, error);
            _settingsLoadFailed = true;
            OnPersistenceChanged(this, EventArgs.Empty);
            // Keep known preferences during a temporary storage outage.
            return _settings;
        }
    }

    private async Task<bool> TryResumeAsync(Guid id)
    {
        AccountElapsedTime();
        StopTimer();
        try
        {
            _settings = await LoadSettingsSafelyAsync();

            var save = await _saves.LoadAsync(id);
            _failedResumeId = null;
            OnPersistenceChanged(this, EventArgs.Empty);

            if (save is null)
            {
                return false;
            }

            Session = _sessions.Restore(save, _settings.Helpers);
            _saves.Attach(Session, save);
            _time.Saved();
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            ReportReadFailure(PersistenceOperation.LoadGame, error, id);
            _failedResumeId = id;
            OnPersistenceChanged(this, EventArgs.Empty);
            return false;
        }

        ActivateSession(Session);
        return true;
    }

    /// <summary>
    /// Puts a freshly created or restored session on screen: clears the previous game's overlays
    /// and mode, pushes its state to the bindings and starts its clock.
    /// </summary>
    private void ActivateSession(GameSession session)
    {
        IsCrossMode = false;
        session.Mode = PaintMode.Fill;
        IsTimeUp = false;
        IsPaused = false;
        IsBreakReminderOpen = false;
        Toast = string.Empty;

        SyncFromSession();
        UpdateElapsedText();
        StopTimer();
        StartTimer();

        NotifySettingsDependentProperties();
        NotifyLevelDependentProperties();

        BoardChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Writes the game in progress to the save table. Called on pause, on quit and on leaving the
    /// screen, plus periodically while playing.
    /// </summary>
    public Task AutosaveAsync()
    {
        // This call is already a save point, including if accounting opens the break reminder.
        AccountElapsedTime(saveOnBreak: false);
        return QueueAutosaveAsync(onlyIfChanged: false);
    }

    private Task QueueAutosaveAsync(bool onlyIfChanged)
    {
        _time.Saved();
        return _saves.SaveAsync(onlyIfChanged);
    }

    private Task AutosaveIfBoardChangedAsync() => QueueAutosaveAsync(onlyIfChanged: true);

    /// <summary>Starts the requested puzzle, falling back to saved choices when no options are supplied.</summary>
    public Task StartAsync(NewGameOptions? options = null) => StartAsync(_ => options);

    /// <summary>
    /// Starts a puzzle whose options depend on the settings, reading the settings exactly once.
    /// </summary>
    /// <remarks>
    /// The routes that need the player's helpers to build their options (a trial, a level, the
    /// daily, a chosen picture) used to load the settings themselves and then have this method
    /// load them again straight after.
    /// </remarks>
    private async Task StartAsync(Func<GameSettings, NewGameOptions?> chooseOptions)
    {
        // Settle the outgoing attempt before replacing it. Loading and generation are not play.
        AccountElapsedTime();
        StopTimer();
        _settings = await LoadSettingsSafelyAsync();

        // New games may request a random seed; Restart supplies the current puzzle's identity.
        var effective = (chooseOptions(_settings) ?? _settings.ToNewGameOptions()) with { Helpers = _settings.Helpers };

        try
        {
            Session = _sessions.Create(effective, await LoadUnlockedPacksAsync(effective));
        }
        catch (PuzzleGenerationException)
        {
            // Generation failure returns to the menu. Do not let an exception escape the page lifecycle callback.
            // ShowToast already speaks and announces it.
            ShowToast(T("genFailed"));

            await _navigation.ResetToAsync(Routes.Menu);
            return;
        }

        // A new game is a new row: restarting must not overwrite the save it came from.
        _saves.Attach(Session);
        _failedResumeId = null;
        OnPersistenceChanged(this, EventArgs.Empty);
        _time.Saved();

        ActivateSession(Session);
    }

    /// <summary>Uses the campaign level number, or the picture name for an authored milestone.</summary>
    private string NameForPuzzle()
    {
        if (Session is not { } session)
        {
            return string.Empty;
        }

        if (session.Puzzle.IsGenerated)
        {
            return CurrentLevel is { } level ? Strings.Format("levelN", level) : T("Puzzle_gen");
        }

        return T($"Puzzle_{session.Puzzle.Id}");
    }

    /// <summary>Refreshes completion labels when a new session changes the game mode.</summary>
    private void NotifyLevelDependentProperties()
    {
        OnPropertyChanged(nameof(SolvedTitle));
        OnPropertyChanged(nameof(NextPuzzleText));
        OnPropertyChanged(nameof(ShowNextButton));
        OnPropertyChanged(nameof(AllLevelsDoneText));
        OnPropertyChanged(nameof(IsCampaignComplete));
    }

    /// <summary>Blocks all board commands while the page is suspended or a pause/break overlay is open.</summary>
    private bool IsInputBlocked => _isClockSuspended || IsPaused || IsBreakReminderOpen;

    /// <summary>Applies a paint request from the board view.</summary>
    /// <param name="index">The square.</param>
    /// <param name="target">What it should become.</param>
    /// <param name="continuesStroke">True for every square of a drag after the first.</param>
    public void Paint(int index, CellState target, bool continuesStroke = false)
    {
        AccountElapsedTime();
        if (Session is not { IsOver: false } session || IsInputBlocked)
        {
            return;
        }

        Apply(index, session.Paint(index, target, continuesStroke), target);
    }

    /// <summary>Applies a tap using the current mark mode. Used by the accessible cell buttons.</summary>
    public void TapCell(int index)
    {
        AccountElapsedTime();
        if (Session is not { IsOver: false } session || IsInputBlocked)
        {
            return;
        }

        var before = session[index];
        var outcome = session.Tap(index);

        // The sound follows what the square became, which a tap decides for itself.
        Apply(index, outcome, session[index] == before ? CellState.Empty : session[index]);
    }

    private void Apply(int index, MoveOutcome outcome, CellState target)
    {
        // Nothing moved - most often a drag sliding over squares that already hold its mark.
        // Syncing would redraw the board, minimap and accessibility overlay for no change.
        if (outcome.Result == MoveResult.NoChange)
        {
            return;
        }

        var mistakesBefore = Mistakes;

        switch (outcome.Result)
        {
            case MoveResult.Mistake:
                ShowToast(T("mistakeMsg"));
                MistakeMade?.Invoke(this, index);

                // No sound here on purpose - see GameSound.
                break;

            case MoveResult.Applied when outcome.CompletedALine:
                ShowToast(T("lineDone"));

                // Play the line-completion sound instead of the ordinary cell sound.
                _audio.Play(GameSound.LineComplete);
                break;

            case MoveResult.Applied:
                _audio.Play(SoundFor(target));
                break;

            default:
                break;
        }

        if (outcome.Result == MoveResult.Applied && target == CellState.Filled)
        {
            CellFilled?.Invoke(this, index);
        }

        SyncFromSession();
        BoardChanged?.Invoke(this, EventArgs.Empty);

        // Persist a charged mistake immediately because it affects the final score.
        if (Mistakes > mistakesBefore)
        {
            _ = AutosaveIfBoardChangedAsync();
        }

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
        AccountElapsedTime();
        if (Session is not { IsOver: false } session || IsInputBlocked)
        {
            return;
        }

        IsCrossMode = !IsCrossMode;
        session.Mode = IsCrossMode ? PaintMode.Cross : PaintMode.Fill;
    }

    [RelayCommand]
    private void Undo()
    {
        AccountElapsedTime();
        if (IsInputBlocked || Session?.Undo() != true)
        {
            return;
        }

        SyncFromSession();
        BoardChanged?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void Redo()
    {
        AccountElapsedTime();
        if (IsInputBlocked || Session?.Redo() != true)
        {
            return;
        }

        SyncFromSession();
        BoardChanged?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void UseHint()
    {
        AccountElapsedTime();
        if (Session is not { IsOver: false } session || IsInputBlocked)
        {
            return;
        }

        if (!session.CanUseHint)
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

        // Update the board snapshot before the hint event scrolls to and highlights the cell.
        SyncFromSession();
        BoardChanged?.Invoke(this, EventArgs.Empty);
        HintGranted?.Invoke(this, hint.Index);

        if (session.IsSolved)
        {
            _ = HandleCompletionAsync();
        }
    }

    [RelayCommand]
    private async Task PauseAsync()
    {
        AccountElapsedTime();
        if (Session is not { IsOver: false } || IsInputBlocked)
        {
            return;
        }

        IsPaused = true;
        StopTimer();

        // Save on pause without waiting for the autosave interval.
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
    private void DismissBreakReminder()
    {
        if (!IsBreakReminderOpen)
        {
            return;
        }

        // A reminder can be dismissed between ticks, or after no ticks at all.
        _time.Rebase();
        IsBreakReminderOpen = false;
    }

    [RelayCommand]
    private async Task RestartAsync()
    {
        IsPaused = false;
        await StartAsync(Session?.Origin?.Restart(_settings.Helpers));
    }

    /// <summary>
    /// The win screen's "Next": the next campaign level when one is being played, otherwise a
    /// fresh puzzle with the same settings.
    /// </summary>
    [RelayCommand]
    private async Task NextPuzzleAsync()
    {
        if (CurrentLevel is { } level)
        {
            if (level >= LevelCatalog.LevelCount)
            {
                // The button is hidden on the last level's win screen; this is its backstop.
                return;
            }

            await StartAsync(LevelCatalog.Get(level + 1, _puzzles.Puzzles).ToOptions(_settings.Helpers));
            return;
        }

        // Moving on leaves daily mode; only Restart preserves the original daily date.
        await StartAsync(Session?.Origin?.NextPuzzle(_settings.Helpers));
    }

    [RelayCommand]
    private async Task QuitAsync()
    {
        AccountElapsedTime();
        StopTimer();
        await AutosaveAsync();
        await _navigation.ResetToAsync(Routes.Menu);
    }

    [RelayCommand]
    private async Task HowToAsync()
    {
        await _navigation.GoToAsync(Routes.HowTo);
    }

    /// <summary>
    /// Options from the pause overlay. The game stays paused underneath and picks the changes
    /// up on return - settings are re-read by NotifySettingsDependentProperties on appearing.
    /// </summary>
    [RelayCommand]
    private async Task OptionsAsync()
    {
        await _navigation.GoToAsync(Routes.Options);
    }

    private async Task HandleCompletionAsync()
    {
        if (Session is not { } session)
        {
            return;
        }

        // Capture completion identity before awaiting: Next can replace the session, save ID and mode.
        var completion = _saves.CaptureCompletion(session);

        StopTimer();
        IsSolved = true;
        IsPaused = false;

        _audio.Play(GameSound.Win);

        // Include the revealed picture name in the completion announcement.
        var solved = $"{SolvedTitle} {PuzzleName}. {StarsDescription}";

        _narration.Speak(solved);
        Announce(solved);

        PuzzleSolved?.Invoke(this, EventArgs.Empty);

        // The app-lifetime completion service retains failures after this page is gone.
        await _saves.CompleteAsync(completion, _completions);
    }


    private void NotifySettingsDependentProperties()
    {
        OnPropertyChanged(nameof(ShowTimer));
        OnPropertyChanged(nameof(ZoomPercent));
        OnPropertyChanged(nameof(BigNumbers));
        OnPropertyChanged(nameof(TapBehaviour));
        OnPropertyChanged(nameof(ShowModeButton));
        OnPropertyChanged(nameof(ShowMagnifier));
        OnPropertyChanged(nameof(BoardDescription));
        OnPropertyChanged(nameof(HapticsEnabled));

        // Notify both row and column bindings when handedness or the wide layout changes.
        OnPropertyChanged(nameof(UndoColumn));
        OnPropertyChanged(nameof(RedoColumn));
        OnPropertyChanged(nameof(HintColumn));
        OnPropertyChanged(nameof(RestartColumn));
        OnPropertyChanged(nameof(UndoRow));
        OnPropertyChanged(nameof(RedoRow));
        OnPropertyChanged(nameof(HintRow));
        OnPropertyChanged(nameof(RestartRow));
        OnPropertyChanged(nameof(IsWideControlsOnRight));
    }

    /// <summary>
    /// Pushes the session's state onto the bound properties. Runs on every painted cell, so it
    /// deliberately does no work that a move cannot change: the clock only moves on a tick, and
    /// the solved time is only read by the win overlay.
    /// </summary>
    private void SyncFromSession()
    {
        if (Session is not { } session)
        {
            return;
        }

        HintsRemaining = session.HintsRemaining;
        HasUnlimitedHints = session.HasUnlimitedHints;
        HintsUsed = session.HintsUsed;
        ShowHints = session.Rules.AllowHints;
        ShowMistakes = session.Rules.WarnOnMistakes;

        var pictureSize = session.Puzzle.PictureCellCount;
        Progress = pictureSize == 0 ? 0 : (double)session.FilledCount / pictureSize;

        Mistakes = session.Mistakes;
        StarRating = session.StarRating;
        CanUndo = session.CanUndo;
        CanRedo = session.CanRedo;
        CanUseHint = session.CanUseHint;
        IsSolved = session.IsSolved;

        // The accessible summary carries the filled count, so it is re-raised when that count
        // moves - not after every move. Crossing a square changes nothing it says, and each raise
        // formats a localised string and pushes a native accessibility update, mid-drag.
        // A new session re-raises it through NotifySettingsDependentProperties.
        if (session.FilledCount != _describedFilledCount)
        {
            _describedFilledCount = session.FilledCount;
            OnPropertyChanged(nameof(BoardDescription));
        }
    }

    /// <summary>The filled count <see cref="BoardDescription"/> was last raised for.</summary>
    private int _describedFilledCount = -1;

    /// <summary>
    /// Suspends timing while the page is covered or the window is in the background.
    /// The suspension also applies to a session whose initialization has not finished yet.
    /// </summary>
    public void SuspendClock()
    {
        AccountElapsedTime();
        _isClockSuspended = true;
        StopTimer();
    }

    /// <summary>
    /// Restarts the clock suspended by <see cref="SuspendClock"/>, unless the game has ended in
    /// the meantime. Safe to call when the clock is already running.
    /// </summary>
    public void ResumeClock()
    {
        _isClockSuspended = false;
        StartTimer();
    }

    private void StartTimer()
    {
        if (_isClockSuspended || IsPaused || Session is not { IsOver: false } || _timer is { IsRunning: true })
        {
            return;
        }

        StopTimer();

        _timer = _timers.CreateSecondTimer();
        _timer.Tick += OnTimerTick;

        // Measured from here, so the first tick counts only the time since the clock started.
        _time.Rebase();

        _timer.Start();
    }

    /// <summary>
    /// Refreshes the clock and runs periodic saves. Input and lifecycle events also account
    /// for elapsed time, so a delayed tick cannot extend a trial's deadline.
    /// </summary>
    private void OnTimerTick(object? sender, EventArgs e)
    {
        // Ignore an event already queued by a timer that has since stopped or been replaced.
        if (!ReferenceEquals(sender, _timer))
        {
            return;
        }

        AccountElapsedTime();
        if (_time.IsSaveDue && !IsInputBlocked && Session is { IsOver: false })
        {
            _ = AutosaveIfBoardChangedAsync();
        }
    }

    /// <summary>Charges active play through now, before any operation can observe stale time.</summary>
    private void AccountElapsedTime(bool saveOnBreak = true)
    {
        if (_timer is not { IsRunning: true } || IsInputBlocked || Session is not { IsOver: false } session)
        {
            return;
        }

        var needsBreak = _time.Account(session);
        UpdateElapsedText();

        if (session.IsTimeUp)
        {
            HandleTimeUp();
            return;
        }

        if (needsBreak)
        {
            IsBreakReminderOpen = true;

            var reminder = $"{BreakTitle} {BreakBody}";

            _narration.Speak(reminder);
            Announce(reminder);

            // Save when opening the break reminder.
            if (saveOnBreak)
            {
                _ = QueueAutosaveAsync(onlyIfChanged: false);
            }
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
        // Trials show remaining time; ordinary games show elapsed time. Both use m:ss.
        var shown = Session is { IsTimed: true } timed ? timed.Remaining : Session?.Elapsed ?? TimeSpan.Zero;

        ElapsedText = $"{(int)shown.TotalMinutes}:{shown.Seconds:00}";
    }

    /// <summary>Ends an expired trial without saving or awarding progress.</summary>
    private void HandleTimeUp()
    {
        StopTimer();

        IsTimeUp = true;
        SyncFromSession();

        var message = $"{TimeUpTitle} {TimeUpBody}";

        _narration.Speak(message);
        Announce(message);
    }

    /// <summary>Announces text through the active screen reader.</summary>
    /// <remarks>
    /// This is independent of the narration preference and is a no-op without a screen reader.
    /// </remarks>
    private void Announce(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        _screenReader.Announce(text);
    }

    private void ShowToast(string message)
    {
        // Repeated toasts extend their display time without repeating speech.
        var isRepeat = Toast == message;

        Toast = message;

        if (!isRepeat)
        {
            // Narrate each new toast here.
            _narration.Speak(message);

            // Announce transient messages to the active screen reader without moving focus.
            Announce(message);
        }

        // Only the latest toast token may dismiss the message; older delayed callbacks are ignored.
        var token = ++_toastToken;

        _ = Task.Delay(1500).ContinueWith(
            _ => _uiThread.BeginInvokeOnMainThread(() =>
            {
                if (_toastToken == token && Toast == message)
                {
                    Toast = string.Empty;
                }
            }),
            TaskScheduler.Default);
    }

    /// <summary>
    /// Re-reads whether a screen reader is running. Called when the board is shown, because the
    /// system's change notification cannot reach a backgrounded app.
    /// </summary>
    public void RefreshAccessibilityState() => _accessibility.Refresh();

    private void ResetModeIfButtonHidden()
    {
        // Returning to gesture-only controls must not leave Cross selected with no toggle
        // available. Keep the selection while the accessible overlay still needs that toggle.
        if (!ShowModeButton && IsCrossMode && Session is { } session)
        {
            IsCrossMode = false;
            session.Mode = PaintMode.Fill;
        }
    }

    public bool HasPersistenceFailure => _saves.HasFailure || _completions.HasFailure
        || _settingsLoadFailed || _progressLoadFailed || _failedResumeId is not null;

    public string PersistenceFailureText => T("storageUnavailable");
    public string RetryPersistenceText => T("tryAgain");

    private void OnPersistenceChanged(object? sender, EventArgs e) =>
        _uiThread.BeginInvokeOnMainThread(() => OnPropertyChanged(nameof(HasPersistenceFailure)));

    private void ReportReadFailure(PersistenceOperation operation, Exception error, Guid? id = null)
    {
        if (error is not OperationCanceledException)
        {
            _diagnostics.Report(operation, error, id);
        }
    }

    [RelayCommand]
    private async Task RetryPersistenceAsync()
    {
        if (_failedResumeId is { } id)
        {
            if (!await TryResumeAsync(id) && _failedResumeId is null)
            {
                await StartAsync();
            }
        }
        else if (_settingsLoadFailed)
        {
            await RefreshSettingsAsync();
        }

        if (_progressLoadFailed && Session?.Origin is { } origin)
        {
            await LoadUnlockedPacksAsync(origin.Options);
        }

        await AutosaveAsync();
        await _completions.RetryAsync();
    }

    private void OnThemeChanged(object? sender, EventArgs e) =>
        PaletteChanged?.Invoke(this, EventArgs.Empty);

    protected override void OnLanguageChangedCore() =>
        CellDescriptionsChanged?.Invoke(this, EventArgs.Empty);

    private void OnScreenReaderStateChanged(object? sender, EventArgs e)
    {
        ResetModeIfButtonHidden();
        OnPropertyChanged(nameof(NeedsCellOverlay));
        OnPropertyChanged(nameof(ShowModeButton));

        // The summary's "cannot be reached" caveat depends on whether the overlay exists.
        OnPropertyChanged(nameof(BoardDescription));

        OverlayNeedChanged?.Invoke(this, EventArgs.Empty);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            SuspendClock();
            _saves.Changed -= OnPersistenceChanged;
            _completions.Changed -= OnPersistenceChanged;
            _theme.Changed -= OnThemeChanged;
            _accessibility.ScreenReaderStateChanged -= OnScreenReaderStateChanged;
        }

        base.Dispose(disposing);
    }
}
