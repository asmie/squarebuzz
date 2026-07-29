using SQLite;

namespace Squarebuzz.Data.Entities;

/// <summary>
/// Tracks which migrations have run. A single row per applied version, so the runner can tell
/// a fresh database from a partly-upgraded one.
/// </summary>
[Table("schema_version")]
internal sealed class SchemaVersionEntity
{
    [PrimaryKey]
    [Column("version")]
    public int Version { get; set; }

    [Column("applied_at")]
    public DateTime AppliedAtUtc { get; set; }
}

/// <summary>
/// Settings as key/value rows rather than one wide table.
/// </summary>
/// <remarks>
/// This game gains a new toggle almost every time a screen lands, and key/value absorbs that
/// without a schema migration per setting. Unknown keys are ignored on read and missing keys
/// fall back to the default, so old and new builds can share a database.
/// </remarks>
[Table("setting")]
internal sealed class SettingEntity
{
    [PrimaryKey]
    [Column("key")]
    public string Key { get; set; } = string.Empty;

    [Column("value")]
    public string? Value { get; set; }
}

/// <summary>An unfinished puzzle.</summary>
[Table("saved_game")]
internal sealed class SavedGameEntity
{
    [PrimaryKey]
    [Column("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>Null when the puzzle was generated and must be rebuilt from the seed.</summary>
    [Column("puzzle_id")]
    public string? PuzzleId { get; set; }

    [Column("size")]
    public int Size { get; set; }

    [Column("difficulty")]
    public int Difficulty { get; set; }

    [Column("pack_id")]
    public string PackId { get; set; } = string.Empty;

    [Column("seed")]
    public int Seed { get; set; }

    [Column("challenge")]
    public int Challenge { get; set; }

    /// <summary>
    /// The board, one byte per cell. CellState is a byte enum, so a 25x25 board is 625 bytes -
    /// small enough to store raw and far cheaper than a row per cell.
    /// </summary>
    [Column("cells")]
    public byte[] Cells { get; set; } = [];

    [Column("elapsed_seconds")]
    public double ElapsedSeconds { get; set; }

    [Column("hints_remaining")]
    public int HintsRemaining { get; set; }

    [Column("mistakes")]
    public int Mistakes { get; set; }

    /// <summary>Stored as UTC ticks so ordering is stable regardless of the device's time zone.</summary>
    [Indexed(Name = "ix_saved_game_saved_at", Order = 1)]
    [Column("saved_at_ticks")]
    public long SavedAtUtcTicks { get; set; }

    [Column("saved_at_offset_ticks")]
    public long SavedAtOffsetTicks { get; set; }
}

/// <summary>The player's standing. Exactly one row, pinned to <see cref="SingletonId"/>.</summary>
[Table("progress")]
internal sealed class ProgressEntity
{
    public const int SingletonId = 1;

    [PrimaryKey]
    [Column("id")]
    public int Id { get; set; } = SingletonId;

    [Column("player_name")]
    public string PlayerName { get; set; } = string.Empty;

    [Column("stars")]
    public int Stars { get; set; }

    [Column("streak")]
    public int Streak { get; set; }

    /// <summary>Day number since the epoch, or null when nothing has been played.</summary>
    [Column("last_played_day")]
    public int? LastPlayedDayNumber { get; set; }

    [Column("total_blocks_filled")]
    public int TotalBlocksFilled { get; set; }
}

/// <summary>A completed picture, one row per authored puzzle.</summary>
[Table("solved_puzzle")]
internal sealed class SolvedPuzzleEntity
{
    [PrimaryKey]
    [Column("puzzle_id")]
    public string PuzzleId { get; set; } = string.Empty;

    [Column("first_solved_day")]
    public int FirstSolvedDayNumber { get; set; }

    [Column("best_stars")]
    public int BestStars { get; set; }

    [Column("best_time_seconds")]
    public double BestTimeSeconds { get; set; }

    [Column("times_solved")]
    public int TimesSolved { get; set; }
}

/// <summary>An earned trophy.</summary>
[Table("trophy")]
internal sealed class TrophyEntity
{
    [PrimaryKey]
    [Column("trophy_id")]
    public int TrophyId { get; set; }

    [Column("earned_day")]
    public int EarnedDayNumber { get; set; }
}
