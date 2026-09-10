using Squarebuzz.Core.Model;
using Squarebuzz.Presentation.ViewModels;
using Xunit;

namespace Squarebuzz.Presentation.Tests;

public sealed class GameLocalizationTests : IDisposable
{
    private readonly GameViewModelHarness _h = new();

    public GameLocalizationTests()
    {
        _h.PuzzleRepository.PuzzlesList.Add(Puzzle.FromRows("heart", "animals", "#FF8A3D", ["#.", ".#"]));
        _h.Strings.Translations[(AppLanguage.English, "Puzzle_heart")] = "Heart";
        _h.Strings.Translations[(AppLanguage.Polish, "Puzzle_heart")] = "Serce";
        _h.Strings.Translations[(AppLanguage.English, "Puzzle_gen")] = "Random picture";
        _h.Strings.Translations[(AppLanguage.Polish, "Puzzle_gen")] = "Losowy obrazek";
        _h.Strings.Translations[(AppLanguage.English, "levelN")] = "Level {0}";
        _h.Strings.Translations[(AppLanguage.Polish, "levelN")] = "Poziom {0}";
    }

    public void Dispose() => _h.Dispose();

    [Theory]
    [InlineData(false, null, false, "Heart", "Serce")]
    [InlineData(false, null, true, "Heart", "Serce")]
    [InlineData(true, null, false, "Random picture", "Losowy obrazek")]
    [InlineData(true, null, true, "Random picture", "Losowy obrazek")]
    [InlineData(true, 7, false, "Level 7", "Poziom 7")]
    [InlineData(true, 7, true, "Level 7", "Poziom 7")]
    public async Task LanguageChange_RefreshesTheBoundPuzzleNameWithoutChangingTheGame(
        bool generated, int? level, bool solved, string english, string polish)
    {
        await _h.Vm.StartAsync(NewGameOptions.Default with
        {
            PuzzleId = generated ? null : "heart",
            ForceGenerated = generated,
            Level = level,
        });
        if (solved)
        {
            _h.SolveCurrentPuzzle();
            await GameViewModelHarness.WaitUntilAsync(() => _h.Progress.Completions.Count == 1);
        }

        var session = _h.Vm.Session!;
        var cells = session.Cells.ToArray();
        var boundName = _h.Vm.PuzzleName;
        Assert.Equal(english, boundName);
        _h.Vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is null or "" or nameof(GameViewModel.PuzzleName))
            {
                boundName = _h.Vm.PuzzleName;
            }
        };

        _h.Strings.SetLanguage(AppLanguage.Polish);

        Assert.Equal(polish, boundName);
        Assert.Equal(polish, _h.Vm.PuzzleName);
        Assert.Same(session, _h.Vm.Session);
        Assert.Equal(cells, session.Cells.ToArray());
        Assert.Equal(solved, _h.Vm.IsSolved);
        Assert.Equal(solved ? 1 : 0, _h.Progress.Completions.Count);
    }

    [Fact]
    public async Task ResumedPuzzle_ChangesItsNameWithTheLanguage()
    {
        await _h.Vm.StartAsync(NewGameOptions.Default with { PuzzleId = "heart" });
        _h.Vm.Paint(0, CellState.Filled);
        await _h.Vm.AutosaveAsync();
        var save = Assert.Single(_h.SaveGames.Saves).Value;
        _h.Vm.ApplyQueryAttributes(new Dictionary<string, object> { ["saveId"] = save.Id.ToString("D") });
        await _h.Vm.InitialiseAsync();

        _h.Strings.SetLanguage(AppLanguage.Polish);

        Assert.Equal("Serce", _h.Vm.PuzzleName);
        Assert.Equal(CellState.Filled, _h.Vm.Session![0]);
    }

    [Fact]
    public async Task ReplacingTheSession_NotifiesTheNewPuzzleName()
    {
        await _h.Vm.StartAsync(NewGameOptions.Default with { PuzzleId = "heart" });
        var boundName = _h.Vm.PuzzleName;
        _h.Vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is null or "" or nameof(GameViewModel.PuzzleName))
            {
                boundName = _h.Vm.PuzzleName;
            }
        };

        await _h.Vm.StartAsync(NewGameOptions.Default with { ForceGenerated = true });

        Assert.Equal("Random picture", boundName);
    }

    [Fact]
    public async Task LanguageChange_RefreshesExistingCellDescriptionsWithoutRebuildingTheOverlay()
    {
        _h.Accessibility.IsScreenReaderActive = true;
        _h.Settings.Settings = GameSettings.Default with
        {
            Helpers = HelperSettings.Default with { AutoCross = false },
        };
        _h.Strings.Translations[(AppLanguage.English, "a11yCell")] = "Row {0}, column {1}: {2}. Clues {3}, {4}.";
        _h.Strings.Translations[(AppLanguage.Polish, "a11yCell")] = "Wiersz {0}, kolumna {1}: {2}. Wskazowki {3}, {4}.";
        _h.Strings.Translations[(AppLanguage.English, "a11yCellFilled")] = "filled";
        _h.Strings.Translations[(AppLanguage.Polish, "a11yCellFilled")] = "wypelnione";
        _h.Strings.Translations[(AppLanguage.English, "a11yCellCrossed")] = "crossed";
        _h.Strings.Translations[(AppLanguage.Polish, "a11yCellCrossed")] = "skreslone";
        _h.Strings.Translations[(AppLanguage.English, "a11yCellEmpty")] = "empty";
        _h.Strings.Translations[(AppLanguage.Polish, "a11yCellEmpty")] = "puste";
        await _h.Vm.StartAsync();
        _h.Vm.Paint(0, CellState.Filled);
        _h.Vm.Paint(1, CellState.Crossed);

        // Like the page, retain descriptions until explicitly told to refresh them. Calling
        // DescribeCell only after the language change would miss the stale-control regression.
        var descriptions = Enumerable.Range(0, 4).Select(_h.Vm.DescribeCell).ToArray();
        Assert.Equal("Row 1, column 1: filled. Clues 1, 1.", descriptions[0]);
        var refreshes = 0;
        var rebuilds = 0;
        _h.Vm.CellDescriptionsChanged += (_, _) =>
        {
            refreshes++;
            for (var index = 0; index < descriptions.Length; index++)
            {
                descriptions[index] = _h.Vm.DescribeCell(index);
            }
        };
        _h.Vm.OverlayNeedChanged += (_, _) => rebuilds++;

        _h.Strings.SetLanguage(AppLanguage.Polish);

        Assert.Equal("Wiersz 1, kolumna 1: wypelnione. Wskazowki 1, 1.", descriptions[0]);
        Assert.Equal("Wiersz 1, kolumna 2: skreslone. Wskazowki 1, 1.", descriptions[1]);
        Assert.Equal("Wiersz 2, kolumna 1: puste. Wskazowki 1, 1.", descriptions[2]);
        Assert.Equal(1, refreshes);
        Assert.Equal(0, rebuilds);
    }

    [Fact]
    public void LanguageChange_BeforeInitialisationLeavesThePuzzleNameEmpty()
    {
        _h.Strings.SetLanguage(AppLanguage.Polish);

        Assert.Empty(_h.Vm.PuzzleName);
        Assert.Empty(_h.Vm.DescribeCell(0));
    }
}
