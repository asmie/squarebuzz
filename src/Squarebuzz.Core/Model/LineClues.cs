using System.Collections;
using System.Collections.ObjectModel;

namespace Squarebuzz.Core.Model;

/// <summary>
/// The clue for one row or column: the lengths of its runs of filled cells, in order.
/// A row reading <c>##.#</c> has the clue <c>2 1</c>.
/// </summary>
/// <remarks>
/// A line with no filled cells has an empty run collection. The UI renders
/// that as a single "0", which is why <see cref="DisplayRuns"/> exists - but the solver
/// wants the genuinely empty sequence, so the two are kept apart deliberately.
/// </remarks>
public sealed class LineClues : IReadOnlyList<int>, IEquatable<LineClues>
{
    private static readonly ReadOnlyCollection<int> ZeroDisplay = Array.AsReadOnly<int>([0]);

    private readonly int[] _runs;

    public LineClues(params int[] runs)
    {
        ArgumentNullException.ThrowIfNull(runs);

        if (runs.Any(r => r <= 0))
        {
            throw new ArgumentOutOfRangeException(nameof(runs), "Clue runs must all be positive; use an empty sequence for a blank line.");
        }

        _runs = [.. runs];
        Sum = _runs.Sum();

        // The shortest line that could hold these runs: every run plus one gap between each.
        MinimumLength = _runs.Length == 0 ? 0 : Sum + _runs.Length - 1;
    }

    /// <summary>The clue for a line with no filled cells at all.</summary>
    public static LineClues Blank { get; } = new();

    /// <summary>Total number of filled cells this clue accounts for.</summary>
    public int Sum { get; }

    /// <summary>Shortest line length that could satisfy this clue.</summary>
    public int MinimumLength { get; }

    public bool IsBlank => _runs.Length == 0;

    /// <summary>
    /// The runs as the player sees them: a blank line shows a single "0" rather than nothing.
    /// </summary>
    public IReadOnlyList<int> DisplayRuns => _runs.Length == 0 ? ZeroDisplay : this;

    public int Count => _runs.Length;

    public int this[int index] => _runs[index];

    public IEnumerator<int> GetEnumerator() => ((IEnumerable<int>)_runs).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public bool Equals(LineClues? other)
    {
        if (other is null)
        {
            return false;
        }

        return ReferenceEquals(this, other) || _runs.AsSpan().SequenceEqual(other._runs);
    }

    public override bool Equals(object? obj) => Equals(obj as LineClues);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var run in _runs)
        {
            hash.Add(run);
        }

        return hash.ToHashCode();
    }

    public override string ToString() => _runs.Length == 0 ? "0" : string.Join(' ', _runs);
}
