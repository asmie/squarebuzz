using Squarebuzz.Core.Model;
using Xunit;

namespace Squarebuzz.Presentation.Tests;

public sealed class AutomaticCrossAutosaveTests
{
    [Fact]
    public async Task ClaimingAnAutomaticCross_IsSavedEvenWhenVisibleCellsAreUnchanged()
    {
        using var harness = new GameViewModelHarness();
        var session = new GameSession(
            Puzzle.FromRows("plus", "test", "#FF8A3D", ["..#..", "..#..", "#####", "..#..", "..#.."]),
            GameRules.Relaxed, NewGameOptions.Default);
        harness.Saves.Attach(session);
        session.Paint(2, CellState.Filled);
        await harness.Saves.SaveAsync(onlyIfChanged: true);
        var first = Assert.Single(harness.SaveGames.SaveAttempts);
        Assert.True(first.AutoCrossedCells[0]);

        session.Paint(0, CellState.Crossed);
        await harness.Saves.SaveAsync(onlyIfChanged: true);
        Assert.Equal(2, harness.SaveGames.SaveAttempts.Count);
        var claimed = harness.SaveGames.SaveAttempts[1];
        Assert.Equal(first.Cells, claimed.Cells);
        Assert.False(claimed.AutoCrossedCells[0]);
        Assert.True(first.AutoCrossedCells[0]); // Previous snapshots are immutable copies.

        session.Paint(2, CellState.Empty);
        await harness.Saves.SaveAsync(onlyIfChanged: true);
        var cleared = harness.SaveGames.Saves[harness.Saves.Id];
        Assert.Equal(CellState.Crossed, cleared.Cells[0]);
        Assert.Equal(CellState.Empty, cleared.Cells[1]);
        Assert.DoesNotContain(true, cleared.AutoCrossedCells);
    }
}
