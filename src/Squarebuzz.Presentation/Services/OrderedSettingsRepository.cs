using Squarebuzz.Core.Abstractions;
using Squarebuzz.Core.Model;

namespace Squarebuzz.Presentation.Services;

/// <summary>
/// Orders settings reads and writes across screens. A read waits for every write submitted
/// before it, including changes from an Options page that has already been popped.
/// </summary>
public sealed class OrderedSettingsRepository : ISettingsRepository
{
    private readonly ISettingsRepository _inner;
    private readonly IPersistenceDiagnostics _diagnostics;
    private readonly object _gate = new();
    private Task _pending = Task.CompletedTask;

    public OrderedSettingsRepository(ISettingsRepository inner, IPersistenceDiagnostics? diagnostics = null)
    {
        ArgumentNullException.ThrowIfNull(inner);
        _inner = inner;
        _diagnostics = diagnostics ?? NullPersistenceDiagnostics.Instance;
    }

    public Task<GameSettings> LoadAsync(CancellationToken cancellationToken = default) =>
        EnqueueAsync(() => DiagnoseAsync(PersistenceOperation.LoadSettings,
            () => _inner.LoadAsync(cancellationToken)), cancellationToken);

    public Task SaveAsync(GameSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return EnqueueAsync(() => DiagnoseAsync(PersistenceOperation.SaveSettings, async () =>
        {
            await _inner.SaveAsync(settings, cancellationToken).ConfigureAwait(false);
            return true;
        }), cancellationToken);
    }

    public Task SaveChangesAsync(GameSettings baseline, GameSettings updated, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(updated);
        return EnqueueAsync(() => DiagnoseAsync(PersistenceOperation.SaveSettings, async () =>
        {
            await _inner.SaveChangesAsync(baseline, updated, cancellationToken).ConfigureAwait(false);
            return true;
        }), cancellationToken);
    }

    private async Task<T> DiagnoseAsync<T>(PersistenceOperation operation, Func<Task<T>> action)
    {
        try
        {
            return await action().ConfigureAwait(false);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            _diagnostics.Report(operation, error);
            throw;
        }
    }

    private Task<T> EnqueueAsync<T>(Func<Task<T>> operation, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            var next = RunAfterAsync(_pending, operation, cancellationToken);
            _pending = next;
            return next;
        }
    }

    private static async Task<T> RunAfterAsync<T>(Task previous, Func<Task<T>> operation, CancellationToken cancellationToken)
    {
        try
        {
            await previous.ConfigureAwait(false);
        }
        catch (Exception)
        {
            // The original caller receives its failure. It must not poison later requests.
        }

        // Check after the preceding operation finishes: cancelling a queued request must not
        // let the next request overtake a write that is still running.
        cancellationToken.ThrowIfCancellationRequested();
        return await operation().ConfigureAwait(false);
    }
}
