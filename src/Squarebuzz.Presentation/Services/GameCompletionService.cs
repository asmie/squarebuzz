using Squarebuzz.Core.Abstractions;
using Squarebuzz.Core.Model;

namespace Squarebuzz.Presentation.Services;

/// <summary>
/// App-lifetime owner of completion retries. Calls originate on the UI thread; queued work
/// survives its game page and is ordered with a parent's progress reset.
/// </summary>
public sealed class GameCompletionService
{
    private readonly IGameCompletionRepository _completions;
    private readonly IProgressRepository _progress;
    private readonly Dictionary<Guid, PuzzleCompletion> _pending = [];
    private Task _operations = Task.CompletedTask;

    public GameCompletionService(IGameCompletionRepository completions, IProgressRepository progress)
    {
        ArgumentNullException.ThrowIfNull(completions);
        ArgumentNullException.ThrowIfNull(progress);
        _completions = completions;
        _progress = progress;
    }

    public Task CompleteAsync(Guid sessionId, PuzzleCompletion completion, Task precedingSave)
    {
        ArgumentNullException.ThrowIfNull(completion);
        ArgumentNullException.ThrowIfNull(precedingSave);
        return Enqueue(async () =>
        {
            // Register in the service queue before waiting for saves, so a later reset cannot
            // overtake this win and then have the delayed completion restore erased progress.
            await precedingSave;
            _pending.TryAdd(sessionId, completion);
            await RetryCoreAsync();
        });
    }

    public Task RetryAsync() => Enqueue(RetryCoreAsync);

    public Task ResetAsync() => Enqueue(async () =>
    {
        await _progress.ResetAsync();
        _pending.Clear();
    });

    private async Task RetryCoreAsync()
    {
        foreach (var (id, completion) in _pending.ToArray())
        {
            try
            {
                await _completions.JournalAsync(id, completion);
                _pending.Remove(id);
            }
            catch (Exception)
            {
                // Retain even a result whose initial journal write failed. Once that write
                // succeeds, the repository also protects it across application restarts.
                // Keep later results behind it so recovery cannot apply older streak dates last.
                break;
            }
        }

        try
        {
            await _completions.RetryPendingAsync();
        }
        catch (Exception)
        {
            // The durable journal is unchanged; retry on the next resume/startup/completion.
        }
    }

    private Task Enqueue(Func<Task> operation)
    {
        _operations = RunAfterAsync(_operations, operation);
        return _operations;
    }

    private static async Task RunAfterAsync(Task previous, Func<Task> operation)
    {
        try
        {
            await previous;
        }
        catch (Exception)
        {
            // A failed reset is reported to its caller, but must not poison later retries.
        }

        await operation();
    }
}
