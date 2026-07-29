using SQLite;

namespace Squarebuzz.Data.Migrations;

/// <summary>
/// One forward step in the database schema.
/// </summary>
/// <remarks>
/// Migrations are numbered and applied in order, each recorded in <c>schema_version</c> once
/// it succeeds. There is deliberately no Down step: a released build never downgrades a
/// player's database, and pretending otherwise invites untested reverse paths.
/// </remarks>
internal interface IMigration
{
    /// <summary>Sequence number. Must be unique and ascending.</summary>
    int Version { get; }

    /// <summary>Human-readable name, for diagnostics.</summary>
    string Name { get; }

    Task ApplyAsync(SQLiteAsyncConnection connection);
}
