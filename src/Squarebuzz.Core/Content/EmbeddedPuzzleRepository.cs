using System.Reflection;
using System.Text.Json;
using Squarebuzz.Core.Abstractions;
using Squarebuzz.Core.Model;

namespace Squarebuzz.Core.Content;

/// <summary>
/// Reads the authored pictures from the <c>puzzles.json</c> resource embedded in this
/// assembly. Content ships with the code, so there is no file to lose and nothing to
/// download - the game works offline from first launch.
/// </summary>
public sealed class EmbeddedPuzzleRepository : IPuzzleRepository
{
    internal const string ResourceName = "Squarebuzz.Core.Content.puzzles.json";

    private readonly Dictionary<string, Puzzle> _byId;
    private readonly Dictionary<(string Id, int Revision), Puzzle> _byRevision;

    public EmbeddedPuzzleRepository()
        : this(ReadEmbeddedContent())
    {
    }

    /// <summary>Testing seam: load content from an arbitrary JSON string.</summary>
    internal EmbeddedPuzzleRepository(string json)
    {
        var content = JsonSerializer.Deserialize(json, PuzzleContentSerializerContext.Default.PuzzleContentDto)
                      ?? throw new InvalidOperationException("Puzzle content deserialised to null.");

        Packs = [.. content.Packs.Select(p => new PackDefinition(p.Id, p.Icon, p.Locked, p.IsWildcard))];

        Puzzles = [.. content.Puzzles.Select(ToPuzzle)];

        _byId = Puzzles.ToDictionary(p => p.Id, StringComparer.Ordinal);

        var allRevisions = Puzzles.Concat(content.ArchivedPuzzles.Select(ToPuzzle)).ToList();
        _byRevision = allRevisions.ToDictionary(p => (p.Id, p.Revision));

        var declaredPacks = Packs.Select(p => p.Id).ToHashSet(StringComparer.Ordinal);
        var orphan = allRevisions.FirstOrDefault(p => !declaredPacks.Contains(p.Pack));

        if (orphan is not null)
        {
            throw new InvalidOperationException(
                $"Puzzle '{orphan.Id}' references pack '{orphan.Pack}', which is not declared in the content file.");
        }
    }

    public IReadOnlyList<PackDefinition> Packs { get; }

    public IReadOnlyList<Puzzle> Puzzles { get; }

    public IReadOnlyList<Puzzle> Find(string packId, int size, IReadOnlySet<string>? unlockedPackIds = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packId);

        var pack = Packs.FirstOrDefault(p => string.Equals(p.Id, packId, StringComparison.Ordinal));

        // Surprise includes unlocked packs, including those earned through campaign milestones.
        if (pack?.IsWildcard == true)
        {
            var unlocked = Packs
                .Where(p => !p.IsWildcard && (!p.Locked || unlockedPackIds?.Contains(p.Id) == true))
                .Select(p => p.Id)
                .ToHashSet(StringComparer.Ordinal);

            return [.. Puzzles.Where(p => p.Width == size && p.Height == size && unlocked.Contains(p.Pack))];
        }

        return [.. Puzzles.Where(p => p.Width == size && p.Height == size && string.Equals(p.Pack, packId, StringComparison.Ordinal))];
    }

    public Puzzle? FindById(string puzzleId, int? revision = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(puzzleId);

        return revision is { } requested
            ? _byRevision.GetValueOrDefault((puzzleId, requested))
            : _byId.GetValueOrDefault(puzzleId);
    }

    /// <summary>Builds a puzzle and checks that declared dimensions match its rows.</summary>
    private static Puzzle ToPuzzle(PuzzleDto dto)
    {
        var puzzle = Puzzle.FromRows(dto.Id, dto.Pack, dto.Color, dto.Rows, revision: dto.Revision);

        if (dto.Width != puzzle.Width || dto.Height != puzzle.Height)
        {
            throw new InvalidOperationException(
                $"Puzzle '{dto.Id}' declares {dto.Width}x{dto.Height} but its rows are {puzzle.Width}x{puzzle.Height}.");
        }

        return puzzle;
    }

    private static string ReadEmbeddedContent()
    {
        var assembly = Assembly.GetExecutingAssembly();

        using var stream = assembly.GetManifestResourceStream(ResourceName)
                           ?? throw new InvalidOperationException(
                               $"Embedded resource '{ResourceName}' is missing. Check the EmbeddedResource item in Squarebuzz.Core.csproj.");

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
