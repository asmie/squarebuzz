using Squarebuzz.Core.Content;
using Squarebuzz.Core.Layout;
using Squarebuzz.Core.Model;
using Xunit;

namespace Squarebuzz.Core.Tests.Layout;

public class BoardLayoutTests
{
    private static Puzzle Heart() => new EmbeddedPuzzleRepository().FindById("heart")!;

    private static Puzzle Square(int size)
    {
        // A full block: every row and column has a single clue, so gutters stay one slot wide.
        var rows = Enumerable.Repeat(new string('#', size), size).ToList();
        return Puzzle.FromRows($"square{size}", "test", "#FF8A3D", rows);
    }

    [Fact]
    public void GuttersAreSizedForTheLongestClue()
    {
        // The heart's row 0 is ".#.#." -> clue "1 1", the widest row clue at two numbers.
        var layout = BoardLayout.Calculate(Heart(), 400, 600);

        Assert.Equal(2, layout.MaxRowClues);
        Assert.Equal(layout.MaxRowClues * layout.ClueSlot, layout.RowGutterWidth);
    }

    [Fact]
    public void ABlankLineStillReservesOneClueSlot()
    {
        // Row 0 of this picture is empty, so its clue is blank - but the gutter must not
        // collapse to zero width or the grid would shift relative to its neighbours.
        var puzzle = Puzzle.FromRows("blankrow", "test", "#FF8A3D", ["...", "###", "..."]);

        var layout = BoardLayout.Calculate(puzzle, 400, 600);

        Assert.Equal(1, layout.MaxRowClues);
        Assert.True(layout.RowGutterWidth > 0);
    }

    [Theory]
    [InlineData(5)]
    [InlineData(10)]
    [InlineData(15)]
    [InlineData(20)]
    public void PhoneSizedGridsFitAPhoneWithoutScrolling(int size)
    {
        var layout = BoardLayout.Calculate(Square(size), 390, 700);

        Assert.True(layout.TotalWidth <= 390, $"{size}x{size} needs {layout.TotalWidth} of 390 available.");
        Assert.False(layout.RequiresScrolling);
    }

    [Fact]
    public void TheGiantGridDoesNotFitAPhone_WhichIsWhyItIsTabletOnly()
    {
        // 25 cells at the 17-unit minimum touch target is already 425 units, before the clue
        // gutter. It cannot fit a 390-unit phone at a hittable size, which is exactly the
        // reason GridSize gates it to large screens. Pinned here so the two rules cannot
        // drift apart: if this ever fits, the gate is obsolete.
        var layout = BoardLayout.Calculate(Square(GridSize.Giant), 390, 700);

        Assert.True(layout.RequiresScrolling);
        Assert.Equal(BoardLayout.MinCellSize, layout.CellSize);
        Assert.True(GridSize.RequiresLargeScreen(GridSize.Giant));
    }

    [Fact]
    public void TheGiantGridFitsATablet()
    {
        // The 760-unit breakpoint the prototype used to unlock this size.
        var layout = BoardLayout.Calculate(Square(GridSize.Giant), 760, 1000);

        Assert.False(layout.RequiresScrolling);
        Assert.True(layout.CellSize > BoardLayout.MinCellSize);
    }

    [Fact]
    public void CellSizeNeverDropsBelowTheMinimumTouchTarget()
    {
        // A deliberately cramped viewport. The cell must stay hittable even if that means the
        // board overflows and has to be scrolled.
        var layout = BoardLayout.Calculate(Square(25), 120, 120);

        Assert.Equal(BoardLayout.MinCellSize, layout.CellSize);
        Assert.True(layout.RequiresScrolling);
    }

    [Fact]
    public void CellSizeIsCappedSoASmallGridDoesNotBecomeHuge()
    {
        var layout = BoardLayout.Calculate(Square(5), 4000, 4000);

        Assert.Equal(BoardLayout.MaxCellSize, layout.CellSize);
    }

    [Theory]
    [InlineData(70)]
    [InlineData(100)]
    [InlineData(160)]
    public void ZoomScalesTheCell(int zoom)
    {
        var baseline = BoardLayout.Calculate(Square(10), 600, 900);
        var zoomed = BoardLayout.Calculate(Square(10), 600, 900, zoom);

        if (zoom < 100)
        {
            Assert.True(zoomed.CellSize <= baseline.CellSize);
        }
        else if (zoom > 100)
        {
            Assert.True(zoomed.CellSize >= baseline.CellSize);
        }
        else
        {
            Assert.Equal(baseline.CellSize, zoomed.CellSize);
        }
    }

    [Fact]
    public void OutOfRangeZoomIsClamped()
    {
        var tooSmall = BoardLayout.Calculate(Square(10), 600, 900, 1);
        var atMinimum = BoardLayout.Calculate(Square(10), 600, 900, GameSettings.MinCellZoomPercent);

        Assert.Equal(atMinimum.CellSize, tooSmall.CellSize);
    }

    [Fact]
    public void AZeroSizedViewportDoesNotThrow()
    {
        // MAUI runs one layout pass before the view has been measured.
        var layout = BoardLayout.Calculate(Square(10), 0, 0);

        Assert.Equal(BoardLayout.MinCellSize, layout.CellSize);
    }

