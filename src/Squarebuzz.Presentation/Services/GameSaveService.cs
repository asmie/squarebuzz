using Squarebuzz.Core.Abstractions;
using Squarebuzz.Core.Model;

namespace Squarebuzz.Presentation.Services;

/// <summary>Page-owned save queue and snapshot acknowledgements, ordered across session replacements.</summary>
public sealed class GameSaveService(ISaveGameRepository saves, IClock clock, IPersistenceDiagnostics diagnostics)
{
    private GameSession? _session;
    private SavedGame? _lastSaved;
    private bool _hasSave;
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Task Pending { get; private set; } = Task.CompletedTask;
    public bool HasFailure { get; private set; }
    public event EventHandler? Changed;

    public void Attach(GameSession session, SavedGame? saved = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        _session = session;
        Id = saved?.Id ?? Guid.NewGuid();
        _lastSaved = saved;
        _hasSave = saved is not null;
        SetFailure(false);
    }

    public Task<SavedGame?> LoadAsync(Guid id) => saves.GetAsync(id);

    public Task SaveAsync(bool onlyIfChanged = false)
    {
        if (_session is not { IsOver: false, IsTimed: false } session)
        {
            return Task.CompletedTask;
        }

        if (!_hasSave && session.Mistakes == 0 && session.HintsUsed == 0
            && !session.Cells.ContainsAnyExcept(CellState.Empty))
        {
            return Task.CompletedTask;
        }

        var snapshot = SavedGame.FromSession(session, Id, clock.Now);
        _hasSave = true;
        Pending = SaveAfterAsync(Pending, session, snapshot, onlyIfChanged);
        return Pending;
    }

    public (Guid Id, PuzzleCompletion Completion) CaptureCompletion(GameSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        var origin = session.Origin;
        var completion = new PuzzleCompletion(origin?.PuzzleId, session.StarRating, session.Elapsed,
            session.Puzzle.PictureCellCount, session.HintsUsed, clock.Now)
        {
            Size = session.Puzzle.Width,
            PackId = session.Puzzle.Pack,
            Mistakes = session.Mistakes,
            IsDaily = origin?.Mode == SessionMode.Daily,
            DailyDate = origin?.DailyDate,
            Level = origin?.Level,
            TimedTier = origin?.TimedTier,
        };
        return (Id, completion);
    }

    public Task CompleteAsync((Guid Id, PuzzleCompletion Completion) captured, GameCompletionService completions)
    {
        ArgumentNullException.ThrowIfNull(completions);
        if (captured.Id == Id)
        {
            // Completion replaces the in-progress save and owns any remaining retry.
            SetFailure(false);
        }
        Pending = completions.CompleteAsync(captured.Id, captured.Completion, Pending);
        return Pending;
    }

    private async Task SaveAfterAsync(Task previous, GameSession session, SavedGame snapshot, bool onlyIfChanged)
    {
        await previous;
        if (session.IsOver || (onlyIfChanged && HasSameProgress(snapshot, _lastSaved)))
        {
            return;
        }

        try
        {
            await saves.SaveAsync(snapshot);
            if (ReferenceEquals(_session, session) && Id == snapshot.Id)
            {
                _lastSaved = snapshot;
                SetFailure(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Keep the snapshot unacknowledged so a later save can retry it.
        }
        catch (Exception error)
        {
            diagnostics.Report(PersistenceOperation.SaveGame, error, snapshot.Id);
            if (ReferenceEquals(_session, session) && Id == snapshot.Id && !session.IsOver)
            {
                SetFailure(true);
            }
        }
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

    private static bool HasSameProgress(SavedGame snapshot, SavedGame? saved) =>
        saved is not null && snapshot.Id == saved.Id && snapshot.Mistakes == saved.Mistakes
        && snapshot.HintsUsed == saved.HintsUsed && snapshot.HintsRemaining == saved.HintsRemaining
        && snapshot.HintBudget == saved.HintBudget
        && snapshot.Cells.SequenceEqual(saved.Cells)
        && snapshot.AutoCrossedCells.SequenceEqual(saved.AutoCrossedCells);
}
