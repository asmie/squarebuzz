namespace Squarebuzz.Core.Generation;

/// <summary>
/// A small linear congruential generator, matching the constants the prototype used.
/// </summary>
/// <remarks>
/// Deliberately not <see cref="System.Random"/>: puzzle generation must be reproducible from
/// a seed so that a saved game can be regenerated, the daily puzzle is the same for everyone,
/// and tests are not flaky. The numeric quality of an LCG is far more than enough for
/// scattering blobs on a grid.
/// </remarks>
public sealed class DeterministicRandom
{
    private const uint Multiplier = 1664525u;
    private const uint Increment = 1013904223u;
    private const double UInt32Range = 4294967296.0;

    private uint _state;

    public DeterministicRandom(int seed) => _state = unchecked((uint)seed);

    /// <summary>Next value in <c>[0, 1)</c>.</summary>
    public double NextDouble()
    {
        _state = unchecked((_state * Multiplier) + Increment);
        return _state / UInt32Range;
    }

    /// <summary>Next value in <c>[0, maxExclusive)</c>.</summary>
    public int Next(int maxExclusive)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxExclusive);

        return (int)(NextDouble() * maxExclusive);
    }
}
