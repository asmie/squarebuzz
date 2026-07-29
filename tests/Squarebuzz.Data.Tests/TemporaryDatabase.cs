using Squarebuzz.Data;

namespace Squarebuzz.Data.Tests;

/// <summary>
/// A throwaway database on disk, deleted when the test finishes.
/// </summary>
/// <remarks>
/// A file rather than <c>:memory:</c> on purpose: the app runs against a file, and only a file
/// exercises WAL mode, the journal side-files and reopening an existing database - which is
/// where migration bugs actually live.
/// </remarks>
internal sealed class TemporaryDatabase : IAsyncDisposable
{
    private readonly string _directory;

    public TemporaryDatabase()
    {
        _directory = Path.Combine(Path.GetTempPath(), "squarebuzz-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);

        Path_ = Path.Combine(_directory, "squarebuzz.db3");
        Database = new SquarebuzzDatabase(Path_);
    }

    public string Path_ { get; }

    public SquarebuzzDatabase Database { get; private set; }

    /// <summary>
    /// Closes and reopens the same file, as relaunching the app does. Use this to prove data
    /// really landed on disk instead of living in a connection's cache.
    /// </summary>
    public async Task ReopenAsync()
    {
        await Database.DisposeAsync();
        Database = new SquarebuzzDatabase(Path_);
    }

    public async ValueTask DisposeAsync()
    {
        await Database.DisposeAsync();

        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // A lingering SQLite handle on Windows can hold the file briefly. Leaving a temp
            // directory behind must never fail an otherwise passing test.
        }
    }
}
