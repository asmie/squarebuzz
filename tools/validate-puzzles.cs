// Validates the authored puzzle content, as an authoring aid.
//
//   dotnet run tools/validate-puzzles.cs [path-to-puzzles.json]
//
// The xUnit content tests remain the CI gate; this tool exists for the edit-validate loop while
// drawing new pictures, because it can show WHERE a puzzle is ambiguous, which a red test cannot.
// It reads the JSON from disk - not the embedded copy - so there is no rebuild between edits.
//
// For every puzzle it re-checks the data rules the tests enforce (dimensions, characters, unique
// ids, declared pack, at most two fully-empty rows and columns per axis), then runs the real
// solver. A puzzle that needs guessing gets its partial solve printed with '?' marking the cells
// line logic could not reach - the exact region the author has to disambiguate. The Passes
// column doubles as the difficulty metric used to order the content file.

#:project ../src/Squarebuzz.Core/Squarebuzz.Core.csproj

using System.Text.Json;
using Squarebuzz.Core.Model;
using Squarebuzz.Core.Solving;

var path = args.Length > 0 ? args[0] : Path.Combine("src", "Squarebuzz.Core", "Content", "puzzles.json");

if (!File.Exists(path))
{
    Console.Error.WriteLine($"Content file not found: {path} (run from the repository root, or pass a path).");
    return 1;
}

using var document = JsonDocument.Parse(File.ReadAllText(path));
var root = document.RootElement;

var packIds = new List<string>();
foreach (var pack in root.GetProperty("packs").EnumerateArray())
{
    packIds.Add(pack.GetProperty("id").GetString() ?? string.Empty);
}

var failures = 0;
var seenIds = new HashSet<string>(StringComparer.Ordinal);
var bySize = new Dictionary<int, int>();
var byPack = new Dictionary<string, int>();
var report = new List<(string Id, string Pack, int Size, string Outcome, int Passes, int Undetermined)>();

void Fail(string id, string message)
{
    failures++;
    Console.Error.WriteLine($"FAIL {id}: {message}");
}

foreach (var element in root.GetProperty("puzzles").EnumerateArray())
{
    var id = element.GetProperty("id").GetString() ?? string.Empty;
    var pack = element.GetProperty("pack").GetString() ?? string.Empty;
    var width = element.GetProperty("width").GetInt32();
    var height = element.GetProperty("height").GetInt32();
    var color = element.GetProperty("color").GetString() ?? string.Empty;

    var rows = new List<string>();
    foreach (var row in element.GetProperty("rows").EnumerateArray())
    {
        rows.Add(row.GetString() ?? string.Empty);
    }

    // The data rules PuzzleContentTests pins, re-checked here so authoring fails fast.
    if (!seenIds.Add(id))
    {
        Fail(id, "duplicate id.");
    }

    if (!packIds.Contains(pack))
    {
        Fail(id, $"pack '{pack}' is not declared in packs[].");
    }

    if (rows.Count != height || rows.Exists(r => r.Length != width))
    {
        Fail(id, $"grid is not {width}x{height}.");
        continue;
    }

    if (rows.Exists(r => r.AsSpan().IndexOfAnyExcept('#', '.') >= 0))
    {
        Fail(id, "rows may only contain '#' and '.'.");
        continue;
    }

    var emptyRows = rows.Count(r => !r.Contains('#', StringComparison.Ordinal));
    var emptyColumns = 0;
    for (var x = 0; x < width; x++)
    {
        var any = false;
        for (var y = 0; y < height; y++)
        {
            any |= rows[y][x] == '#';
        }

        if (!any)
        {
            emptyColumns++;
        }
    }

    if (emptyRows > 2 || emptyColumns > 2)
    {
        Fail(id, $"{emptyRows} empty rows / {emptyColumns} empty columns; at most 2 per axis are allowed.");
    }

    // The real gate: pure line logic must reach the exact picture, no guessing.
    var puzzle = Puzzle.FromRows(id, pack, color, rows);
    var analysis = PuzzleSolver.Analyse(puzzle);

    report.Add((id, pack, width, analysis.Outcome.ToString(), analysis.Passes, analysis.UndeterminedCells));
    bySize[width] = bySize.GetValueOrDefault(width) + 1;
    byPack[pack] = byPack.GetValueOrDefault(pack) + 1;

    if (analysis.IsSolvable)
    {
        continue;
    }

    Fail(id, $"{analysis.Outcome} after {analysis.Passes} passes, {analysis.UndeterminedCells} cells undetermined.");

    // Show the author exactly which region is ambiguous: '#' deduced filled, '.' deduced
    // empty, '?' unreachable by line logic. Fixes are always local to the '?' cells.
    var board = new CellState[puzzle.CellCount];
    PuzzleSolver.Solve(puzzle, board);

    for (var y = 0; y < height; y++)
    {
        var line = new char[width];
        for (var x = 0; x < width; x++)
        {
            line[x] = board[(y * width) + x] switch
            {
                CellState.Filled => '#',
                CellState.Crossed => '.',
                _ => '?',
            };
        }

        Console.Error.WriteLine($"       {new string(line)}");
    }
}

Console.WriteLine();
Console.WriteLine($"{"id",-12} {"pack",-10} {"size",-6} {"outcome",-14} {"passes",6} {"undet.",6}");

foreach (var row in report)
{
    Console.WriteLine($"{row.Id,-12} {row.Pack,-10} {$"{row.Size}x{row.Size}",-6} {row.Outcome,-14} {row.Passes,6} {row.Undetermined,6}");
}

Console.WriteLine();
Console.WriteLine("Per size: " + string.Join(", ", bySize.OrderBy(p => p.Key).Select(p => $"{p.Key}x{p.Key}: {p.Value}")));
Console.WriteLine("Per pack: " + string.Join(", ", byPack.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => $"{p.Key}: {p.Value}")));
Console.WriteLine($"Total: {report.Count} puzzles, {packIds.Count} packs.");

if (failures > 0)
{
    Console.Error.WriteLine($"{failures} failure(s).");
    return 1;
}

Console.WriteLine("All puzzles pass.");
return 0;
