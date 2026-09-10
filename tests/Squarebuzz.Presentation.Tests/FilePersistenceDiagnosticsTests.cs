using System.Text.Json;
using Squarebuzz.Presentation.Services;
using Xunit;

namespace Squarebuzz.Presentation.Tests;

public sealed class FilePersistenceDiagnosticsTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "squarebuzz-diagnostics-" + Guid.NewGuid());

    [Fact]
    public void Report_WritesStructuredMetadataWithoutExceptionMessage()
    {
        var id = Guid.NewGuid();
        new FilePersistenceDiagnostics(_directory).Report(PersistenceOperation.SaveGame, new IOException("private board data"), id);
        var line = File.ReadAllText(Path.Combine(_directory, "persistence.jsonl"));
        using var record = JsonDocument.Parse(line);
        Assert.Equal("SaveGame", record.RootElement.GetProperty("operation").GetString());
        Assert.Equal(id, record.RootElement.GetProperty("sessionId").GetGuid());
        Assert.Equal(typeof(IOException).FullName, record.RootElement.GetProperty("exceptionType").GetString());
        Assert.DoesNotContain("private board data", line, StringComparison.Ordinal);
    }

    [Fact]
    public void Report_RotatesAndKeepsOnlyOnePreviousFile()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "persistence.jsonl");
        var diagnostics = new FilePersistenceDiagnostics(_directory);
        File.WriteAllText(Path.Combine(_directory, "persistence.previous.jsonl"), "old");
        File.WriteAllText(path, new string('x', 256 * 1024));
        diagnostics.Report(PersistenceOperation.SaveGame, new IOException());
        Assert.Equal(2, Directory.GetFiles(_directory).Length);
        Assert.Equal(256 * 1024, new FileInfo(Path.Combine(_directory, "persistence.previous.jsonl")).Length);
        using var record = JsonDocument.Parse(File.ReadAllText(path));
        Assert.Equal("SaveGame", record.RootElement.GetProperty("operation").GetString());
    }

    [Fact]
    public void Report_UnwritableDestination_DoesNotEscapeFailurePath()
    {
        Directory.CreateDirectory(_directory);
        var file = Path.Combine(_directory, "file");
        File.WriteAllText(file, "occupied");
        new FilePersistenceDiagnostics(file).Report(PersistenceOperation.LoadGame, new IOException());
        Assert.Equal("occupied", File.ReadAllText(file));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}
