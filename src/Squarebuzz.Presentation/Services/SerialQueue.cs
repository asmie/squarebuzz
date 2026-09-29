namespace Squarebuzz.Presentation.Services;

/// <summary>
/// Runs asynchronous operations one at a time, in the order they were queued.
/// </summary>
/// <remarks>
/// <para>
/// A failed operation is reported to its own caller only: the next one still runs. Otherwise one
/// storage hiccup would poison every later save, retry and reset queued behind it.
/// </para>
/// <para>
/// <paramref name="continueOnCapturedContext"/> decides where each operation resumes. The
/// completion service runs on the UI thread and raises UI-facing events, so it keeps the
/// context; the settings repository is shared by every screen and resumes anywhere.
/// </para>
/// </remarks>
/// <param name="continueOnCapturedContext">Whether queued operations resume on the caller's context.</param>
internal sealed class SerialQueue(bool continueOnCapturedContext)
{
    private readonly object _gate = new();
    private Task _tail = Task.CompletedTask;

    /// <summary>Queues <paramref name="operation"/> behind everything already queued.</summary>
    /// <remarks>
    /// Cancellation is checked when the operation reaches the front, not only when it is queued:
    /// a cancelled request must not overtake a write that is still running.
    /// </remarks>
    public Task<T> Enqueue<T>(Func<Task<T>> operation, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            var next = RunAfterAsync(_tail, operation, cancellationToken);
            _tail = next;
            return next;
        }
    }

    /// <inheritdoc cref="Enqueue{T}(Func{Task{T}}, CancellationToken)"/>
    public Task Enqueue(Func<Task> operation, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);

        return Enqueue(async () =>
        {
            await operation().ConfigureAwait(continueOnCapturedContext);
            return true;
        }, cancellationToken);
    }

    private async Task<T> RunAfterAsync<T>(Task previous, Func<Task<T>> operation, CancellationToken cancellationToken)
    {
        try
        {
            await previous.ConfigureAwait(continueOnCapturedContext);
        }
        catch (Exception)
        {
            // The original caller receives its failure. It must not poison later requests.
        }

        cancellationToken.ThrowIfCancellationRequested();
        return await operation().ConfigureAwait(continueOnCapturedContext);
    }
}
