using SQLite;

namespace Squarebuzz.Data.Migrations;

/// <summary>
/// One forward step in the database schema.
/// </summary>
/// <remarks>
/// <para>
/// Migrations are numbered and applied in order, each recorded in <c>schema_version</c> once
/// it succeeds. There is deliberately no Down step: a released build never downgrades a
/// player's database, and pretending otherwise invites untested reverse paths.
/// </para>
/// <para>
/// <see cref="Apply"/> receives a connection that is already inside a transaction, which the
/// runner commits together with the version row - or rolls back entirely. SQLite's DDL is
/// transactional, so a migration of several statements either lands whole or leaves no trace,
/// and the retry on the next launch starts from a clean slate. The method is synchronous
/// because sqlite-net's transaction scope is; nothing a migration does here is worth yielding
/// the thread for anyway.
/// </para>
/// </remarks>
internal interface IMigration
{
    /// <summary>Sequence number. Must be unique and ascending.</summary>
    int Version { get; }

    /// <summary>Human-readable name, for diagnostics.</summary>
    string Name { get; }

    /// <summary>Applies the step, inside the transaction the runner has opened.</summary>
    void Apply(SQLiteConnection connection);
}
