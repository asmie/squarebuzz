using Squarebuzz.Core.Abstractions;
using Squarebuzz.Core.Model;
using Squarebuzz.Presentation.Tests.Fakes;
using Squarebuzz.Presentation.ViewModels;
using Squarebuzz.Presentation.Services;

namespace Squarebuzz.Presentation.Tests;

/// <summary>
/// A <see cref="GameViewModel"/> wired to fakes, plus the fakes themselves so tests can
/// arrange state and inspect what the screen did.
/// </summary>
public sealed class GameViewModelHarness : IDisposable
{
    public GameViewModelHarness(GameSessionFactory? sessions = null, ISettingsRepository? settings = null)
    {
        Sessions = sessions ?? new GameSessionFactory(PuzzleRepository, Generator);
        Completions = new FakeGameCompletionRepository(Progress, SaveGames);
        CompletionService = new GameCompletionService(Completions, Progress);

        Vm = new GameViewModel(
            Sessions,
            settings ?? Settings,
            Progress,
            SaveGames,
            PuzzleRepository,
            Strings,
            Navigation,
            Clock,
            ScreenTime,
            Audio,
            Narration,
            Accessibility,
            Theme,
            UiThread,
            Timers,
            ScreenReader,
            CompletionService);
    }

    public FakeLocalizationService Strings { get; } = new();

    public FakeNavigationService Navigation { get; } = new();

    public FakeClock Clock { get; } = new();

    public FakeSettingsRepository Settings { get; } = new();

    public FakeProgressRepository Progress { get; } = new();

    public FakeSaveGameRepository SaveGames { get; } = new();

    public FakePuzzleRepository PuzzleRepository { get; } = new();

    public FakePuzzleGenerator Generator { get; } = new();

    public FakeScreenTimeMonitor ScreenTime { get; } = new();

    public FakeAudioService Audio { get; } = new();

    public FakeNarrationService Narration { get; } = new();

    public FakeAccessibilityState Accessibility { get; } = new();

    public FakeThemeService Theme { get; } = new();

    public FakeUiThread UiThread { get; } = new();

    public FakeGameTimerFactory Timers { get; } = new();

    public FakeScreenReader ScreenReader { get; } = new();

    public GameSessionFactory Sessions { get; }

    public FakeGameCompletionRepository Completions { get; }

    public GameCompletionService CompletionService { get; }

    public GameViewModel Vm { get; }

    /// <summary>
    /// Solves the fake generator's 2x2 board through the same entry GamePage's OnCellPainted
    /// uses: <see cref="GameViewModel.Paint"/>. Cells 0 and 3 are the picture.
    /// </summary>
    public void SolveCurrentPuzzle()
    {
        Vm.Paint(0, CellState.Filled);
        Vm.Paint(3, CellState.Filled);
    }

    /// <summary>Polls until <paramref name="condition"/> holds, for fire-and-forget completions.</summary>
    public static async Task WaitUntilAsync(Func<bool> condition, int timeoutMs = 2000)
    {
        var deadline = Environment.TickCount64 + timeoutMs;

        while (!condition())
        {
            if (Environment.TickCount64 > deadline)
            {
                throw new TimeoutException("The awaited condition never became true.");
            }

            await Task.Delay(10);
        }
    }

    public void Dispose() => Vm.Dispose();
}
