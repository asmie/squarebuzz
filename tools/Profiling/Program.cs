using System.Diagnostics;
using Squarebuzz.Core.Generation;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Squarebuzz.App.Controls;
using Squarebuzz.App.Drawing;
using Squarebuzz.Core.Clues;
using Squarebuzz.Core.Model;
using Squarebuzz.Profiling;

if (args.Length == 1 && args[0] == "--help")
{
    Console.WriteLine("From the repository root: dotnet run --project tools/Profiling -c Release -- [--output DIRECTORY]");
    return 0;
}
if (args.Length != 0 && (args.Length != 2 || args[0] != "--output"))
{
    Console.Error.WriteLine("Expected --output DIRECTORY or --help.");
    return 1;
}
#if DEBUG
Console.Error.WriteLine("Profiling requires -c Release.");
return 1;
#else
if (!File.Exists("src/Squarebuzz.App/Resources/Themes/Light.xaml"))
{
    Console.Error.WriteLine("Run from the repository root.");
    return 1;
}
var output = Path.GetFullPath(args.Length == 2 ? args[1] : "artifacts/profiling");
Directory.CreateDirectory(output);
var reportPath = Path.Combine(output, $"host-{DateTime.UtcNow:yyyyMMddTHHmmssfff}-{Guid.NewGuid():N}.json");
var results = new List<Measurement>();
var manifest = Directory.EnumerateFiles(AppContext.BaseDirectory, "*.dll").Order(StringComparer.Ordinal)
    .ToDictionary(file => Path.GetFileName(file)!, file => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file))));
var report = new HostReport(DateTimeOffset.UtcNow, RuntimeInformation.FrameworkDescription,
    RuntimeInformation.OSDescription, RuntimeInformation.ProcessArchitecture.ToString(), Environment.ProcessorCount, manifest, results);
