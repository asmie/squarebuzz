using Squarebuzz.Core.Content;
using Squarebuzz.Core.Generation;
using Squarebuzz.Core.Model;
using Xunit;

namespace Squarebuzz.Presentation.Tests;

public sealed class PuzzleRestartTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(null)]
    public async Task HintBudget_RestartKeepsIt_NextReadsNewPreferences(int? limit)
    {
        var factory = new GameSessionFactory(new EmbeddedPuzzleRepository(), new UniqueSolutionGenerator(new BlobPuzzleGenerator()));
        using var playing = new GameViewModelHarness(factory);
        playing.Settings.Settings = GameSettings.Default with
        {
            Helpers = HelperSettings.Default with { HintBudget = new HintBudget(limit) },
        };
        await playing.Vm.StartAsync(NewGameOptions.Default with { Seed = 42 });
        Assert.Equal(limit is null, playing.Vm.HasUnlimitedHints);
        if (limit is null) Assert.Contains("∞", playing.Vm.StatusText);
        playing.Vm.UseHintCommand.Execute(null);
        Assert.Equal(limit is null, playing.Vm.CanUseHint);
        playing.Settings.Settings = GameSettings.Default with
        {
            Helpers = HelperSettings.Default with { HintBudget = new HintBudget(8) },
        };

        await playing.Vm.RestartCommand.ExecuteAsync(null);
        Assert.Equal(limit, playing.Vm.Session!.HintBudget.Limit);
        Assert.True(playing.Vm.CanUseHint);
        Assert.Equal(0, playing.Vm.Session.HintsUsed);
        await playing.Vm.NextPuzzleCommand.ExecuteAsync(null);
        Assert.Equal(8, playing.Vm.HintsRemaining);
        Assert.False(playing.Vm.HasUnlimitedHints);
    }

    [Fact]
    public async Task NextPuzzle_StillSelectsAnotherAuthoredPicture()
    {
        var factory = new GameSessionFactory(new EmbeddedPuzzleRepository(),
            new UniqueSolutionGenerator(new BlobPuzzleGenerator()));
        using var playing = new GameViewModelHarness(factory);
        await playing.Vm.StartAsync(NewGameOptions.Default with { Seed = 42 });
        var puzzle = playing.Vm.Session!.Puzzle;

        await playing.Vm.NextPuzzleCommand.ExecuteAsync(null);

        Assert.False(playing.Vm.Session!.Puzzle.IsGenerated);
        Assert.NotEqual(puzzle.Id, playing.Vm.Session.Puzzle.Id);
    }

    [Theory]
    [InlineData(5, false, false, false)]
    [InlineData(5, false, true, false)]
    [InlineData(5, true, false, false)]
    [InlineData(5, true, true, false)]
    [InlineData(20, true, false, false)]
    [InlineData(20, true, true, false)]
    [InlineData(5, false, false, true)]
    [InlineData(20, true, false, true)]
    public async Task Restart_PreservesTheResolvedPuzzleAndResetsTheAttempt(
        int size, bool generated, bool resumeFirst, bool timed)
    {
        // Real puzzles distinguish a restart from a fresh random generation or authored pick.
        var puzzles = new EmbeddedPuzzleRepository();
        var factory = new GameSessionFactory(puzzles, new UniqueSolutionGenerator(new BlobPuzzleGenerator()));
        using var original = new GameViewModelHarness(factory);
        await original.Vm.StartAsync(NewGameOptions.Default with
        {
            Size = size,
            Seed = 42,
            ForceGenerated = generated,
            TimeLimit = timed ? TimeSpan.FromMinutes(2) : null,
        });
        var initial = original.Vm.Session!;
        var solution = initial.Puzzle.Solution.ToArray();
        original.Clock.Advance(TimeSpan.FromSeconds(5));
        original.Vm.Paint(Array.FindIndex(solution, filled => !filled), CellState.Filled);
        original.Vm.UseHintCommand.Execute(null);
        Assert.Equal(1, initial.Mistakes);
        Assert.Equal(1, initial.HintsUsed);
        Assert.Contains(CellState.Filled, initial.Cells.ToArray());

        using var resumed = new GameViewModelHarness(factory);
        var playing = original;
        if (resumeFirst)
        {
            await original.Vm.AutosaveAsync();
            var save = Assert.Single(original.SaveGames.Saves).Value;
            resumed.SaveGames.Saves[save.Id] = save;
            resumed.Vm.ApplyQueryAttributes(new Dictionary<string, object> { ["saveId"] = save.Id.ToString("D") });
            await resumed.Vm.InitialiseAsync();
            playing = resumed;
            Assert.Equal(initial.Cells.ToArray(), playing.Vm.Session!.Cells.ToArray());
        }

        await playing.Vm.RestartCommand.ExecuteAsync(null);

        var restarted = playing.Vm.Session!;
        Assert.NotSame(initial, restarted);
        Assert.Equal(initial.Seed, restarted.Seed);
        Assert.Equal(initial.Puzzle.Id, restarted.Puzzle.Id);
        Assert.Equal(initial.Puzzle.IsGenerated, restarted.Puzzle.IsGenerated);
        Assert.Equal(solution, restarted.Puzzle.Solution.ToArray());
        Assert.Equal(initial.TimeLimit, restarted.TimeLimit);
        Assert.All(restarted.Cells.ToArray(), cell => Assert.Equal(CellState.Empty, cell));
        Assert.Equal(TimeSpan.Zero, restarted.Elapsed);
        Assert.Equal(0, restarted.Mistakes);
        Assert.Equal(0, restarted.HintsUsed);
        Assert.Equal(initial.Rules.HintAllowance, restarted.HintsRemaining);
        Assert.False(restarted.CanUndo);
        Assert.False(restarted.CanRedo);
        Assert.False(playing.Vm.IsPaused);
        Assert.False(playing.Vm.IsTimeUp);
        Assert.False(playing.Vm.IsSolved);
    }
}
