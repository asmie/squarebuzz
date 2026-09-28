using Squarebuzz.Core.Abstractions;
using Squarebuzz.Core.Generation;
using Squarebuzz.Core.Model;

namespace Squarebuzz.Presentation.Tests.Fakes;

public sealed class FakeSettingsRepository : ISettingsRepository
{
    public GameSettings Settings { get; set; } = GameSettings.Default;

    public List<GameSettings> Saved { get; } = [];

    /// <summary>Makes <see cref="LoadAsync"/> throw, as a locked or corrupt database would.</summary>
    public bool LoadFails { get; set; }

    public Task<GameSettings> LoadAsync(CancellationToken cancellationToken = default) => LoadFails
        ? Task.FromException<GameSettings>(new InvalidOperationException("database is locked"))
        : Task.FromResult(Settings);

    /// <summary>Makes <see cref="SaveAsync"/> throw, as a full disk or a locked database would.</summary>
    public bool SaveFails { get; set; }

    /// <summary>When set, holds a settings write until the test releases it.</summary>
    public TaskCompletionSource? SaveGate { get; set; }

    public async Task SaveAsync(GameSettings settings, CancellationToken cancellationToken = default)
    {
        if (SaveGate is { } gate)
        {
            await gate.Task;
        }

        if (SaveFails)
        {
            throw new InvalidOperationException("disk full");
        }

        Settings = settings;
        Saved.Add(settings);
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

    /// <summary>How often progress was wiped - a destructive action a test wants to count exactly.</summary>
    public int ResetCalls { get; private set; }
    public Exception? ResetError { get; set; }

    public Task ResetAsync(CancellationToken cancellationToken = default)
    {
        ResetCalls++;
        if (ResetError is { } error) return Task.FromException(error);
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
    public Exception? LoadError { get; set; }
    public Exception? SaveError { get; set; }
    public Dictionary<Guid, SavedGame> Saves { get; } = [];

    public List<SavedGame> SaveAttempts { get; } = [];

    /// <summary>When set, saves wait before committing their snapshot.</summary>
    public TaskCompletionSource? SaveGate { get; set; }

    public List<Guid> Deleted { get; } = [];

    /// <summary>When set, <see cref="DeleteAsync"/> stalls until the test releases it.</summary>
    public TaskCompletionSource? DeleteGate { get; set; }

    public Task<IReadOnlyList<SavedGame>> GetAllAsync(CancellationToken cancellationToken = default) =>
        LoadError is { } error ? Task.FromException<IReadOnlyList<SavedGame>>(error)
            : Task.FromResult<IReadOnlyList<SavedGame>>([.. Saves.Values.OrderByDescending(s => s.SavedAt)]);

    public Task<SavedGame?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        LoadError is { } error ? Task.FromException<SavedGame?>(error) : Task.FromResult(Saves.GetValueOrDefault(id));

    public async Task SaveAsync(SavedGame game, CancellationToken cancellationToken = default)
    {
        SaveAttempts.Add(game);
        if (SaveGate is { } gate)
        {
            await gate.Task;
        }

        if (SaveError is { } error)
        {
            throw error;
        }
        Saves[game.Id] = game;
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

    /// <summary>How often the startup purge ran - the splash must run it exactly once per launch.</summary>
    public int PurgeCalls { get; private set; }

    public Task<int> PurgeUnrebuildableAsync(CancellationToken cancellationToken = default)
    {
        PurgeCalls++;
        return Task.FromResult(0);
    }
}

public sealed class FakeGameCompletionRepository(FakeProgressRepository progress, FakeSaveGameRepository saves)
    : IGameCompletionRepository
{
    private readonly Dictionary<Guid, PuzzleCompletion> _pending = [];
    private readonly HashSet<Guid> _applied = [];
    public List<(Guid Id, PuzzleCompletion Completion)> Attempts { get; } = [];
    public bool Fails { get; set; }
    public TaskCompletionSource? CompleteGate { get; set; }
    public int RetryCalls { get; private set; }

    public async Task JournalAsync(Guid sessionId, PuzzleCompletion completion)
    {
        Attempts.Add((sessionId, completion));
        if (CompleteGate is { } gate)
        {
            await gate.Task;
        }

        if (Fails)
        {
            throw new IOException("Completion storage unavailable");
        }

        if (!_applied.Contains(sessionId))
        {
            _pending.TryAdd(sessionId, completion);
        }
    }

    public async Task RetryPendingAsync()
    {
        RetryCalls++;
        if (Fails)
        {
            throw new IOException("Completion storage unavailable");
        }

        foreach (var (id, completion) in _pending.ToArray())
        {
            await progress.RecordCompletionAsync(completion);
            await saves.DeleteAsync(id);
            _applied.Add(id);
            _pending.Remove(id);
        }
    }
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

    public Puzzle? FindById(string puzzleId, int? revision = null) =>
        PuzzlesList.FirstOrDefault(p => string.Equals(p.Id, puzzleId, StringComparison.Ordinal)
                                       && (revision is null || revision == p.Revision));
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
