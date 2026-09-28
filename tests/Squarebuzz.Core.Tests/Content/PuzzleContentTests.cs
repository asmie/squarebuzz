using System.Reflection;
using System.Text.Json;
using Squarebuzz.Core.Model;
using Xunit;

namespace Squarebuzz.Core.Tests.Content;

/// <summary>
/// Checks authored grid structure, revision lookup and embedded-resource wiring.
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
    // Declared dimensions must match the rows from which Puzzle derives its actual size.
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

                // Reject unknown characters before the loader treats them as empty cells.
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
    public void EveryArchivedRevision_ResolvesExactlyAndIsExcludedFromNewGames()
    {
        using var doc = LoadContent();
        var repository = new Core.Content.EmbeddedPuzzleRepository();
        foreach (var entry in doc.RootElement.GetProperty("archivedPuzzles").EnumerateArray())
        {
            var id = entry.GetProperty("id").GetString()!;
            var revision = entry.GetProperty("revision").GetInt32();
            var puzzle = repository.FindById(id, revision);
            Assert.NotNull(puzzle);
            var expected = string.Concat(entry.GetProperty("rows").EnumerateArray().Select(row => row.GetString()))
                .Select(cell => cell == '#').ToArray();
            Assert.Equal(expected, puzzle.Solution.ToArray());
            Assert.DoesNotContain(puzzle, repository.Puzzles);
            Assert.DoesNotContain(puzzle, repository.Find(puzzle.Pack, puzzle.Width));
        }
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
    public void EveryPuzzle_HasAtMostTwoEmptyRowsAndColumns()
    {
        using var doc = LoadContent();

        foreach (var puzzle in doc.RootElement.GetProperty("puzzles").EnumerateArray())
        {
            var id = puzzle.GetProperty("id").GetString();
            var rows = puzzle.GetProperty("rows").EnumerateArray().Select(r => r.GetString()!).ToList();
            var width = puzzle.GetProperty("width").GetInt32();

            var emptyRows = rows.Count(r => !r.Contains('#'));
            var emptyColumns = Enumerable.Range(0, width).Count(x => rows.All(r => r[x] != '#'));

            // Allow limited empty space around the silhouette.
            Assert.True(emptyRows <= 2, $"Puzzle '{id}' has {emptyRows} completely empty rows.");
            Assert.True(emptyColumns <= 2, $"Puzzle '{id}' has {emptyColumns} completely empty columns.");
        }
    }
}
