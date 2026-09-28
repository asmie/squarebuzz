using Squarebuzz.Core.Model;
using Squarebuzz.Core.Solving;

namespace Squarebuzz.Core.Generation;

/// <summary>
/// Decorator that only lets through puzzles a player can actually reason out. It asks the
/// wrapped generator for candidates and rejects any that <see cref="PuzzleSolver"/> cannot
/// finish by line logic alone.
/// </summary>
/// <remarks>
/// <para>
/// This is what makes the game's own claim - "every puzzle has exactly one answer, never
/// guess" - true rather than aspirational. The prototype showed that text while generating
/// unvalidated grids.
/// </para>
/// <para>
/// Candidate seeds are derived deterministically from the request seed, so a given request
/// always yields the same accepted puzzle no matter how many candidates were rejected on
/// the way. That keeps saved games and the daily puzzle reproducible.
/// </para>
/// </remarks>
public sealed class UniqueSolutionGenerator : IPuzzleGenerator
{
    /// <summary>
    /// Sized from the measured acceptance rate of <see cref="BlobPuzzleGenerator"/>, which
    /// is 38-61% across 15x15 to 25x25 (pinned by UniqueSolutionGeneratorTests). At the worst
    /// observed rate the odds of all 60 candidates failing are around 1 in 10^12, and since
    /// the loop returns on first success the typical cost is only 2-3 solves.
    /// </summary>
    public const int DefaultMaxAttempts = 60;

    private readonly IPuzzleGenerator _inner;
    private readonly int _maxAttempts;

    public UniqueSolutionGenerator(IPuzzleGenerator inner, int maxAttempts = DefaultMaxAttempts)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxAttempts);

        _inner = inner;
        _maxAttempts = maxAttempts;
    }

    /// <summary>Candidates examined during the most recent <see cref="Generate"/> call. Diagnostics only.</summary>
    public int LastAttemptCount { get; private set; }

    public Puzzle Generate(PuzzleRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var seedSource = new DeterministicRandom(request.Seed);
        var closestUndetermined = int.MaxValue;

        for (var attempt = 1; attempt <= _maxAttempts; attempt++)
        {
            // The first attempt uses the caller's seed so a request that succeeds
            // immediately is identical to calling the inner generator directly.
            var seed = attempt == 1 ? request.Seed : seedSource.Next(int.MaxValue);
            var candidate = _inner.Generate(request with { Seed = seed });
            var result = PuzzleSolver.Analyse(candidate);

            if (result.IsSolvable)
            {
                LastAttemptCount = attempt;
                return candidate;
            }

            closestUndetermined = Math.Min(closestUndetermined, result.UndeterminedCells);
        }

        LastAttemptCount = _maxAttempts;

        // Reject the request if no candidate passes the solver within the attempt budget.
        throw new PuzzleGenerationException(
            $"No logically solvable {request.Width}x{request.Height} puzzle found for pack '{request.Pack}' " +
            $"at difficulty {request.Difficulty} within {_maxAttempts} attempts. " +
            $"The closest candidate still had {closestUndetermined} undetermined cells.");
    }
}
