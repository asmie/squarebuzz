using Squarebuzz.Core.Abstractions;
using Squarebuzz.Core.Generation;

namespace Squarebuzz.Core.Model;

/// <summary>
/// Turns the player's New Game choices into a ready <see cref="GameSession"/>, picking an
/// authored picture where one exists and generating otherwise.
/// </summary>
public sealed class GameSessionFactory
{
    private readonly IPuzzleRepository _repository;
    private readonly IPuzzleGenerator _generator;

    public GameSessionFactory(IPuzzleRepository repository, IPuzzleGenerator generator)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(generator);

        _repository = repository;
        _generator = generator;
    }

    /// <param name="unlockedPackIds">
    /// Packs the player has earned, so the wildcard "surprise" pack can draw from them too.
    /// Passed in rather than looked up because this factory sits in the domain and has no route
    /// to saved progress. Null keeps to the shipped locks.
    /// </param>
    public GameSession Create(NewGameOptions options, IReadOnlySet<string>? unlockedPackIds = null)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (!GridSize.IsSupported(options.Size))
        {
            throw new ArgumentOutOfRangeException(nameof(options), options.Size, "Unsupported grid size.");
        }

        var seed = options.Seed ?? Random.Shared.Next();
        var puzzle = SelectPuzzle(options, seed, unlockedPackIds);
        var rules = GameRules.Create(options.Challenge, options.Helpers, options.HintBudget);

        // The resolved seed is recorded even when the caller left it null, so this exact
        // game can be saved and resumed.
        return new GameSession(puzzle, rules, options with { Seed = seed }, seed);
    }

    /// <summary>Creates a session for a specific picture.</summary>
    /// <remarks>
    /// Kept on the factory alongside Create so callers use one session-construction API.
    /// </remarks>
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Performance",
        "CA1822:Mark members as static",
        Justification = "Belongs on the factory abstraction alongside Create; see remarks.")]
    public GameSession CreateFor(Puzzle puzzle, ChallengeLevel challenge, HelperSettings helpers)
    {
        ArgumentNullException.ThrowIfNull(puzzle);

        return new GameSession(puzzle, GameRules.Create(challenge, helpers));
    }

    /// <summary>Resolves the picture used by a save.</summary>
    /// <remarks>
    /// Authored saves use an exact ID and revision. Generated saves use their original request and seed.
    /// </remarks>
    public Puzzle ResolvePuzzle(SavedGame save)
    {
        ArgumentNullException.ThrowIfNull(save);

        if (save.PuzzleId is { } id)
        {
            return _repository.FindById(id, save.PuzzleRevision)
                   ?? throw new InvalidOperationException(
                       $"Saved game references puzzle '{id}' revision {save.PuzzleRevision}, which is no longer in the shipped content.");
        }

        return _generator.Generate(new PuzzleRequest(save.Size, save.Size, save.Difficulty, save.PackId, save.Seed));
    }

    /// <summary>Restores an untimed session and its saved marks. Timed sessions cannot be saved.</summary>
    public GameSession Restore(SavedGame save, HelperSettings helpers)
    {
        ArgumentNullException.ThrowIfNull(save);
        ArgumentNullException.ThrowIfNull(helpers);

        var options = new NewGameOptions(save.Size, save.Difficulty, save.PackId, save.Challenge)
        {
            Helpers = helpers,
            Seed = save.Seed,
            // Restart recreates the session from this origin. Preserve the saved picture
            // selection instead of letting size and pack choose a different one.
            PuzzleId = save.PuzzleId,
            PuzzleRevision = save.IsGenerated ? null : save.PuzzleRevision,
            ForceGenerated = save.IsGenerated,
            Level = save.Level,
            DailyDate = save.DailyDate,
            HintBudget = save.HintBudget ?? HintBudget.LegacyFor(save.Challenge),
        };

        var puzzle = ResolvePuzzle(save);

        var session = new GameSession(puzzle, GameRules.Create(save.Challenge, helpers, options.HintBudget), options, save.Seed);
        session.Restore(save.Cells, save.Elapsed, save.HintsRemaining, save.HintsUsed, save.Mistakes, save.AutoCrossedCells);

        return session;
    }

    private Puzzle SelectPuzzle(NewGameOptions options, int seed, IReadOnlySet<string>? unlockedPackIds)
    {
        // An explicit gallery selection overrides size and pack. Missing gallery picks may fall
        // back, but a restart with an explicit revision must resolve exactly.
        if (options.PuzzleId is { } requested)
        {
            if (_repository.FindById(requested, options.PuzzleRevision) is { } picked)
            {
                return picked;
            }

            // A restart must never silently replace the board it is replaying.
            if (options.PuzzleRevision is { } revision)
            {
                throw new InvalidOperationException(
                    $"Puzzle '{requested}' revision {revision} is no longer in the shipped content.");
            }
        }

        if (GridSize.IsAuthored(options.Size) && !options.ForceGenerated)
        {
            var candidates = _repository.Find(options.PackId, options.Size, unlockedPackIds);

            // Fall back to any picture of the right size rather than failing: a pack with no
            // art at this size should still give the player a game, as the prototype did.
            if (candidates.Count == 0)
            {
                candidates = [.. _repository.Puzzles.Where(p => p.Width == options.Size && p.Height == options.Size)];
            }

            // "Next" must not repeat the picture just solved - unless it is the only one there
            // is, in which case a repeat beats no game at all.
            if (options.ExcludePuzzleId is { } excluded && candidates.Count > 1)
            {
                var filtered = candidates.Where(p => !string.Equals(p.Id, excluded, StringComparison.Ordinal)).ToList();

                if (filtered.Count > 0)
                {
                    candidates = filtered;
                }
            }

            if (candidates.Count > 0)
            {
                // Seeded so the same options always resolve to the same picture.
                var index = new DeterministicRandom(seed).Next(candidates.Count);
                return candidates[index];
            }
        }

        return _generator.Generate(new PuzzleRequest(
            options.Size,
            options.Size,
            options.Difficulty,
            options.PackId,
            seed));
    }
}
