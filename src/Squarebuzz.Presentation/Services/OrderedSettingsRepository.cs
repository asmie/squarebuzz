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
    private readonly object _gate = new();
    private Task _pending = Task.CompletedTask;

    public OrderedSettingsRepository(ISettingsRepository inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        _inner = inner;
    }

    public Task<GameSettings> LoadAsync(CancellationToken cancellationToken = default) =>
        EnqueueAsync(() => _inner.LoadAsync(cancellationToken), cancellationToken);

    public Task SaveAsync(GameSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return EnqueueAsync(async () =>
        {
            await _inner.SaveAsync(settings, cancellationToken).ConfigureAwait(false);
            return true;
        }, cancellationToken);
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
