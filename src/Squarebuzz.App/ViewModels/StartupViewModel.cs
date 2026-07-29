using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Squarebuzz.App.Services;
using Squarebuzz.Core.Abstractions;
using Squarebuzz.Core.Model;
using Squarebuzz.Core.Solving;

namespace Squarebuzz.App.ViewModels;

/// <summary>
/// Scaffolding screen for the structural milestone. It proves the composition root works end
/// to end - DI, Shell hosting, runtime theme swapping, and that the domain runs correctly on
/// the real device - and is replaced by the splash screen when the screens land.
/// </summary>
public partial class StartupViewModel : ViewModelBase
{
    private readonly IThemeService _themeService;
    private readonly IPuzzleRepository _puzzles;
    private readonly GameSessionFactory _sessions;

    public StartupViewModel(
        IThemeService themeService,
        IPuzzleRepository puzzles,
        GameSessionFactory sessions)
    {
        _themeService = themeService;
        _puzzles = puzzles;
        _sessions = sessions;

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

    public override Task OnAppearingAsync()
    {
        RunDomainSelfCheck();
        return Task.CompletedTask;
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

    [RelayCommand]
    private void CycleTheme()
    {
        var next = _themeService.Theme switch
        {
            GameTheme.Light => GameTheme.Dark,
            GameTheme.Dark => GameTheme.ColorBlind,
            _ => GameTheme.Light,
        };

        _themeService.Apply(next, _themeService.Accent);
        RefreshTheme();
    }

    [RelayCommand]
    private void CycleAccent()
    {
        var next = _themeService.Accent switch
        {
            GameAccent.Tangerine => GameAccent.Grape,
            GameAccent.Grape => GameAccent.Trio,
            _ => GameAccent.Tangerine,
        };

        _themeService.Apply(_themeService.Theme, next);
        RefreshTheme();
    }

    private void RefreshTheme()
    {
        ThemeName = _themeService.Theme.ToString();
        AccentName = _themeService.Accent.ToString();
    }
}
