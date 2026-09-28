using System.Text.Json.Serialization;

namespace Squarebuzz.Core.Content;

/// <summary>
/// Wire shape of <c>Content/puzzles.json</c>. Kept separate from the domain
/// <see cref="Model.Puzzle"/> so the file format can change without disturbing game logic.
/// </summary>
internal sealed class PuzzleContentDto
{
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; init; }

    [JsonPropertyName("packs")]
    public IReadOnlyList<PackDto> Packs { get; init; } = [];

    [JsonPropertyName("puzzles")]
    public IReadOnlyList<PuzzleDto> Puzzles { get; init; } = [];

    [JsonPropertyName("archivedPuzzles")]
    public IReadOnlyList<PuzzleDto> ArchivedPuzzles { get; init; } = [];
}

internal sealed class PackDto
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("icon")]
    public string Icon { get; init; } = string.Empty;

    [JsonPropertyName("locked")]
    public bool Locked { get; init; }

    [JsonPropertyName("isWildcard")]
    public bool IsWildcard { get; init; }
}

internal sealed class PuzzleDto
{
    [JsonPropertyName("revision")]
    public int Revision { get; init; } = 1;

    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("pack")]
    public string Pack { get; init; } = string.Empty;

    [JsonPropertyName("width")]
    public int Width { get; init; }

    [JsonPropertyName("height")]
    public int Height { get; init; }

    [JsonPropertyName("color")]
    public string Color { get; init; } = string.Empty;

    [JsonPropertyName("rows")]
    public IReadOnlyList<string> Rows { get; init; } = [];
}

/// <summary>
/// Source-generated serialisation context. Reflection-based deserialisation can be stripped
/// by the iOS/Android trimmer, so the reader goes through generated code instead.
/// </summary>
[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(PuzzleContentDto))]
internal sealed partial class PuzzleContentSerializerContext : JsonSerializerContext;
