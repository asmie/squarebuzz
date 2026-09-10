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
        var rules = GameRules.Create(options.Challenge, options.Helpers);

        // The resolved seed is recorded even when the caller left it null, so this exact
        // game can be saved and resumed.
        return new GameSession(puzzle, rules, options with { Seed = seed }, seed);
    }

    /// <summary>Resumes a specific picture, for continuing a saved game.</summary>
    /// <remarks>
    /// Kept as an instance member despite touching no fields: it is the resume counterpart to
    /// <see cref="Create"/>, and callers reach both through the injected factory. Making it
    /// static would split session construction across two call styles for no benefit.
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

    /// <summary>
    /// Recovers the picture a save was playing, without building a session around it.
    /// </summary>
    /// <remarks>
    /// Needed on its own by the Continue screen, which draws a thumbnail of every save but has
    /// no reason to construct playable sessions for a list. Authored pictures are looked up by
    /// id; generated ones are rebuilt from the stored seed, which is the whole reason generation
    /// has to be deterministic.
    /// </remarks>
    public Puzzle ResolvePuzzle(SavedGame save)
    {
        ArgumentNullException.ThrowIfNull(save);

        if (save.PuzzleId is { } id)
        {
            return _repository.FindById(id)
                   ?? throw new InvalidOperationException(
                       $"Saved game references puzzle '{id}', which is no longer in the shipped content.");
        }

        return _generator.Generate(new PuzzleRequest(save.Size, save.Size, save.Difficulty, save.PackId, save.Seed));
    }

    /// <summary>Rebuilds a playable session from a save, marks and all.</summary>
    /// <remarks>
    /// The result is always untimed. That is not an omission here but a property of the save
    /// itself: <see cref="SavedGame"/> carries no time limit and <see cref="SavedGame.FromSession"/>
    /// refuses a timed session, so there is no countdown to restore.
    /// </remarks>
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
            ForceGenerated = save.IsGenerated,
            Level = save.Level,
            DailyDate = save.DailyDate,
        };

        var puzzle = ResolvePuzzle(save);

        var session = new GameSession(puzzle, GameRules.Create(save.Challenge, helpers), options, save.Seed);
        session.Restore(save.Cells, save.Elapsed, save.HintsRemaining, save.HintsUsed, save.Mistakes);

        return session;
    }

    private Puzzle SelectPuzzle(NewGameOptions options, int seed, IReadOnlySet<string>? unlockedPackIds)
    {
        // An explicit pick wins over size and pack - that is the whole point of choosing from
        // the Gallery. A missing id falls through rather than failing, so removing content
        // cannot strand a player on an error.
        if (options.PuzzleId is { } requested && _repository.FindById(requested) is { } picked)
        {
            return picked;
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
