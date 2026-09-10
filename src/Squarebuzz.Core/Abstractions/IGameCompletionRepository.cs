using Squarebuzz.Core.Model;

namespace Squarebuzz.Core.Abstractions;

/// <summary>Durable completion journal and atomic, idempotent progress updates.</summary>
public interface IGameCompletionRepository
{
    /// <summary>
    /// Durably journals the result without replacing an existing pending result or receipt.
    /// RetryPendingAsync applies progress, trophies and save removal together, exactly once.
    /// </summary>
    Task JournalAsync(Guid sessionId, PuzzleCompletion completion);

    /// <summary>Applies results left pending by a previous failure or process exit.</summary>
    Task RetryPendingAsync();
}
