using Squarebuzz.Presentation.Services;

namespace Squarebuzz.Presentation.Tests.Fakes;

public sealed class FakePersistenceDiagnostics : IPersistenceDiagnostics
{
    public List<(PersistenceOperation Operation, Exception Error, Guid? SessionId)> Reports { get; } = [];

    public void Report(PersistenceOperation operation, Exception exception, Guid? sessionId = null) =>
        Reports.Add((operation, exception, sessionId));
}
