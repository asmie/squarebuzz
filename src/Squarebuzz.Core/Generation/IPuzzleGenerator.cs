using Squarebuzz.Core.Model;

namespace Squarebuzz.Core.Generation;

/// <summary>
/// What to generate.
/// </summary>
/// <param name="Width">Grid width in cells.</param>
/// <param name="Height">Grid height in cells.</param>
/// <param name="Difficulty">1 (very easy) to 5 (expert). Drives how sparse the picture is.</param>
/// <param name="Pack">Pack the result is attributed to, for display purposes.</param>
/// <param name="Seed">Makes generation reproducible; the same seed always yields the same picture.</param>
public sealed record PuzzleRequest(int Width, int Height, int Difficulty, string Pack, int Seed)
{
    public const int MinDifficulty = 1;
    public const int MaxDifficulty = 5;
}

/// <summary>
/// Produces puzzles for the grid sizes that have no authored content (15x15 and above).
/// </summary>
/// <remarks>
/// Strategy pattern: <see cref="BlobPuzzleGenerator"/> decides what a picture looks like, and
/// <see cref="UniqueSolutionGenerator"/> wraps any generator to guarantee the result is fair.
/// Keeping the two apart means the shape-making can be replaced without touching the
/// correctness guarantee.
/// </remarks>
public interface IPuzzleGenerator
{
    Puzzle Generate(PuzzleRequest request);
}

/// <summary>Thrown when a generator cannot satisfy a request.</summary>
public sealed class PuzzleGenerationException : Exception
{
    public PuzzleGenerationException()
    {
    }

    public PuzzleGenerationException(string message)
        : base(message)
    {
    }

    public PuzzleGenerationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
