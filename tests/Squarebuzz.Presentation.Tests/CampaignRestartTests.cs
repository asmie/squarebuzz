using System.Globalization;
using Squarebuzz.Core.Content;
using Squarebuzz.Core.Generation;
using Squarebuzz.Core.Model;
using Squarebuzz.Core.Progression;
using Xunit;

namespace Squarebuzz.Presentation.Tests;

public sealed class CampaignRestartTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(41)]
    [InlineData(201)]
    public async Task SavedLevel_ResumesAndRestartsWithTheSamePuzzle(int level)
    {
        // Real content and generation matter: the usual 2x2 fake cannot distinguish a
        // generated level from an authored picture selected accidentally during Restart.
        var puzzles = new EmbeddedPuzzleRepository();
        var factory = new GameSessionFactory(puzzles, new UniqueSolutionGenerator(new BlobPuzzleGenerator()));
        using var original = new GameViewModelHarness(factory);
        original.PuzzleRepository.PuzzlesList.AddRange(puzzles.Puzzles);
        original.Vm.ApplyQueryAttributes(new Dictionary<string, object>
        {
            ["level"] = level.ToString(CultureInfo.InvariantCulture),
        });
        await original.Vm.InitialiseAsync();
        var initial = original.Vm.Session!;
        var filledIndex = Array.FindIndex(initial.Puzzle.Solution.ToArray(), filled => filled);
        original.Vm.Paint(filledIndex, CellState.Filled);
        await original.Vm.AutosaveAsync();
        var save = Assert.Single(original.SaveGames.Saves).Value;

        using var resumed = new GameViewModelHarness(factory);
        resumed.PuzzleRepository.PuzzlesList.AddRange(puzzles.Puzzles);
        resumed.SaveGames.Saves[save.Id] = save;
        resumed.Vm.ApplyQueryAttributes(new Dictionary<string, object> { ["saveId"] = save.Id.ToString("D") });
        await resumed.Vm.InitialiseAsync();
        Assert.Equal(initial.Cells.ToArray(), resumed.Vm.Session!.Cells.ToArray());
        Assert.Equal(initial.Puzzle.Id, resumed.Vm.Session.Puzzle.Id);

        await resumed.Vm.RestartCommand.ExecuteAsync(null);

        var restarted = resumed.Vm.Session!;
        Assert.Equal(level, restarted.Origin!.Level);
        Assert.Equal(LevelCatalog.SeedFor(level), restarted.Seed);
        Assert.Equal(initial.Puzzle.IsGenerated, restarted.Puzzle.IsGenerated);
        Assert.Equal(initial.Puzzle.Id, restarted.Puzzle.Id);
        Assert.Equal(initial.Puzzle.Solution.ToArray(), restarted.Puzzle.Solution.ToArray());
        Assert.All(restarted.Cells.ToArray(), cell => Assert.Equal(CellState.Empty, cell));
        Assert.Equal(TimeSpan.Zero, restarted.Elapsed);
        Assert.Equal(0, restarted.Mistakes);
        Assert.Equal(0, restarted.HintsUsed);
    }
}
