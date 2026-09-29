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

    // A list, not a dictionary: results must be journaled in the order they were won, and a
    // dictionary reuses a removed entry's slot, so a later win could enumerate ahead of an
    // earlier one that is still failing.
    private readonly List<(Guid Id, PuzzleCompletion Completion)> _pending = [];

    // On the UI thread throughout: the queued work raises Changed and touches _pending.
    private readonly SerialQueue _operations = new(continueOnCapturedContext: true);
    private readonly IPersistenceDiagnostics _diagnostics;
    public bool HasFailure { get; private set; }
    public event EventHandler? Changed;

    public GameCompletionService(IGameCompletionRepository completions, IProgressRepository progress,
        IPersistenceDiagnostics? diagnostics = null)
    {
        ArgumentNullException.ThrowIfNull(completions);
        ArgumentNullException.ThrowIfNull(progress);
        _completions = completions;
        _progress = progress;
        _diagnostics = diagnostics ?? NullPersistenceDiagnostics.Instance;
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
            if (!_pending.Exists(entry => entry.Id == sessionId))
            {
                _pending.Add((sessionId, completion));
            }

            await RetryCoreAsync();
        });
    }

    public Task RetryAsync() => Enqueue(RetryCoreAsync);

    public Task ResetAsync() => Enqueue(async () =>
    {
        try
        {
            await _progress.ResetAsync();
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            _diagnostics.Report(PersistenceOperation.ResetProgress, error);
            throw;
        }
        _pending.Clear();
        SetFailure(false);
    });

    private async Task RetryCoreAsync()
    {
        var failed = false;
        foreach (var (id, completion) in _pending.ToArray())
        {
            try
            {
                await _completions.JournalAsync(id, completion);
                _pending.RemoveAll(entry => entry.Id == id);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception error)
            {
                failed = true;
                _diagnostics.Report(PersistenceOperation.JournalCompletion, error, id);
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
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception error)
        {
            failed = true;
            _diagnostics.Report(PersistenceOperation.ApplyCompletions, error);
            // The durable journal is unchanged; retry on the next resume/startup/completion.
        }
        SetFailure(failed);
    }

    private void SetFailure(bool value)
    {
        if (HasFailure == value)
        {
            return;
        }
        HasFailure = value;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private Task Enqueue(Func<Task> operation) => _operations.Enqueue(operation);
}
