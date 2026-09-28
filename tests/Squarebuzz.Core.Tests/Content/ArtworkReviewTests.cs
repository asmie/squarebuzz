using Squarebuzz.Core.Content;
using Squarebuzz.Core.Model;
using Xunit;

namespace Squarebuzz.Core.Tests.Content;

public class ArtworkReviewTests
{
    [Fact]
    public void LargerPictures_AvoidNearIdenticalHalvesExceptGeometricSubjects()
    {
        // These subjects are intentionally symmetric. See docs/puzzle-art.md.
        string[] exceptions = ["snowflake", "basketball"];
        foreach (var puzzle in new EmbeddedPuzzleRepository().Puzzles.Where(p => p.Width == 10))
        {
            if (exceptions.Contains(puzzle.Id, StringComparer.Ordinal)) continue;
            Assert.True(MirrorOverlap(puzzle, horizontal: true) < 0.85, $"{puzzle.Id}: left/right overlap >=85%.");
            Assert.True(MirrorOverlap(puzzle, horizontal: false) < 0.85, $"{puzzle.Id}: top/bottom overlap >=85%.");
        }
    }

    private static double MirrorOverlap(Puzzle puzzle, bool horizontal)
    {
        var intersection = 0;
        var union = 0;
        for (var y = 0; y < puzzle.Height; y++)
        {
            for (var x = 0; x < puzzle.Width; x++)
            {
                var a = puzzle.IsFilled(x, y);
                var b = horizontal ? puzzle.IsFilled(puzzle.Width - x - 1, y) : puzzle.IsFilled(x, puzzle.Height - y - 1);
                if (a && b) intersection++;
                if (a || b) union++;
            }
        }

        return union == 0 ? 1 : (double)intersection / union;
    }
}