try
{
    using var fixture = new HostFixture();
    foreach (var size in new[] { 5, 15, 25 })
    {
        var session = fixture.Factory.Create(NewGameOptions.Default with { Size = size, ForceGenerated = true, Seed = 42 });
        var board = new BoardView { AvailableSize = new Size(360, 600), Session = session };
        var mini = new BoardMiniMapView();
        var magnifier = new MagnifierView { Session = session };
        Add($"P1 board refresh {size}", "Production RefreshCells; no handler, draw or GPU.",
            () => { board.RefreshCells(); return session.Puzzle.CellCount; });
        Add($"P1 minimap refresh {size}", "Production UpdateCells; no draw or GPU.",
            () => { mini.UpdateCells(session); return session.Puzzle.CellCount; });
        Add($"P1 magnifier refresh {size}", "Production ShowCell including palette lookup; no draw.",
            () => { magnifier.ShowCell(size * size / 2); return session.Puzzle.CellCount; });
        var marks = session.Cells.ToArray();
        Add($"P3 clue strikes empty {size}", "All rows/columns including column gathering; excludes drawing strings/pixels.",
            () => ComputeStrikes(session.Puzzle, marks));
        for (var i = 0; i < marks.Length; i++)
            marks[i] = i % 3 == 0 ? CellState.Filled : i % 3 == 1 ? CellState.Crossed : CellState.Empty;
        Add($"P3 clue strikes mixed {size}", "Same full clue pass with deterministic mixed marks.",
            () => ComputeStrikes(session.Puzzle, marks));
    }
    Add("P3 palette", "Production FromResources against merged Light/Tangerine colors; excludes OS motion reads.",
        () => { GC.KeepAlive(BoardPalette.FromResources()); return 11; });
    fixture.StartAccessibleGame();
    var buttons = Enumerable.Range(0, 625).Select(_ => new Button()).ToArray();
    Add("P2 descriptions 25", "625 production DescribeCell calls, real English resources; no semantic setters/native services.", () =>
    {
        var length = 0;
        for (var i = 0; i < buttons.Length; i++) length += fixture.Game.DescribeCell(i).Length;
        return length;
    });
    Add("P2 descriptions and managed setters 25", "Page-equivalent loop over 625 managed Buttons, unchanged descriptions. No Android handlers/TalkBack.", () =>
    {
        for (var i = 0; i < buttons.Length; i++) SemanticProperties.SetDescription(buttons[i], fixture.Game.DescribeCell(i));
        return buttons.Length;
    });
    foreach (var size in new[] { 15, 20, 25 })
    foreach (var difficulty in new[] { 1, 3, 5 })
    {
        var sequence = 0;
        var requests = Enumerable.Range(0, 20).Select(seed => new PuzzleRequest(size, size, difficulty, "animals", seed)).ToArray();
        Add($"P4 generation {size} difficulty {difficulty}", "Production unique/blob generator, rotating seeds 0..19; excludes navigation/layout.", () =>
        {
            var puzzle = fixture.Generator.Generate(requests[sequence]);
            sequence = (sequence + 1) % requests.Length;
            return puzzle.PictureCellCount;
        }, minimumBatch: 20);
    }
    // SQLite retains at most 12 saves. The 50-save fake bypasses that policy for stress only.
    foreach (var count in new[] { 1, 10, 12, 50 })
    {
        fixture.PrepareSaves(count);
        var scope = count > 12 ? "Out-of-range stress; exceeds production's 12-save cap. " : string.Empty;
        Add($"P4 Continue {count} generated saves", scope + "Production OnAppearingAsync/BuildCard; 25x25 difficulty 3 seeds 0..count-1. In-memory repository; no SQLite/native list layout.", () =>
        {
            HostFixture.CompleteSynchronously(fixture.Continue.OnAppearingAsync());
            if (fixture.Continue.Saves.Count != count || fixture.Continue.HasPersistenceFailure)
                throw new InvalidOperationException("Continue did not rebuild every fixture save.");
            return fixture.Continue.Saves.Count;
        });
    }
    report.Status = "completed";
}
catch (Exception exception)
{
    report.Status = "failed";
    report.Error = exception.ToString();
    Console.Error.WriteLine(exception);
}
finally
{
    File.WriteAllText(reportPath, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
    Console.WriteLine($"Host profiling {report.Status}: {reportPath}");
}
return report.Status == "completed" ? 0 : 1;

void Add(string name, string scope, Func<int> action, int minimumBatch = 1)
{
    var result = Measurement.Run(name, scope, action, minimumBatch);
    results.Add(result);
    Console.WriteLine(FormattableString.Invariant($"{name}: {result.MedianMilliseconds:F4} ms/op; {result.MedianBytes:F0} B/op"));
}

static int ComputeStrikes(Puzzle puzzle, CellState[] marks)
{
    Span<bool> struck = stackalloc bool[Math.Max(4, Math.Max(puzzle.MaxRowClueCount, puzzle.MaxColumnClueCount))];
    Span<CellState> column = stackalloc CellState[puzzle.Height];
    var count = 0;
    for (var row = 0; row < puzzle.Height; row++)
    {
        ClueStrikeCalculator.Compute(puzzle.RowClues[row], marks.AsSpan(row * puzzle.Width, puzzle.Width), struck);
        if (struck[0]) count++;
    }
    for (var x = 0; x < puzzle.Width; x++)
    {
        for (var y = 0; y < puzzle.Height; y++) column[y] = marks[y * puzzle.Width + x];
        ClueStrikeCalculator.Compute(puzzle.ColumnClues[x], column, struck);
        if (struck[0]) count++;
    }
    return count;
}
#endif

internal sealed record Sample(double MillisecondsPerOperation, double BytesPerOperation, int Gen0Collections, int Gen1Collections, int Gen2Collections);

internal sealed record Measurement(string Name, string Scope, int OperationsPerBatch, Sample[] Samples)
{
    public double MedianMilliseconds => Samples.Select(s => s.MillisecondsPerOperation).Order().ElementAt(Samples.Length / 2);
    public double MedianBytes => Samples.Select(s => s.BytesPerOperation).Order().ElementAt(Samples.Length / 2);
    public static Measurement Run(string name, string scope, Func<int> action, int minimumBatch)
    {
        // Whole generation corpora per batch; no forced GC inside samples.
        var batch = minimumBatch;
        var checksum = 0;
        for (var warm = 0; warm < 3; warm++) checksum ^= action();
        while (true)
        {
            var start = Stopwatch.GetTimestamp();
            for (var i = 0; i < batch; i++) checksum ^= action();
            if (Stopwatch.GetElapsedTime(start).TotalMilliseconds >= 10 || batch >= 1_048_576) break;
            batch *= 2;
        }
        for (var warm = 0; warm < 5; warm++)
            for (var i = 0; i < batch; i++) checksum ^= action();
        var samples = new Sample[9];
        for (var sample = 0; sample < samples.Length; sample++)
        {
            var g0 = GC.CollectionCount(0);
            var g1 = GC.CollectionCount(1);
            var g2 = GC.CollectionCount(2);
            var bytes = GC.GetAllocatedBytesForCurrentThread();
            var start = Stopwatch.GetTimestamp();
            for (var i = 0; i < batch; i++) checksum ^= action();
            var elapsed = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            var allocated = GC.GetAllocatedBytesForCurrentThread() - bytes;
            samples[sample] = new Sample(elapsed / batch, allocated / (double)batch,
                GC.CollectionCount(0) - g0, GC.CollectionCount(1) - g1, GC.CollectionCount(2) - g2);
        }
        GC.KeepAlive(checksum);
        return new Measurement(name, scope, batch, samples);
    }
}

internal sealed record HostReport(DateTimeOffset StartedUtc, string Runtime, string OperatingSystem,
    string Architecture, int ProcessorCount, Dictionary<string, string> AssemblySha256, List<Measurement> Measurements)
{
    public string Status { get; set; } = "running";
    public string? Error { get; set; }
    public string Limitations { get; } = "Host Release, tiered compilation disabled, managed code only. English, Light/Tangerine. No native handlers, rendering, frame times, screen reader, SQLite, OS motion query or cold startup. Batch averages are not per-interaction tail latencies.";
}
