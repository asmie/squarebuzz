// dotnet run tools/validate-puzzles.cs [path-to-puzzles.json] [--suggest]
// Reads content from disk, validates revisions and grids, and runs the production line solver.
// --suggest lists single-cell changes that make an unresolved board solvable. Review their
// appearance before applying them. Archived boards are checked for structure, not solvability.

#:project ../src/Squarebuzz.Core/Squarebuzz.Core.csproj

using System.Text.Json;
using Squarebuzz.Core.Model;
using Squarebuzz.Core.Solving;

var suggest = args.Contains("--suggest");
var path = args.FirstOrDefault(a => !a.StartsWith("--", StringComparison.Ordinal))
    ?? Path.Combine("src", "Squarebuzz.Core", "Content", "puzzles.json");

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

var revisions = new HashSet<(string Id, int Revision)>();
var currentEntries = root.GetProperty("puzzles").EnumerateArray().ToArray();
var archives = root.TryGetProperty("archivedPuzzles", out var archived)
    ? archived.EnumerateArray().ToArray() : [];
foreach (var entry in currentEntries.Concat(archives))
{
    var id = entry.GetProperty("id").GetString() ?? string.Empty;
    var revision = entry.TryGetProperty("revision", out var version) ? version.GetInt32() : 1;
    if (revision < 1 || !revisions.Add((id, revision)))
    {
        Fail(id, $"invalid or duplicate revision {revision}.");
    }
}

foreach (var entry in currentEntries)
{
    var id = entry.GetProperty("id").GetString() ?? string.Empty;
    var revision = entry.TryGetProperty("revision", out var version) ? version.GetInt32() : 1;
    for (var previous = 1; previous < revision; previous++)
    {
        if (!revisions.Contains((id, previous))) Fail(id, $"missing archived revision {previous}.");
    }
}

foreach (var entry in archives)
{
    var id = entry.GetProperty("id").GetString() ?? string.Empty;
    var rows = entry.GetProperty("rows").EnumerateArray().Select(row => row.GetString() ?? string.Empty).ToArray();
    var width = entry.GetProperty("width").GetInt32();
    var height = entry.GetProperty("height").GetInt32();
    if (width <= 0 || height <= 0 || rows.Length != height
        || rows.Any(row => row.Length != width || row.AsSpan().IndexOfAnyExcept('#', '.') >= 0)
        || !packIds.Contains(entry.GetProperty("pack").GetString() ?? string.Empty))
    {
        Fail(id, "invalid archived grid or pack.");
    }
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

    // Check grid structure before invoking the solver.
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

    // Report symmetry and density for visual review; these do not fail validation.
    var filledCells = rows.Sum(r => r.Count(c => c == '#'));
    var mirrored = rows.All(r => r.SequenceEqual(r.Reverse()));

    if (mirrored && width > 5)
    {
        Console.WriteLine($"note {id}: an exact left-right mirror image - the player can solve one half and copy it.");
    }

    if (width == 10 && (filledCells < 45 || filledCells > 65))
    {
        Console.WriteLine($"note {id}: {filledCells}% filled; 10x10 pictures target 45-65%.");
    }

    // Line logic must resolve every cell without guessing.
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

    // Show deductions: '#' filled, '.' empty, '?' unresolved by line logic.
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

    if (suggest)
    {
        Suggest(id, pack, color, rows, board, width, height);
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

// Try single-cell flips in unresolved areas and their neighbours; print up to eight
// candidates that the line solver can finish.
static void Suggest(string id, string pack, string color, List<string> rows, CellState[] board, int width, int height)
{
    var candidates = new List<int>();
    var seen = new HashSet<int>();

    void Add(int x, int y)
    {
        if (x >= 0 && x < width && y >= 0 && y < height && seen.Add((y * width) + x))
        {
            candidates.Add((y * width) + x);
        }
    }

    for (var i = 0; i < board.Length; i++)
    {
        if (board[i] == CellState.Empty)
        {
            Add(i % width, i / width);
        }
    }

    foreach (var i in candidates.ToArray())
    {
        var (x, y) = (i % width, i / width);
        Add(x - 1, y);
        Add(x + 1, y);
        Add(x, y - 1);
        Add(x, y + 1);
    }

    var found = 0;

    foreach (var index in candidates)
    {
        var (x, y) = (index % width, index / width);
        var flipped = rows.ToList();
        var chars = flipped[y].ToCharArray();
        chars[x] = chars[x] == '#' ? '.' : '#';
        flipped[y] = new string(chars);

        if (!PuzzleSolver.Analyse(Puzzle.FromRows(id, pack, color, flipped)).IsSolvable)
        {
            continue;
        }

        Console.Error.WriteLine($"       fix: {(chars[x] == '#' ? "fill" : "clear")} row {y + 1}, column {x + 1}");

        if (++found == 8)
        {
            break;
        }
    }

    if (found == 0)
    {
        Console.Error.WriteLine("       no single-square fix; the ambiguous region needs redrawing.");
    }
}
