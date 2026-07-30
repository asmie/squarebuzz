using Squarebuzz.Core.Model;

namespace Squarebuzz.Core.Progression;

/// <summary>Where a node sits relative to the player's progress.</summary>
public enum PathNodeState
{
    /// <summary>Already finished. Replayable - a child who liked a picture should be able to do it again.</summary>
    Done,

    /// <summary>The next one to play. Exactly one node is ever current.</summary>
    Current,

    /// <summary>Not reached yet. Shown, numbered and dimmed rather than hidden.</summary>
    Locked,
}

/// <summary>One stop on the path.</summary>
public sealed record PathNode(int Number, string PuzzleId, int Size, PathNodeState State)
{
    /// <summary>
    /// Done and current nodes can be started; locked ones cannot.
    /// </summary>
    /// <remarks>
    /// Locked nodes are still drawn, with their number visible. The design is explicit about the
    /// equivalent decision for the 25x25 size card - "visible, explained, not hidden" - because a
    /// child who can see what is coming has a reason to carry on.
    /// </remarks>
    public bool IsPlayable => State != PathNodeState.Locked;
}

/// <summary>
/// The ordered run through the authored pictures that the design calls the Puzzle Path.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately derived rather than stored. A node is done when its picture is in the solved
/// table, which the game already records for the Gallery and the trophies, so the path needs no
/// state of its own and cannot disagree with the rest of the game about what has been finished.
/// </para>
/// <para>
/// The order is the order the content ships in, which already ramps: the six 5x5 pictures and then
/// the six 10x10 ones. Sorting here would silently reorder the path whenever a puzzle was added,
/// so instead the content file is the single place that decides.
/// </para>
/// </remarks>
public static class PuzzlePath
{
    /// <summary>
    /// Builds the path from the authored pictures and the ids the player has finished.
    /// </summary>
    public static IReadOnlyList<PathNode> Build(
        IReadOnlyList<Puzzle> puzzles,
        IReadOnlyCollection<string> solvedPuzzleIds)
    {
        ArgumentNullException.ThrowIfNull(puzzles);
        ArgumentNullException.ThrowIfNull(solvedPuzzleIds);

        var solved = new HashSet<string>(solvedPuzzleIds, StringComparer.Ordinal);
        var nodes = new List<PathNode>(puzzles.Count);
        var currentTaken = false;

        for (var i = 0; i < puzzles.Count; i++)
        {
            var puzzle = puzzles[i];

            PathNodeState state;

            if (solved.Contains(puzzle.Id))
            {
                // A gap is possible: a picture played from the Gallery can be finished out of
                // order. It still counts as done, and the first *unsolved* one is where the path
                // says to go next - which is the only reading that cannot leave the player with
                // two current nodes or none.
                state = PathNodeState.Done;
            }
            else if (!currentTaken)
            {
                state = PathNodeState.Current;
                currentTaken = true;
            }
            else
            {
                state = PathNodeState.Locked;
            }

            nodes.Add(new PathNode(i + 1, puzzle.Id, puzzle.Width, state));
        }

        return nodes;
    }

    /// <summary>How many nodes are finished.</summary>
    public static int CountDone(IReadOnlyList<PathNode> nodes)
    {
        ArgumentNullException.ThrowIfNull(nodes);

        var done = 0;

        foreach (var node in nodes)
        {
            if (node.State == PathNodeState.Done)
            {
                done++;
            }
        }

        return done;
    }

    /// <summary>The node to play next, or null when every picture is finished.</summary>
    public static PathNode? Current(IReadOnlyList<PathNode> nodes)
    {
        ArgumentNullException.ThrowIfNull(nodes);

        foreach (var node in nodes)
        {
            if (node.State == PathNodeState.Current)
            {
                return node;
            }
        }

        return null;
    }
}
