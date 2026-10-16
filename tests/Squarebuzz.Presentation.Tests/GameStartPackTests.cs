using Squarebuzz.Core.Model;
using Xunit;

namespace Squarebuzz.Presentation.Tests;

public sealed class GameStartPackTests
{
    [Theory]
    [InlineData(5, false, null, "surprise", 1)]
    [InlineData(10, false, null, "surprise", 1)]
    [InlineData(15, false, null, "surprise", 0)]
    [InlineData(5, true, null, "surprise", 0)]
    [InlineData(5, false, "heart", "surprise", 0)]
    [InlineData(5, false, "missing", "surprise", 1)]
    [InlineData(5, false, null, "animals", 0)]
    public async Task StartingAGame_ReadsPackProgressOnlyWhenSelectionNeedsIt(
        int size, bool generated, string? puzzleId, string packId, int expectedReads)
    {
        using var h = new GameViewModelHarness();
        h.PuzzleRepository.PacksList.Add(new PackDefinition("surprise", "?", false, true));
        h.PuzzleRepository.PacksList.Add(new PackDefinition("animals", "A", false, false));
        h.PuzzleRepository.PuzzlesList.Add(Puzzle.FromRows("heart", "animals", "#FF8A3D",
            ["..#..", "..#..", "#####", "..#..", "..#.."]));

        await h.Vm.StartAsync(new NewGameOptions(size, 2, packId, ChallengeLevel.Relaxed)
        {
            ForceGenerated = generated,
            PuzzleId = puzzleId,
        });

        Assert.NotNull(h.Vm.Session);
        Assert.Equal(expectedReads, h.Progress.SolvedLoadCalls);
    }
}
