using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Squarebuzz.App.Services;
using Squarebuzz.Core.Abstractions;
using Squarebuzz.Core.Model;
using Squarebuzz.Core.Solving;
using Squarebuzz.Data;

namespace Squarebuzz.App.ViewModels;

/// <summary>
/// Scaffolding screen for the structural milestones. It proves the composition root works end
/// to end - DI, Shell hosting, runtime theming, the domain, and SQLite - on the real device,
/// and is replaced by the splash screen when the screens land.
/// </summary>
public partial class StartupViewModel : ViewModelBase
{
    private readonly IThemeService _themeService;
    private readonly IPuzzleRepository _puzzles;
    private readonly GameSessionFactory _sessions;
    private readonly ISettingsRepository _settingsRepository;
    private readonly ISaveGameRepository _saveGames;
    private readonly IProgressRepository _progress;
    private readonly SquarebuzzDatabase _database;

    private GameSettings _settings = GameSettings.Default;

    public StartupViewModel(
        IThemeService themeService,
        IPuzzleRepository puzzles,
        GameSessionFactory sessions,
        ISettingsRepository settingsRepository,
        ISaveGameRepository saveGames,
        IProgressRepository progress,
        SquarebuzzDatabase database)
    {
        _themeService = themeService;
        _puzzles = puzzles;
        _sessions = sessions;
        _settingsRepository = settingsRepository;
        _saveGames = saveGames;
        _progress = progress;
        _database = database;

        Title = "squarebuzz";
        RefreshTheme();
    }

    [ObservableProperty]
    public partial string ThemeName { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string AccentName { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string ContentSummary { get; private set; } = "…";

    [ObservableProperty]
    public partial string SolverSummary { get; private set; } = "…";

    [ObservableProperty]
    public partial string GeneratorSummary { get; private set; } = "…";

    [ObservableProperty]
    public partial string StorageSummary { get; private set; } = "…";

    public override async Task OnAppearingAsync()
    {
        // The persisted theme is applied here rather than in the App constructor because
        // loading it is asynchronous. Once the real splash screen exists it covers this
        // moment; on this scaffolding page a brief default-themed frame is acceptable.
        await LoadAndApplySettingsAsync();

        RunDomainSelfCheck();
        await RunStorageSelfCheckAsync();
    }

    private async Task LoadAndApplySettingsAsync()
    {
        try
        {
            _settings = await _settingsRepository.LoadAsync();
            _themeService.Apply(_settings.Theme, _settings.Accent);
            RefreshTheme();
        }
        catch (Exception ex)
        {
            StorageSummary = $"settings load FAILED: {ex.GetType().Name}: {ex.Message}";
        }
    }

    /// <summary>
    /// Exercises the parts of the domain that can only really fail on a device: loading the
    /// embedded content through source-generated JSON (which the mobile trimmer could strip),
    /// and running the solver and generator on the target CPU.
    /// </summary>
    private void RunDomainSelfCheck()
    {
        try
        {
            ContentSummary = $"{_puzzles.Puzzles.Count} pictures, {_puzzles.Packs.Count} packs loaded";

            var authored = _puzzles.Puzzles.Count(p => PuzzleSolver.Analyse(p).IsSolvable);
            SolverSummary = $"{authored}/{_puzzles.Puzzles.Count} authored puzzles solvable by logic";

            var stopwatch = Stopwatch.StartNew();
            var session = _sessions.Create(NewGameOptions.Default with
            {
                Size = GridSize.Big,
                PackId = "surprise",
                Seed = 20260729,
            });
            var result = PuzzleSolver.Analyse(session.Puzzle);
            stopwatch.Stop();

            GeneratorSummary =
                $"generated {session.Puzzle.Width}x{session.Puzzle.Height}: {result.Outcome}, " +
                $"{result.Passes} passes, {stopwatch.ElapsedMilliseconds} ms";
        }
        catch (Exception ex)
        {
            // Surfaced on screen rather than swallowed: a trimmer or resource problem must be
            // impossible to miss during a device run.
            ContentSummary = $"FAILED: {ex.GetType().Name}";
            SolverSummary = ex.Message;
            GeneratorSummary = string.Empty;
        }
    }

    /// <summary>
    /// Proves SQLite works on this platform: the native library loaded, the file opened in the
    /// app's private storage, migrations ran, and a save round-trips.
    /// </summary>
    private async Task RunStorageSelfCheckAsync()
    {
        try
        {
            var schemaVersion = await _database.GetSchemaVersionAsync();
            var saveCount = await _saveGames.CountAsync();
            var progress = await _progress.GetProgressAsync();

            StorageSummary =
                $"SQLite schema v{schemaVersion}, journal={_database.JournalMode}, " +
                $"{saveCount} saves, {progress.Stars} stars";
        }
        catch (Exception ex)
        {
            StorageSummary = $"SQLite FAILED: {ex.GetType().Name}: {ex.Message}";
        }
    }

    [RelayCommand]
    private static async Task PlayAsync() => await Shell.Current.GoToAsync(Routes.Game);

    [RelayCommand]
    private async Task CycleThemeAsync()
    {
        var next = _themeService.Theme switch
        {
            GameTheme.Light => GameTheme.Dark,
            GameTheme.Dark => GameTheme.ColorBlind,
            _ => GameTheme.Light,
        };

        _themeService.Apply(next, _themeService.Accent);
        RefreshTheme();

        await PersistThemeAsync();
    }

    [RelayCommand]
    private async Task CycleAccentAsync()
    {
        var next = _themeService.Accent switch
        {
            GameAccent.Tangerine => GameAccent.Grape,
            GameAccent.Grape => GameAccent.Trio,
            _ => GameAccent.Tangerine,
        };

        _themeService.Apply(_themeService.Theme, next);
        RefreshTheme();

        await PersistThemeAsync();
    }

    private async Task PersistThemeAsync()
    {
        try
        {
            _settings = _settings with { Theme = _themeService.Theme, Accent = _themeService.Accent };
            await _settingsRepository.SaveAsync(_settings);

            await RunStorageSelfCheckAsync();
        }
        catch (Exception ex)
        {
            StorageSummary = $"settings save FAILED: {ex.GetType().Name}: {ex.Message}";
        }
    }

    private void RefreshTheme()
    {
        ThemeName = _themeService.Theme.ToString();
        AccentName = _themeService.Accent.ToString();
    }
}
