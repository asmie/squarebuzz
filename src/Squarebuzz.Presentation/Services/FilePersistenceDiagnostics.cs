using System.Text.Json;

namespace Squarebuzz.Presentation.Services;

/// <summary>Bounded local diagnostics, without settings, board contents or exception messages.</summary>
public sealed class FilePersistenceDiagnostics(string directory) : IPersistenceDiagnostics
{
    private const long MaxFileBytes = 256 * 1024;
    private readonly object _gate = new();

    public void Report(PersistenceOperation operation, Exception exception, Guid? sessionId = null)
    {
        // Diagnostics run on a failure path: even an inaccessible log directory must be harmless.
        try
        {
            using var record = new MemoryStream();
            using (var writer = new Utf8JsonWriter(record))
            {
                writer.WriteStartObject();
                writer.WriteString("timestamp", DateTimeOffset.UtcNow);
                writer.WriteString("operation", operation.ToString());
                if (sessionId is { } id) writer.WriteString("sessionId", id);
                writer.WriteString("exceptionType", exception.GetType().FullName);
                writer.WriteNumber("hResult", exception.HResult);
                var stack = exception.StackTrace;
                writer.WriteString("stackTrace", stack is { Length: > 4096 } ? stack[..4096] : stack);
                writer.WriteEndObject();
            }
            record.WriteByte((byte)'\n');
            lock (_gate)
            {
                Directory.CreateDirectory(directory);
                var path = Path.Combine(directory, "persistence.jsonl");
                if (File.Exists(path) && new FileInfo(path).Length + record.Length > MaxFileBytes)
                {
                    File.Move(path, Path.Combine(directory, "persistence.previous.jsonl"), overwrite: true);
                }
                using var file = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read);
                record.Position = 0;
                record.CopyTo(file);
            }
        }
        catch (Exception)
        {
            // Logging must never replace or escape the recoverable storage failure.
        }
    }
}
