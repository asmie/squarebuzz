using Squarebuzz.Core.Content;
using Squarebuzz.Core.Model;
using Squarebuzz.Core.Progression;
using Xunit;

namespace Squarebuzz.Core.Tests.Progression;

public class PuzzlePathTests
{
    private static IReadOnlyList<Puzzle> Authored() => new EmbeddedPuzzleRepository().Puzzles;

    [Fact]
    public void AFreshPlayer_StartsAtTheFirstNodeWithEverythingElseLocked()
    {
        var nodes = PuzzlePath.Build(Authored(), []);

        Assert.Equal(Authored().Count, nodes.Count);
        Assert.Equal(PathNodeState.Current, nodes[0].State);
        Assert.All(nodes.Skip(1), n => Assert.Equal(PathNodeState.Locked, n.State));

        Assert.Equal(0, PuzzlePath.CountDone(nodes));
        Assert.Equal(nodes[0], PuzzlePath.Current(nodes));
    }

    [Fact]
    public void NodesAreNumberedFromOneInContentOrder()
    {
        // The content file decides the order, and it already ramps - the 5x5 pictures before the
        // 10x10 ones. Sorting here would quietly reshuffle the path when a puzzle was added.
        var puzzles = Authored();
        var nodes = PuzzlePath.Build(puzzles, []);

        for (var i = 0; i < nodes.Count; i++)
        {
            Assert.Equal(i + 1, nodes[i].Number);
            Assert.Equal(puzzles[i].Id, nodes[i].PuzzleId);
            Assert.Equal(puzzles[i].Width, nodes[i].Size);
        }

        // Guards the ramp itself: a later node must never be smaller than an earlier one.
        for (var i = 1; i < nodes.Count; i++)
        {
            Assert.True(
                nodes[i].Size >= nodes[i - 1].Size,
                $"Node {nodes[i].Number} ({nodes[i].Size}) is smaller than node {nodes[i - 1].Number} " +
                $"({nodes[i - 1].Size}); the path no longer gets harder as it goes.");
        }
    }

    [Fact]
    public void FinishingInOrder_MovesTheCurrentNodeAlong()
    {
        var puzzles = Authored();
        var solved = new List<string>();

        for (var i = 0; i < puzzles.Count - 1; i++)
        {
            solved.Add(puzzles[i].Id);

            var nodes = PuzzlePath.Build(puzzles, solved);

            Assert.Equal(i + 1, PuzzlePath.CountDone(nodes));
            Assert.Equal(puzzles[i + 1].Id, PuzzlePath.Current(nodes)?.PuzzleId);
        }
    }

    [Fact]
    public void SolvingOutOfOrder_LeavesExactlyOneCurrentNode()
    {
        // The Gallery lets a picture be played directly, so a later one can be finished first.
        // Whatever happens there, the path must never show two "next" nodes or none at all.
        var puzzles = Authored();
        var nodes = PuzzlePath.Build(puzzles, [puzzles[4].Id, puzzles[7].Id]);

        Assert.Equal(2, PuzzlePath.CountDone(nodes));
        Assert.Single(nodes, n => n.State == PathNodeState.Current);
        Assert.Equal(puzzles[0].Id, PuzzlePath.Current(nodes)?.PuzzleId);

        // The out-of-order ones read as done even though earlier nodes are not.
        Assert.Equal(PathNodeState.Done, nodes[4].State);
        Assert.Equal(PathNodeState.Done, nodes[7].State);
        Assert.Equal(PathNodeState.Locked, nodes[5].State);
    }

    [Fact]
    public void AFinishedPath_HasNoCurrentNode()
    {
        var puzzles = Authored();
        var nodes = PuzzlePath.Build(puzzles, [.. puzzles.Select(p => p.Id)]);

        Assert.All(nodes, n => Assert.Equal(PathNodeState.Done, n.State));
        Assert.Equal(puzzles.Count, PuzzlePath.CountDone(nodes));
        Assert.Null(PuzzlePath.Current(nodes));
    }

    [Fact]
    public void OnlyDoneAndCurrentNodesCanBePlayed()
    {
        var puzzles = Authored();
        var nodes = PuzzlePath.Build(puzzles, [puzzles[0].Id]);

        Assert.True(nodes[0].IsPlayable);   // done, replayable
        Assert.True(nodes[1].IsPlayable);   // current
        Assert.False(nodes[2].IsPlayable);  // locked
    }

    [Fact]
    public void UnknownSolvedIds_AreIgnored()
    {
        // Generated puzzles are recorded with a null id and never reach here, but a stale row from
        // renamed content must not shift the path.
        var nodes = PuzzlePath.Build(Authored(), ["not-a-real-puzzle", "another"]);

        Assert.Equal(0, PuzzlePath.CountDone(nodes));
        Assert.Equal(PathNodeState.Current, nodes[0].State);
    }

    [Fact]
    public void AnEmptyContentSet_YieldsAnEmptyPath()
    {
        var nodes = PuzzlePath.Build([], []);

        Assert.Empty(nodes);
        Assert.Equal(0, PuzzlePath.CountDone(nodes));
        Assert.Null(PuzzlePath.Current(nodes));
    }
}
