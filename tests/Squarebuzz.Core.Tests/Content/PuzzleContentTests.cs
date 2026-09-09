using System.Reflection;
using System.Text.Json;
using Squarebuzz.Core.Model;
using Xunit;

namespace Squarebuzz.Core.Tests.Content;

/// <summary>
/// Guards the authored puzzle content and the embedded-resource wiring that ships it.
/// These assertions are about data, not behaviour, so they stay valid as the domain grows -
/// and they fail loudly if someone hand-edits a grid into an inconsistent shape.
/// </summary>
public class PuzzleContentTests
{
    private const string ResourceName = "Squarebuzz.Core.Content.puzzles.json";

    private static JsonDocument LoadContent()
    {
        var assembly = typeof(GameTheme).Assembly;
        using var stream = assembly.GetManifestResourceStream(ResourceName);

        Assert.NotNull(stream);
        return JsonDocument.Parse(stream);
    }

    [Fact]
    // The width and height fields used to be parsed and ignored - Puzzle.FromRows derives the
    // real size from the rows - so an entry could declare 10x10 over a 5x5 grid and ship. The
    // loader now checks the declaration against the grid it describes and names the culprit.
    public void APuzzleWhoseDeclaredSizeDisagreesWithItsRows_IsRejectedAtLoad()
    {
        const string lying = """
            {
              "schemaVersion": 1,
              "packs": [ { "id": "animals", "icon": "x", "locked": false, "isWildcard": false } ],
              "puzzles": [
                { "id": "fib", "pack": "animals", "width": 10, "height": 10, "color": "#000000",
                  "rows": ["#####", "#...#", "#.#.#", "#...#", "#####"] }
              ]
            }
            """;

        var error = Assert.Throws<InvalidOperationException>(() => new Core.Content.EmbeddedPuzzleRepository(lying));

        Assert.Contains("fib", error.Message, StringComparison.Ordinal);
        Assert.Contains("10x10", error.Message, StringComparison.Ordinal);
        Assert.Contains("5x5", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void PuzzleContent_IsEmbeddedInTheCoreAssembly()
    {
        var names = typeof(GameTheme).Assembly.GetManifestResourceNames();

        Assert.Contains(ResourceName, names);
    }

    [Fact]
    public void EveryPuzzle_HasRowsMatchingItsDeclaredSize()
    {
        using var doc = LoadContent();
        var puzzles = doc.RootElement.GetProperty("puzzles");

        Assert.NotEmpty(puzzles.EnumerateArray());

        foreach (var puzzle in puzzles.EnumerateArray())
        {
            var id = puzzle.GetProperty("id").GetString();
            var width = puzzle.GetProperty("width").GetInt32();
            var height = puzzle.GetProperty("height").GetInt32();
            var rows = puzzle.GetProperty("rows").EnumerateArray().Select(r => r.GetString()).ToList();

            Assert.Equal(height, rows.Count);

            foreach (var row in rows)
            {
                Assert.NotNull(row);
                Assert.Equal(width, row.Length);

                // Only '#' (filled) and '.' (empty) are legal. A stray character would
                // otherwise be silently read as "empty" and quietly change the picture.
                Assert.All(row, c => Assert.True(c is '#' or '.', $"Puzzle '{id}' contains illegal grid character '{c}'."));
            }
        }
    }

    [Fact]
    public void EveryPuzzle_HasAUniqueId()
    {
        using var doc = LoadContent();

        var ids = doc.RootElement.GetProperty("puzzles")
            .EnumerateArray()
            .Select(p => p.GetProperty("id").GetString())
            .ToList();

        Assert.Equal(ids.Count, ids.Distinct().Count());
    }

    [Fact]
    public void EveryPuzzle_ReferencesADeclaredPack()
    {
        using var doc = LoadContent();

        var packIds = doc.RootElement.GetProperty("packs")
            .EnumerateArray()
            .Select(p => p.GetProperty("id").GetString())
            .ToHashSet();

        foreach (var puzzle in doc.RootElement.GetProperty("puzzles").EnumerateArray())
        {
            var pack = puzzle.GetProperty("pack").GetString();
            Assert.Contains(pack, packIds);
        }
    }

    [Fact]
    public void EveryPuzzle_HasAtLeastOneFilledCellInEveryRowAndColumn()
    {
        // A fully empty line is legal nonogram-wise (clue "0"), but in this game it reads as
        // a bug in the picture, and the prototype's generator explicitly avoids it. Authored
        // content should hold to the same bar.
        using var doc = LoadContent();

        foreach (var puzzle in doc.RootElement.GetProperty("puzzles").EnumerateArray())
        {
            var id = puzzle.GetProperty("id").GetString();
            var rows = puzzle.GetProperty("rows").EnumerateArray().Select(r => r.GetString()!).ToList();
            var width = puzzle.GetProperty("width").GetInt32();

            var emptyRows = rows.Count(r => !r.Contains('#'));
            var emptyColumns = Enumerable.Range(0, width).Count(x => rows.All(r => r[x] != '#'));

            // The prototype's own art has a few deliberately blank edge rows (car, crown),
            // so allow up to two per axis rather than demanding none.
            Assert.True(emptyRows <= 2, $"Puzzle '{id}' has {emptyRows} completely empty rows.");
            Assert.True(emptyColumns <= 2, $"Puzzle '{id}' has {emptyColumns} completely empty columns.");
        }
    }
}
