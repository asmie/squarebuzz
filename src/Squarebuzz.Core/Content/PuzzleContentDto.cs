using System.Text.Json.Serialization;

namespace Squarebuzz.Core.Content;

/// <summary>
/// Wire shape of <c>Content/puzzles.json</c>. Kept separate from the domain
/// <see cref="Model.Puzzle"/> so the file format can change without disturbing game logic.
/// Setters let the generated deserializer preserve defaults for omitted JSON properties;
/// init-only properties are assigned even when absent, overwriting their initializers.
/// </summary>
internal sealed class PuzzleContentDto
{
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; set; }

    [JsonPropertyName("packs")]
    public IReadOnlyList<PackDto> Packs { get; set; } = [];

    [JsonPropertyName("puzzles")]
    public IReadOnlyList<PuzzleDto> Puzzles { get; set; } = [];

    [JsonPropertyName("archivedPuzzles")]
    public IReadOnlyList<PuzzleDto> ArchivedPuzzles { get; set; } = [];
}

internal sealed class PackDto
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("icon")]
    public string Icon { get; set; } = string.Empty;

    [JsonPropertyName("locked")]
    public bool Locked { get; set; }

    [JsonPropertyName("isWildcard")]
    public bool IsWildcard { get; set; }
}

internal sealed class PuzzleDto
{
    [JsonPropertyName("revision")]
    public int Revision { get; set; } = 1;

    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("pack")]
    public string Pack { get; set; } = string.Empty;

    [JsonPropertyName("width")]
    public int Width { get; set; }

    [JsonPropertyName("height")]
    public int Height { get; set; }

    [JsonPropertyName("color")]
    public string Color { get; set; } = string.Empty;

    [JsonPropertyName("rows")]
    public IReadOnlyList<string> Rows { get; set; } = [];
}

/// <summary>
/// Source-generated serialisation context. Reflection-based deserialisation can be stripped
/// by the iOS/Android trimmer, so the reader goes through generated code instead.
/// </summary>
[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(PuzzleContentDto))]
internal sealed partial class PuzzleContentSerializerContext : JsonSerializerContext;
