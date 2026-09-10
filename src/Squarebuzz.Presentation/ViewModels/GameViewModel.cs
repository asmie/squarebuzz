using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Squarebuzz.Presentation.Navigation;
using Squarebuzz.Presentation.Services;
using Squarebuzz.Core.Abstractions;
using Squarebuzz.Core.Generation;
using Squarebuzz.Core.Model;
using Squarebuzz.Core.Progression;

namespace Squarebuzz.Presentation.ViewModels;

/// <summary>
/// Drives the board screen: mode toggle, undo/redo, hints, the timer, pausing and completion.
/// </summary>
/// <remarks>
/// Holds the <see cref="GameSession"/> but never reimplements its rules - every move goes
/// through the session so the domain stays the single source of truth for what is legal.
/// Pause and completion are overlays on this screen rather than separate routes, so the session
/// never has to be serialised across a navigation just to show a summary over the board.
/// </remarks>
/// <remarks>
/// Route parameters arrive through <see cref="ApplyQueryAttributes"/>. The MAUI head's
/// <c>GamePage</c> implements Shell's <c>IQueryAttributable</c> and forwards here, so this
/// assembly stays MAUI-free while Shell navigation keeps working.
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

    /// <summary>
    /// How often play is written to disk. Frequent enough that a crash or a task-kill costs
    /// only a few moves, rare enough that it never competes with drawing.
    /// </summary>
    private static readonly TimeSpan AutosaveEvery = TimeSpan.FromSeconds(15);

    private readonly GameSessionFactory _sessions;
    private readonly ISettingsRepository _settingsRepository;
    private readonly IProgressRepository _progress;
    private readonly ISaveGameRepository _saveGames;
    private readonly GameCompletionService _completions;
    private readonly IPuzzleRepository _puzzles;
    private readonly INavigationService _navigation;
    private readonly IClock _clock;
    private readonly IScreenTimeMonitor _screenTime;
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

    /// <summary>Identity of this game's row in the save table, so autosaves replace rather than pile up.</summary>
    private Guid _saveId = Guid.NewGuid();

    private Guid? _pendingResumeId;
    private string? _pendingPuzzleId;
    private bool _isDaily;
    private TimedTier? _pendingTier;
    private int? _pendingLevel;

    /// <summary>Play time since the last write, so the periodic autosave fires on the clock rather than on a tick count.</summary>
    private TimeSpan _sinceAutosave;

    /// <summary>
    /// <see cref="IClock.Monotonic"/> as of the last tick that counted, so the next tick can measure
    /// how much time really passed rather than assuming its nominal interval did.
    /// </summary>
    private TimeSpan _lastTickAt;

    /// <summary>The exact snapshot last written for the current session.</summary>
    private SavedGame? _lastSavedGame;

    /// <summary>True once a save has been loaded or requested, even if its board is now empty.</summary>
    private bool _hasSave;

    /// <summary>Orders saves and completion persistence, including across a restart.</summary>
    private Task _pendingSave = Task.CompletedTask;

    /// <summary>Identifies the most recent toast, so only its own dismissal takes effect.</summary>
    private int _toastToken;

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
        INarrationService narration,
        IAccessibilityState accessibility,
        IThemeService theme,
        IUiThread uiThread,
        IGameTimerFactory timers,
        IScreenReader screenReader,
        GameCompletionService completions)
        : base(strings)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentNullException.ThrowIfNull(settingsRepository);
        ArgumentNullException.ThrowIfNull(progress);
        ArgumentNullException.ThrowIfNull(saveGames);
        ArgumentNullException.ThrowIfNull(puzzles);
        ArgumentNullException.ThrowIfNull(navigation);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(screenTime);
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
        _saveGames = saveGames;
        _puzzles = puzzles;
        _navigation = navigation;
        _clock = clock;
        _screenTime = screenTime;
        _audio = audio;
        _narration = narration;
        _accessibility = accessibility;
        _theme = theme;
        _uiThread = uiThread;
        _timers = timers;
        _screenReader = screenReader;
        _completions = completions;

        // The board canvas snapshots its palette when it draws, so a theme change mid-game -
        // OS dusk flip under Auto, or Options changed from the pause overlay one day - must
        // push a redraw. Unhooked in Dispose, which PageLifecycle guarantees is called.
        _theme.Changed += OnThemeChanged;

        // TalkBack switched on mid-game must grow the cell overlay right away - the player who
        // just turned it on is exactly the one who cannot see that the board ignored them.
        _accessibility.ScreenReaderStateChanged += OnScreenReaderStateChanged;
    }

    /// <summary>Raised when the board data changed and the canvas needs redrawing.</summary>
    public event EventHandler? BoardChanged;

    /// <summary>Raised when the palette changed, so the canvas re-reads its colours.</summary>
    public event EventHandler? PaletteChanged;

    /// <summary>Raised when <see cref="NeedsCellOverlay"/> changed, so the page rebuilds it.</summary>
    public event EventHandler? OverlayNeedChanged;

    /// <summary>Raised with a cell index when a fill was wrong, so the view can flash it.</summary>
    public event EventHandler<int>? MistakeMade;

    /// <summary>Raised with a cell index when a fill landed, so the view can pop it.</summary>
    public event EventHandler<int>? CellFilled;

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

    /// <remarks>
    /// Raises <see cref="SolvedTimeText"/> as well, which is what makes the win overlay's time
    /// tile show the solve rather than the "0:00" the binding read when the page was built - see
    /// that property.
    /// </remarks>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SolvedTimeText))]
    public partial bool IsSolved { get; private set; }

    /// <summary>
    /// The clock beat the player. A separate flag from <see cref="IsSolved"/> because it is the
    /// game's only loss, and the two overlays say opposite things.
    /// </summary>
    [ObservableProperty]
    public partial bool IsTimeUp { get; private set; }

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

    /// <summary>Hints spent this game, for the win overlay's tiles.</summary>
    [ObservableProperty]
    public partial int HintsUsed { get; private set; }

    /// <summary>Fraction of the picture filled in, 0..1, for the status bar's ring.</summary>
    [ObservableProperty]
    public partial double Progress { get; private set; }

    /// <remarks>
    /// Both star properties, not just the visible one: the description is bound to the same label
    /// and goes stale exactly as <see cref="SolvedTimeText"/> did, so a screen reader was told
    /// "0 of 3 stars" over a row of three filled ones.
    /// </remarks>
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

    [ObservableProperty]
    public partial string PuzzleName { get; private set; } = string.Empty;

    public bool HasToast => !string.IsNullOrEmpty(Toast);

    /// <summary>
    /// The clock is a preference in an ordinary game but the whole point of a timed trial, so a
    /// trial overrides the switch: a player who hid the timer in Options must still see the
    /// countdown they are racing, or the first they learn of it is the "out of time" screen.
    /// </summary>
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
    public int UndoColumn => ColumnFor(UndoOrder);

    public int RedoColumn => ColumnFor(RedoOrder);

    public int HintColumn => ColumnFor(HintOrder);

    public int RestartColumn => ColumnFor(RestartOrder);

    public int UndoRow => RowFor(UndoOrder);

    public int RedoRow => RowFor(RedoOrder);

    public int HintRow => RowFor(HintOrder);

    public int RestartRow => RowFor(RestartOrder);

    /// <summary>
    /// True on a wide landscape screen, where the play column becomes a row.
    /// </summary>
    /// <remarks>
    /// Set by the page from its own measured size, because MAUI has no media queries. The design
    /// doc's scaling note is the source of the rule: "Above 900 px in landscape the play column
    /// becomes a row", with cell size computed from the free rectangle rather than hard-coded.
    /// </remarks>
    [ObservableProperty]
    [NotifyPropertyChangedFor(
        nameof(UndoColumn), nameof(RedoColumn), nameof(HintColumn), nameof(RestartColumn),
        nameof(UndoRow), nameof(RedoRow), nameof(HintRow), nameof(RestartRow))]
    public partial bool IsWideLayout { get; set; }

    /// <summary>
    /// Which side the controls column takes in the wide layout - the hand's side, so the buttons
    /// are under the thumb that is already holding that edge of the tablet.
    /// </summary>
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

    /// <summary>
    /// Label for the mode toggle. It names the mode the button switches <em>to</em>, which is
    /// the convention children read correctly - "Mark X" means "tapping will now mark X".
    /// </summary>
    public string ModeButtonText => IsCrossMode ? T("fill") : T("cross");

    /// <summary>
    /// Whether the mode toggle belongs on screen at all.
    /// </summary>
    /// <remarks>
    /// Only in <see cref="TapBehaviour.ModeButton"/>, which is the mode named after it. Under
    /// hold-to-cross the gesture already chooses the mark, so the button is a second, redundant
    /// way to say the same thing - and two input models on one board is how a child ends up
    /// crossing when they meant to fill.
    /// </remarks>
    public bool ShowModeButton => _settings.TapBehaviour == TapBehaviour.ModeButton;

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

    public string OptionsText => T("options");

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

    /// <summary>
    /// The solve time for the win overlay. Not <see cref="ElapsedText"/>: that shows what is
    /// *left* during a timed trial, and a trial's win screen should still report how long the
    /// solve took, not how much clock remained.
    /// </summary>
    /// <remarks>
    /// Computed, so it only reaches the screen when something raises it - <see cref="IsSolved"/>
    /// does. The overlay is in the visual tree from the start, merely hidden, so its binding is
    /// evaluated once when the page is built: before <see cref="Session"/> is loaded, which read
    /// "0:00" and then never changed, however long the puzzle took. The same staleness would show
    /// the *previous* puzzle's time on a second win in one sitting.
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

    /// <summary>
    /// What a screen reader is told about the board.
    /// </summary>
    /// <remarks>
    /// A <c>GraphicsView</c> contributes nothing to the accessibility tree - the board is simply
    /// absent from it - so this summary is what a screen reader has to go on for the board as a
    /// whole. When a screen reader is running, the individual squares are reachable through the
    /// overlay built by <c>GamePage.BuildCellOverlay</c>; when it is not, the summary says outright
    /// that they are not, because promising a playable board and providing no way to reach a square
    /// would be worse than admitting the limit.
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

            // The caveat is only true when there is no cell overlay. Leaving it in once the squares
            // became reachable would be a description that contradicts the screen it describes.
            return NeedsCellOverlay ? summary : $"{summary} {T("a11yBoardNote")}";
        }
    }

    /// <summary>
    /// What a screen reader says about one square: where it is, what is in it, and the two clues
    /// that govern it.
    /// </summary>
    /// <remarks>
    /// The clues are repeated on every square, which is verbose - but a player who cannot see the
    /// gutters has no other way to know them, and asking a child to hold twenty numbers in their
    /// head is not an alternative. The tidier design would be separate focusable headers per row
    /// and column, announcing clues only when the focus crosses into a new line; that needs
    /// control over focus order, which MAUI does not offer.
    /// </remarks>
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

    /// <summary>
    /// The timer as a sentence; "5:26" alone is read as a pair of numbers.
    /// </summary>
    /// <remarks>
    /// A trial's clock counts <em>down</em> - <see cref="UpdateElapsedText"/> shows what is left,
    /// not what has passed - so it needs its own wording. Both cases shared "Time so far", which
    /// told a screen-reader player the exact opposite of what the number meant, on the one screen
    /// where the number is the whole game. Nothing visual gives that away: the sighted player sees
    /// it counting down.
    /// </remarks>
    public string ElapsedDescription => Strings.Format(
        Session is { IsTimed: true } ? "a11yTimeLeft" : "a11yTime",
        ElapsedText);

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

        if (_pendingTier is { } tier)
        {
            _settings = await LoadSettingsSafelyAsync();

            await StartAsync(tier.ToOptions(_settings.Helpers));
            return;
        }

        if (_pendingLevel is { } level)
        {
            _pendingLevel = null;

            // The catalog decides everything about a level; the settings only lend the
            // player's helper preferences.
            _settings = await LoadSettingsSafelyAsync();
            await StartAsync(LevelCatalog.Get(level, _puzzles.Puzzles).ToOptions(_settings.Helpers));
            return;
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

    /// <summary>
    /// Re-reads the settings after another screen may have changed them - the pause overlay
    /// links to Options, and the player expects a new cell size or handedness to apply the
    /// moment they come back to the board.
    /// </summary>
    public async Task RefreshSettingsAsync()
    {
        if (Session is not { } session)
        {
            // InitialiseAsync loads settings itself; refreshing before it runs is wasted I/O.
            return;
        }

        _settings = await LoadSettingsSafelyAsync();

        // The rules too, not just the view-level preferences: a session resolves its rules at
        // creation, so without this a helper flipped from the pause overlay's Options - the
        // auto-cross switch, say - would quietly do nothing until the next puzzle.
        session.ApplyHelpers(_settings.Helpers);

        // Switching to hold-to-cross takes the mode button off the screen, so a session left in
        // cross mode would keep crossing with nothing left to change it back. The gesture decides
        // the mark in that mode, and its plain tap fills.
        if (!ShowModeButton && IsCrossMode)
        {
            IsCrossMode = false;
            session.Mode = PaintMode.Fill;
        }

        SyncFromSession();
        NotifySettingsDependentProperties();
    }

    /// <summary>
    /// The packs the player has earned, for the wildcard "surprise" pack to draw from.
    /// </summary>
    /// <remarks>
    /// Only fetched when a wildcard is actually in play - every other pack names itself, and the
    /// solved table is of no use in choosing from it. That keeps the extra read off the ordinary
    /// start path rather than paying for it on every new game.
    /// </remarks>
    private async Task<IReadOnlySet<string>?> LoadUnlockedPacksAsync(NewGameOptions options)
    {
        var isWildcard = _puzzles.Packs.Any(p =>
            p.IsWildcard && string.Equals(p.Id, options.PackId, StringComparison.Ordinal));

        if (!isWildcard)
        {
            return null;
        }

        try
        {
            var solved = await _progress.GetSolvedPuzzlesAsync();

            return PackUnlocks.UnlockedPackIds(
                _puzzles.Packs,
                _puzzles.Puzzles,
                [.. solved.Select(s => s.PuzzleId)]);
        }
        catch (Exception)
        {
            // Unreadable progress must not stop a game starting; the shipped locks still apply.
            return null;
        }
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

            _lastSavedGame = save;
            _hasSave = true;
            _sinceAutosave = TimeSpan.Zero;
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

        PuzzleName = NameForPuzzle();

        SyncFromSession();
        UpdateElapsedText();
        StopTimer();
        StartTimer();
        NotifySettingsDependentProperties();
        NotifyLevelDependentProperties();

        BoardChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>
    /// Writes the game in progress to the save table. Called on pause, on quit and on leaving the
    /// screen, plus periodically while playing.
    /// </summary>
    public Task AutosaveAsync() => QueueAutosaveAsync(onlyIfChanged: false);

    private Task QueueAutosaveAsync(bool onlyIfChanged)
    {
        if (Session is not { } session || session.IsOver || session.IsTimed)
        {
            return Task.CompletedTask;
        }

        if (!_hasSave && session.Mistakes == 0 && session.HintsUsed == 0
            && !session.Cells.ContainsAnyExcept(CellState.Empty))
        {
            // Keep untouched games out of Continue, but preserve counters and replace an
            // existing (or pending) save when the player undoes its board back to empty.
            return Task.CompletedTask;
        }

        var snapshot = SavedGame.FromSession(session, _saveId, _clock.Now);
        _hasSave = true;
        _sinceAutosave = TimeSpan.Zero;
        _pendingSave = SaveAfterAsync(_pendingSave, session, snapshot, onlyIfChanged);
        return _pendingSave;
    }

    private async Task SaveAfterAsync(Task previous, GameSession session, SavedGame snapshot, bool onlyIfChanged)
    {
        // Each operation catches storage failures, so a failed write cannot break the queue.
        await previous;

        if (session.IsOver || (onlyIfChanged && HasSameProgress(snapshot, _lastSavedGame)))
        {
            return;
        }

        try
        {
            await _saveGames.SaveAsync(snapshot);

            // The player may have changed the board or started another game during I/O.
            // Acknowledge only what this operation wrote, and only for its own session.
            if (ReferenceEquals(Session, session) && _saveId == snapshot.Id)
            {
                _lastSavedGame = snapshot;
            }
        }
        catch (Exception)
        {
            // Keep the previous snapshot so the next periodic save retries the changed state.
        }
    }

    /// <summary>
    /// Compares persisted progress, not undo depth. Elapsed time alone is saved at explicit
    /// lifecycle save points rather than rewriting an idle board every fifteen seconds.
    /// </summary>
    private static bool HasSameProgress(SavedGame snapshot, SavedGame? saved) =>
        saved is not null
        && snapshot.Id == saved.Id
        && snapshot.Mistakes == saved.Mistakes
        && snapshot.HintsUsed == saved.HintsUsed
        && snapshot.HintsRemaining == saved.HintsRemaining
        && snapshot.Cells.SequenceEqual(saved.Cells);

    private Task AutosaveIfBoardChangedAsync() => QueueAutosaveAsync(onlyIfChanged: true);

    /// <summary>Starts a new puzzle from the player's saved preferences.</summary>
    public async Task StartAsync(NewGameOptions? options = null)
    {
        _settings = await LoadSettingsSafelyAsync();

        // A null seed means "surprise me", so replaying gives a different picture rather than
        // the same one over and over.
        var effective = (options ?? _settings.ToNewGameOptions()) with { Helpers = _settings.Helpers };

        try
        {
            Session = _sessions.Create(effective, await LoadUnlockedPacksAsync(effective));
        }
        catch (PuzzleGenerationException)
        {
            // The generator has already retried internally and given up, so trying again here
            // would not help. This path should be unreachable in practice, but reaching it must
            // not take the app down: StartAsync is called from async void page lifecycle, where
            // an escaped exception has no handler at all. Home with an apology beats a crash.
            var sorry = T("genFailed");

            ShowToast(sorry);
            _narration.Speak(sorry);
            Announce(sorry);

            await _navigation.ResetToAsync(Routes.Menu);
            return;
        }

        // A new game is a new row: restarting must not overwrite the save it came from.
        _saveId = Guid.NewGuid();
        _sinceAutosave = TimeSpan.Zero;
        _lastSavedGame = null;
        _hasSave = false;

        IsCrossMode = false;
        Session.Mode = PaintMode.Fill;
        IsSolved = false;
        IsTimeUp = false;
        IsPaused = false;
        IsBreakReminderOpen = false;
        Toast = string.Empty;

        PuzzleName = NameForPuzzle();

        SyncFromSession();
        UpdateElapsedText();
        StopTimer();
        StartTimer();

        NotifySettingsDependentProperties();
        NotifyLevelDependentProperties();

        BoardChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// What the header calls this game. A campaign level is its number; a milestone level keeps
    /// the picture's name, because the reveal is the reward. Everything else is as before.
    /// </summary>
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

    /// <summary>
    /// The win overlay's texts change shape in level mode, and the overlay is built with the
    /// page - so every new session must push them, exactly like the settings-dependent set.
    /// </summary>
    private void NotifyLevelDependentProperties()
    {
        OnPropertyChanged(nameof(SolvedTitle));
        OnPropertyChanged(nameof(NextPuzzleText));
        OnPropertyChanged(nameof(ShowNextButton));
        OnPropertyChanged(nameof(AllLevelsDoneText));
        OnPropertyChanged(nameof(IsCampaignComplete));
    }

    /// <summary>
    /// True while an overlay is up: the pause screen, or the break reminder.
    /// </summary>
    /// <remarks>
    /// One definition for every route onto the board - painting, tapping, hints, undo and redo -
    /// so they cannot disagree about what "the game is not being played right now" means. They
    /// did: the hint and undo buttons checked only for pause, so a child could keep spending
    /// hints behind the break reminder, which is documented as stopping play.
    /// </remarks>
    private bool IsInputBlocked => IsPaused || IsBreakReminderOpen;

    /// <summary>Applies a paint request from the board view.</summary>
    public void Paint(int index, CellState target)
    {
        if (Session is not { } session || IsSolved || IsInputBlocked)
        {
            return;
        }

        Apply(index, session.Paint(index, target), target);
    }

    /// <summary>
    /// Applies a plain tap, letting the session decide what the mark becomes from the current mode.
    /// </summary>
    /// <remarks>
    /// The accessibility overlay's route in. It cannot compute a target the way the board view does,
    /// because that is worked out from where a finger went down and which way it dragged - a
    /// gesture a screen-reader user is not making.
    /// </remarks>
    public void TapCell(int index)
    {
        if (Session is not { } session || IsSolved || IsInputBlocked)
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

        if (outcome.Result == MoveResult.Applied && target == CellState.Filled)
        {
            CellFilled?.Invoke(this, index);
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
        if (Session is not { } session || IsInputBlocked)
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

        // Sync before announcing the hint: the page's handler scrolls to the cell and draws the
        // ring, and it must see the board with the hinted mark already painted - raising the
        // event first left the handler working against a stale snapshot.
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
        if (_pendingTier is { } tier)
        {
            // A new picture and a full clock. Handing back the same grid would let a player learn
            // it and "beat" the trial by memory rather than by reading the clues.
            return tier.ToOptions(_settings.Helpers);
        }

        if (_isDaily)
        {
            return DailyPuzzle.OptionsFor(_clock.Today, _settings.Helpers);
        }

        // Written out rather than as `Session?.Origin with { ... }`, which compiles but
        // dereferences a possibly-null value and would throw once Origin was ever null.
        var origin = Session?.Origin;

        if (origin is null)
        {
            return null;
        }

        // A restart of a campaign level replays that exact level: its seed and picture are in
        // the origin already, and deterministic is the whole point of a level.
        if (origin.Level is not null)
        {
            return origin;
        }

        return origin with
        {
            Seed = null,

            // A picture chosen from the Gallery must not stick to every following "Next" - and
            // the picture just solved must not come straight back either.
            PuzzleId = null,
            ExcludePuzzleId = Session is { Puzzle.IsGenerated: false } current ? current.Puzzle.Id : null,
        };
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

        // Everything the completion record needs, captured before the first await. The moment
        // control yields, a quick tap on "Next" can replace the session, regenerate the save id
        // and clear the daily flag - and this completion must be attributed to the game that was
        // just won, not to the one that follows it. (A level recorded one-too-high would unlock
        // a level that was never played.)
        var saveId = _saveId;
        var isDaily = _isDaily;
        var level = CurrentLevel;
        var completedAt = _clock.Now;

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

        var completion = new PuzzleCompletion(
            session.Puzzle.IsGenerated ? null : session.Puzzle.Id,
            session.StarRating,
            session.Elapsed,
            session.Puzzle.PictureCellCount,
            session.HintsUsed,
            completedAt)
        {
            Size = session.Puzzle.Width,
            PackId = session.Puzzle.Pack,
            Mistakes = session.Mistakes,
            IsDaily = isDaily,
            Level = level,
        };

        // Finish older saves first. Completion owns save removal and every earned update in
        // one transaction; the app-lifetime service retains failures after this page is gone.
        _pendingSave = _completions.CompleteAsync(saveId, completion, _pendingSave);
        await _pendingSave;
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

        // Both axes, and the side the controls sit on. Handedness moves a button's row as well
        // as its column in the wide tablet layout, so raising only the columns left the buttons
        // half-rearranged after a change made from the pause overlay's Options.
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
        HintsUsed = session.HintsUsed;

        var pictureSize = session.Puzzle.PictureCellCount;
        Progress = pictureSize == 0 ? 0 : (double)session.FilledCount / pictureSize;

        Mistakes = session.Mistakes;
        StarRating = session.StarRating;
        CanUndo = session.CanUndo;
        CanRedo = session.CanRedo;
        CanUseHint = session.HintsRemaining > 0 && !session.IsOver;
        IsSolved = session.IsSolved;

        // The board's accessible description carries the filled count, so it goes stale on every
        // move unless it is raised here - and "2 of 17" while the board is nearly finished is
        // worse than no description at all. Nothing on screen shows this, so only a dump of the
        // accessibility tree catches it.
        OnPropertyChanged(nameof(BoardDescription));
    }

    /// <summary>
    /// Suspends timing while the page is covered or the window is in the background.
    /// The suspension also applies to a session whose initialization has not finished yet.
    /// </summary>
    public void SuspendClock()
    {
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
        _lastTickAt = _clock.Monotonic;

        _timer.Start();
    }

    /// <summary>
    /// Advances the game by the time that really passed, not by the timer's nominal second.
    /// </summary>
    /// <remarks>
    /// A dispatcher timer is a request, not a guarantee: under load the UI thread delivers its
    /// ticks late or drops them outright, and counting each one as a second made the game clock
    /// run slow by exactly the amount the device was struggling. In a timed trial that quietly
    /// handed the player extra real time. Reading a monotonic clock on every tick charges the
    /// true interval however unevenly the ticks arrive.
    /// </remarks>
    private void OnTimerTick(object? sender, EventArgs e)
    {
        var now = _clock.Monotonic;
        var delta = now - _lastTickAt;
        _lastTickAt = now;

        // Moving the reference forward in the guarded cases too is what keeps the break reminder
        // from charging its own duration to the game the moment it is dismissed: the timer keeps
        // running behind the overlay, and without this the first tick after "A little longer"
        // would measure the whole time the child spent reading it.
        if (Session is not { } session || session.IsOver || IsPaused || IsBreakReminderOpen)
        {
            return;
        }

        // Monotonic cannot go backwards, but a zero-length interval is possible if two ticks are
        // delivered together after a stall, and there is nothing to charge for it.
        if (delta <= TimeSpan.Zero)
        {
            return;
        }

        session.Advance(delta);
        UpdateElapsedText();

        if (session.IsTimeUp)
        {
            HandleTimeUp();
            return;
        }

        // Counted here rather than in the monitor's own timer so that only time actually spent
        // playing counts - the guards above are exactly the cases that should not.
        if (_screenTime.Add(delta))
        {
            IsBreakReminderOpen = true;

            var reminder = $"{BreakTitle} {BreakBody}";

            _narration.Speak(reminder);
            Announce(reminder);

            // Nothing about a break should risk the board, so this is a save point too.
            _ = AutosaveAsync();
        }

        _sinceAutosave += delta;

        if (_sinceAutosave >= AutosaveEvery)
        {
            _ = AutosaveIfBoardChangedAsync();
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
        // A trial shows what is left rather than what has passed: the number that matters is the
        // one running out. Same m:ss format either way, matching the prototype's fmtTime.
        var shown = Session is { IsTimed: true } timed ? timed.Remaining : Session?.Elapsed ?? TimeSpan.Zero;

        ElapsedText = $"{(int)shown.TotalMinutes}:{shown.Seconds:00}";
    }

    /// <summary>
    /// Ends a trial the player did not finish in time.
    /// </summary>
    /// <remarks>
    /// No progress is recorded and no save is written: a trial that ran out produced no picture, so
    /// there is nothing to put in the Gallery and nothing to come back to. Losing is meant to cost
    /// the attempt, not the afternoon - the overlay offers another go straight away.
    /// </remarks>
    private void HandleTimeUp()
    {
        StopTimer();

        IsTimeUp = true;

        var message = $"{TimeUpTitle} {TimeUpBody}";

        _narration.Speak(message);
        Announce(message);
    }

    /// <summary>
    /// Sends <paramref name="text"/> to the platform screen reader.
    /// </summary>
    /// <remarks>
    /// A no-op when no screen reader is running, so this is safe to call unconditionally - and
    /// unlike narration it is deliberately not tied to the Voice narration setting, because the
    /// player's screen reader is their choice rather than ours to switch off.
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
        // Already on screen means the player is repeating something - a drag over a row of wrong
        // squares raises "Oops" once per square. Saying it ten times is not ten times as helpful;
        // it talks over itself, and for a screen-reader user it buries everything else. The
        // message still stays up, and its dismissal is pushed back below.
        var isRepeat = Toast == message;

        Toast = message;

        if (!isRepeat)
        {
            // Every transient message in the game goes through here, so narrating it once at the
            // funnel covers "line done", "oops" and "hint used" without three separate calls that
            // a fourth message could later be added alongside and forget.
            _narration.Speak(message);

            // And the same message to whatever screen reader the player is using. A toast that
            // appears and fades is invisible to one otherwise: nothing takes focus, so nothing is
            // read. Announce is a no-op when no screen reader is running.
            Announce(message);
        }

        // Clears itself, so no screen has to remember to tidy up after a transient message. The
        // token means only the *latest* showing clears it: repeats each scheduled their own
        // dismissal, and the earliest would fire first and cut a message that had just been
        // renewed down to a fraction of its time on screen.
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

    private void OnThemeChanged(object? sender, EventArgs e) =>
        PaletteChanged?.Invoke(this, EventArgs.Empty);

    private void OnScreenReaderStateChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(NeedsCellOverlay));

        // The summary's "cannot be reached" caveat depends on whether the overlay exists.
        OnPropertyChanged(nameof(BoardDescription));

        OverlayNeedChanged?.Invoke(this, EventArgs.Empty);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            SuspendClock();
            _theme.Changed -= OnThemeChanged;
            _accessibility.ScreenReaderStateChanged -= OnScreenReaderStateChanged;
        }

        base.Dispose(disposing);
    }
}
