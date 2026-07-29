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

    public GameSession Create(NewGameOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (!GridSize.IsSupported(options.Size))
        {
            throw new ArgumentOutOfRangeException(nameof(options), options.Size, "Unsupported grid size.");
        }

        var seed = options.Seed ?? Random.Shared.Next();
        var puzzle = SelectPuzzle(options, seed);
        var rules = GameRules.Create(options.Challenge, options.Helpers);

        return new GameSession(puzzle, rules);
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

    private Puzzle SelectPuzzle(NewGameOptions options, int seed)
    {
        if (GridSize.IsAuthored(options.Size))
        {
            var candidates = _repository.Find(options.PackId, options.Size);

            // Fall back to any picture of the right size rather than failing: a pack with no
            // art at this size should still give the player a game, as the prototype did.
            if (candidates.Count == 0)
            {
                candidates = [.. _repository.Puzzles.Where(p => p.Width == options.Size && p.Height == options.Size)];
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