    [Fact]
    public void HitTestFindsEveryCellExactlyOnce()
    {
        var puzzle = Square(10);
        var layout = BoardLayout.Calculate(puzzle, 600, 900);
        var found = new HashSet<int>();

        for (var row = 0; row < layout.Rows; row++)
        {
            for (var column = 0; column < layout.Columns; column++)
            {
                var (x, y) = layout.CellOrigin(column, row);

                // Aim at the middle of the cell, as a finger would.
                var index = layout.HitTest(x + (layout.CellSize / 2), y + (layout.CellSize / 2));

                Assert.NotNull(index);
                Assert.Equal((row * layout.Columns) + column, index);
                Assert.True(found.Add(index.Value), $"Cell {index} was hit twice.");
            }
        }

        Assert.Equal(puzzle.CellCount, found.Count);
    }

    [Fact]
    public void HitTestRejectsTheGutters()
    {
        var layout = BoardLayout.Calculate(Square(10), 600, 900);

        // Inside the corner block, the row gutter, and the column gutter respectively.
        Assert.Null(layout.HitTest(1, 1));
        Assert.Null(layout.HitTest(1, layout.ColumnGutterHeight + 10));
        Assert.Null(layout.HitTest(layout.RowGutterWidth + 10, 1));
    }

    [Fact]
    public void HitTestRejectsPointsPastTheGrid()
    {
        var layout = BoardLayout.Calculate(Square(10), 600, 900);

        Assert.Null(layout.HitTest(layout.TotalWidth + 5, layout.TotalHeight / 2));
        Assert.Null(layout.HitTest(layout.TotalWidth / 2, layout.TotalHeight + 5));
        Assert.Null(layout.HitTest(-5, -5));
    }

    [Fact]
    public void HitTestIsExactAtCellBoundaries()
    {
        var layout = BoardLayout.Calculate(Square(10), 600, 900);
        var (x, y) = layout.CellOrigin(3, 4);

        // The top-left pixel belongs to this cell; one unit before it belongs to the previous.
        Assert.Equal((4 * 10) + 3, layout.HitTest(x, y));
        Assert.Equal((4 * 10) + 2, layout.HitTest(x - 1, y));
        Assert.Equal((3 * 10) + 3, layout.HitTest(x, y - 1));
    }

    [Theory]
    [InlineData(false, 9, 22)]
    [InlineData(true, 9, 22)]
    public void ClueFontSizeStaysWithinTheLegibleRange(bool bigNumbers, double min, double max)
    {
        foreach (var size in GridSize.All)
        {
            foreach (var viewport in new[] { 120d, 390d, 800d, 1600d })
            {
                var layout = BoardLayout.Calculate(Square(size), viewport, viewport, bigNumbers: bigNumbers);
                var fontSize = layout.ClueFontSize();

                Assert.InRange(fontSize, min, max);
            }
        }
    }

    [Fact]
    public void BigNumbersAreNeverSmallerThanNormalOnes()
    {
        var normal = BoardLayout.Calculate(Square(10), 600, 900);
        var big = BoardLayout.Calculate(Square(10), 600, 900, bigNumbers: true);

        Assert.True(big.ClueFontSize() >= normal.ClueFontSize());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TwoDigitCluesFitTheirSlot(bool bigNumbers)
    {
        // A digit advances roughly 0.6 em, so "10" needs about 1.2 x the font size. If this
        // fails, big clue numerals bleed into the neighbouring slot - the bug that prompted
        // sizing the slot from the same flag as the font.
        foreach (var size in GridSize.All)
        {
            foreach (var viewport in new[] { 120d, 390d, 800d, 1600d })
            {
                var layout = BoardLayout.Calculate(Square(size), viewport, viewport, bigNumbers: bigNumbers);

                Assert.True(
                    layout.ClueFontSize() * 1.2 <= layout.ClueSlot + 0.5,
                    $"{size}x{size} at {viewport}: font {layout.ClueFontSize()} in slot {layout.ClueSlot}.");
            }
        }
    }

    [Fact]
    public void BigNumbersWidenTheClueSlot()
    {
        var normal = BoardLayout.Calculate(Square(10), 600, 900);
        var big = BoardLayout.Calculate(Square(10), 600, 900, bigNumbers: true);

        Assert.True(big.ClueSlot / big.CellSize > normal.ClueSlot / normal.CellSize);
    }

    [Fact]
    public void GroupSeparatorsFallEveryFiveCellsButNotAtTheEdge()
    {
        var layout = BoardLayout.Calculate(Square(10), 600, 900);

        Assert.True(layout.IsGroupBoundaryAfterColumn(4));
        Assert.False(layout.IsGroupBoundaryAfterColumn(0));
        Assert.False(layout.IsGroupBoundaryAfterColumn(3));

        // Column 9 is the last one, so a separator there would draw on the outer border.
        Assert.False(layout.IsGroupBoundaryAfterColumn(9));
    }

    [Fact]
    public void TotalSizeIsGutterPlusGrid()
    {
        var layout = BoardLayout.Calculate(Heart(), 400, 600);

        Assert.Equal(layout.RowGutterWidth + (layout.Columns * layout.CellSize), layout.TotalWidth);
        Assert.Equal(layout.ColumnGutterHeight + (layout.Rows * layout.CellSize), layout.TotalHeight);
    }
}
