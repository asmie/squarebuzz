using Squarebuzz.Core.Abstractions;
using Squarebuzz.Core.Generation;
using Squarebuzz.Core.Model;

namespace Squarebuzz.Presentation.Tests.Fakes;

public sealed class FakeSettingsRepository : ISettingsRepository
{
    public GameSettings Settings { get; set; } = GameSettings.Default;

    public List<GameSettings> Saved { get; } = [];

    public Task<GameSettings> LoadAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(Settings);

    public Task SaveAsync(GameSettings settings, CancellationToken cancellationToken = default)
    {
        Settings = settings;
        Saved.Add(settings);
        return Task.CompletedTask;
    }
}

public sealed class FakeProgressRepository : IProgressRepository
{
    public PlayerProgress Progress { get; set; } = PlayerProgress.Empty;

    public List<SolvedPuzzle> Solved { get; } = [];

    public List<EarnedTrophy> Trophies { get; } = [];

    public List<PuzzleCompletion> Completions { get; } = [];

    public List<DateOnly> DailyCompletions { get; } = [];

    /// <summary>When set, <see cref="RecordCompletionAsync"/> stalls until the test releases it.</summary>
    public TaskCompletionSource? RecordGate { get; set; }

    public Task<PlayerProgress> GetProgressAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(Progress);

    public Task SaveProgressAsync(PlayerProgress progress, CancellationToken cancellationToken = default)
    {
        Progress = progress;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<SolvedPuzzle>> GetSolvedPuzzlesAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<SolvedPuzzle>>([.. Solved]);

    public Task<IReadOnlyList<EarnedTrophy>> GetTrophiesAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<EarnedTrophy>>([.. Trophies]);

    public Task AwardTrophyAsync(TrophyId trophy, DateOnly earnedOn, CancellationToken cancellationToken = default)
    {
        Trophies.Add(new EarnedTrophy(trophy, earnedOn));
        return Task.CompletedTask;
    }

    public async Task<PlayerProgress> RecordCompletionAsync(
        PuzzleCompletion completion,
        CancellationToken cancellationToken = default)
    {
        // Captured on arrival, before any stall: the argument is what the game handed over,
        // and that is exactly what the attribution tests pin down.
        Completions.Add(completion);

        if (RecordGate is { } gate)
        {
            await gate.Task;
        }

        if (completion.Level is { } level && level > Progress.HighestLevelCompleted)
        {
            Progress = Progress with { HighestLevelCompleted = level };
        }

        return Progress;
    }

    public Task<IReadOnlyList<DateOnly>> GetDailyCompletionsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<DateOnly>>([.. DailyCompletions]);

    public Task ResetAsync(CancellationToken cancellationToken = default)
    {
        Progress = PlayerProgress.Empty;
        Solved.Clear();
        Trophies.Clear();
        Completions.Clear();
        DailyCompletions.Clear();
        return Task.CompletedTask;
    }
}

public sealed class FakeSaveGameRepository : ISaveGameRepository
{
    public Dictionary<Guid, SavedGame> Saves { get; } = [];

    public List<Guid> Deleted { get; } = [];

    /// <summary>When set, <see cref="DeleteAsync"/> stalls until the test releases it.</summary>
    public TaskCompletionSource? DeleteGate { get; set; }

    public Task<IReadOnlyList<SavedGame>> GetAllAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<SavedGame>>([.. Saves.Values.OrderByDescending(s => s.SavedAt)]);

    public Task<SavedGame?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        Task.FromResult(Saves.GetValueOrDefault(id));

    public Task SaveAsync(SavedGame game, CancellationToken cancellationToken = default)
    {
        Saves[game.Id] = game;
        return Task.CompletedTask;
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        Deleted.Add(id);

        if (DeleteGate is { } gate)
        {
            await gate.Task;
        }

        Saves.Remove(id);
    }

    public Task DeleteAllAsync(CancellationToken cancellationToken = default)
    {
        Saves.Clear();
        return Task.CompletedTask;
    }

    public Task<int> CountAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(Saves.Count);

    public Task<int> PurgeUnrebuildableAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(0);
}

public sealed class FakePuzzleRepository : IPuzzleRepository
{
    public List<PackDefinition> PacksList { get; } = [];

    public List<Puzzle> PuzzlesList { get; } = [];

    public IReadOnlyList<PackDefinition> Packs => PacksList;

    public IReadOnlyList<Puzzle> Puzzles => PuzzlesList;

    public IReadOnlyList<Puzzle> Find(string packId, int size, IReadOnlySet<string>? unlockedPackIds = null) =>
        [.. PuzzlesList.Where(p =>
            p.Width == size
            && p.Height == size
            && string.Equals(p.Pack, packId, StringComparison.Ordinal))];

    public Puzzle? FindById(string puzzleId) =>
        PuzzlesList.FirstOrDefault(p => string.Equals(p.Id, puzzleId, StringComparison.Ordinal));
}

/// <summary>
/// Ignores the requested size and always produces the same tiny 2x2 picture (cells 0 and 3
/// filled), so 'solving' in a test is exactly two paints. The seed is kept in the id so tests
/// can tell one generated board from another.
/// </summary>
public sealed class FakePuzzleGenerator : IPuzzleGenerator
{
    public List<PuzzleRequest> Requests { get; } = [];

    public Puzzle Generate(PuzzleRequest request)
    {
        Requests.Add(request);

        return Puzzle.FromRows(
            $"gen-{request.Seed}",
            request.Pack,
            "#FF8A3D",
            ["#.", ".#"],
            isGenerated: true);
    }
}
