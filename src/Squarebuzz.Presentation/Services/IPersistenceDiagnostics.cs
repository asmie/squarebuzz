namespace Squarebuzz.Presentation.Services;

public enum PersistenceOperation
{
    LoadSettings,
    SaveSettings,
    LoadProgress,
    LoadGame,
    LoadGames,
    RebuildGame,
    DeleteGame,
    SaveGame,
    JournalCompletion,
    ApplyCompletions,
    ResetProgress,
}

/// <summary>Local diagnostic reporting. Implementations must not throw or record saved board contents.</summary>
public interface IPersistenceDiagnostics
{
    void Report(PersistenceOperation operation, Exception exception, Guid? sessionId = null);
}

public sealed class NullPersistenceDiagnostics : IPersistenceDiagnostics
{
    public static NullPersistenceDiagnostics Instance { get; } = new();
    private NullPersistenceDiagnostics() { }
    public void Report(PersistenceOperation operation, Exception exception, Guid? sessionId = null) { }
}
