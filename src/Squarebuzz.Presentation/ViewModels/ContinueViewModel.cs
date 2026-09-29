using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Squarebuzz.Presentation.Navigation;
using Squarebuzz.Presentation.Services;
using Squarebuzz.Core.Abstractions;
using Squarebuzz.Core.Model;

namespace Squarebuzz.Presentation.ViewModels;

/// <summary>One unfinished puzzle, as the Continue list shows it.</summary>
public sealed class SavedGameCard
{
    public required Guid Id { get; init; }

    /// <summary>The picture, for the thumbnail. Resolved from the save's id or seed.</summary>
    public required Puzzle Puzzle { get; init; }

    /// <summary>
    /// The player's marks. Drawn instead of the solution so the thumbnail shows how far they got
    /// without handing them the answer.
    /// </summary>
    public required IReadOnlyList<CellState> Marks { get; init; }

    public required string Name { get; init; }

    /// <summary>e.g. "10×10 · 2:14".</summary>
    public required string Details { get; init; }

    /// <summary>
    /// e.g. "34 / 58" - filled blocks over the picture's total. Kept to digits and a slash on
    /// purpose: it needs no translation, and a child who cannot yet read still reads a fraction.
    /// </summary>
    public required string ProgressText { get; init; }

    /// <summary>0 to 1, for the progress bar.</summary>
    public required double Progress { get; init; }

    public required string SavedWhen { get; init; }

    /// <summary>
    /// Names the picture in each button's description. Two rows of unlabelled play and delete
    /// buttons are indistinguishable to a screen reader, and one of them is destructive.
    /// </summary>
    public required string ResumeDescription { get; init; }

    public required string DeleteDescription { get; init; }
}

/// <summary>
/// The Continue screen: every unfinished puzzle, with a thumbnail of how far it has got.
/// </summary>
public partial class ContinueViewModel : LocalizedViewModel
{
    private readonly ISaveGameRepository _saveGames;
    private readonly GameSessionFactory _sessions;
    private readonly INavigationService _navigation;
    private readonly IClock _clock;
    private readonly IPersistenceDiagnostics _diagnostics;
    private SavedGameCard? _failedDelete;

    // Whether the last reload had to skip a save it could not rebuild, so a later delete does
    // not clear that warning by accident.
    private bool _rebuildFailed;

    /// <summary>
    /// Generated pictures already rebuilt for this list, by everything that determines them.
    /// </summary>
    /// <remarks>
    /// Rebuilding a generated save's picture runs the generator, which retries until it finds a
    /// logically solvable grid. The list reloads on every visit and every language change, and
    /// deleting one card used to reload it all - regenerating up to a dozen boards to remove one.
    /// </remarks>
    private readonly Dictionary<PuzzleKey, Puzzle> _generated = [];

    private readonly record struct PuzzleKey(int Size, int Difficulty, string PackId, int Seed);

    public ContinueViewModel(
        ILocalizationService strings,
        ISaveGameRepository saveGames,
        GameSessionFactory sessions,
        INavigationService navigation,
        IClock clock,
        IPersistenceDiagnostics? diagnostics = null)
        : base(strings)
    {
        ArgumentNullException.ThrowIfNull(saveGames);
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentNullException.ThrowIfNull(navigation);
        ArgumentNullException.ThrowIfNull(clock);

        _saveGames = saveGames;
        _sessions = sessions;
        _navigation = navigation;
        _clock = clock;
        _diagnostics = diagnostics ?? NullPersistenceDiagnostics.Instance;
    }

    public ObservableCollection<SavedGameCard> Saves { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSaves))]
    public partial bool IsEmpty { get; private set; } = true;

    public bool HasSaves => Saves.Count > 0;

    [ObservableProperty]
    public partial bool HasPersistenceFailure { get; private set; }

    public string PersistenceFailureText => T("storageUnavailable");
    public string RetryPersistenceText => T("tryAgain");

    public string Heading => T("continueGame");

    public string EmptyMessage => T("noSaves");

    public string StartFirstLabel => T("firstPuzzle");

    public override async Task OnAppearingAsync()
    {
        IsBusy = true;

        try
        {
            await ReloadAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }

    protected override void OnLanguageChangedCore() => _ = ReloadAsync();

    private async Task ReloadAsync()
    {
        IReadOnlyList<SavedGame> saves;

        try
        {
            saves = await _saveGames.GetAllAsync();
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception exception)
        {
            _diagnostics.Report(PersistenceOperation.LoadGames, exception);
            HasPersistenceFailure = true;
            IsEmpty = false;
            return;
        }

        Saves.Clear();
        _rebuildFailed = false;
        var stillSaved = new HashSet<PuzzleKey>();

        foreach (var save in saves)
        {
            // A save whose picture can no longer be produced is skipped rather than crashing the
            // list - it would only happen if authored content was removed between releases.
            try
            {
                Saves.Add(BuildCard(save, stillSaved));
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                _diagnostics.Report(PersistenceOperation.RebuildGame, exception, save.Id);
                _rebuildFailed = true;
            }
        }

        // Drop pictures whose saves are gone, so the cache never outgrows the list.
        foreach (var key in _generated.Keys.Where(k => !stillSaved.Contains(k)).ToList())
        {
            _generated.Remove(key);
        }

        UpdateListState();
    }

    private void UpdateListState()
    {
        HasPersistenceFailure = _failedDelete is not null || _rebuildFailed;
        IsEmpty = Saves.Count == 0 && !HasPersistenceFailure;
        OnPropertyChanged(nameof(HasSaves));
    }

    private Puzzle ResolvePuzzle(SavedGame save, HashSet<PuzzleKey> stillSaved)
    {
        // Authored pictures are a dictionary lookup already; only generation is worth keeping.
        if (save.PuzzleId is not null)
        {
            return _sessions.ResolvePuzzle(save);
        }

        var key = new PuzzleKey(save.Size, save.Difficulty, save.PackId, save.Seed);
        stillSaved.Add(key);

        if (!_generated.TryGetValue(key, out var puzzle))
        {
            puzzle = _sessions.ResolvePuzzle(save);
            _generated[key] = puzzle;
        }

        return puzzle;
    }

    private SavedGameCard BuildCard(SavedGame save, HashSet<PuzzleKey> stillSaved)
    {
        var puzzle = ResolvePuzzle(save, stillSaved);

        // The puzzle precomputes this precisely so callers do not recount an immutable value.
        var total = puzzle.PictureCellCount;
        var done = Math.Min(save.FilledCount, total);

        // A campaign level is named by its number; the mystery picture behind a generated
        // quick game stays a mystery, and an authored picture keeps its name.
        var name = save.Level is { } level && puzzle.IsGenerated
            ? Strings.Format("levelN", level)
            : puzzle.IsGenerated ? T("Puzzle_gen") : T($"Puzzle_{puzzle.Id}");
        var elapsed = ClockText.Of(save.Elapsed);

        return new SavedGameCard
        {
            Id = save.Id,
            Puzzle = puzzle,
            Marks = save.Cells,
            Name = name,
            Details = $"{save.Size}×{save.Size} · {elapsed}",
            ProgressText = $"{done} / {total}",
            Progress = total == 0 ? 0 : done / (double)total,
            SavedWhen = DescribeWhen(save.SavedAt),
            ResumeDescription = Strings.Format("a11yResumeGame", name),
            DeleteDescription = Strings.Format("a11yDeleteGame", name),
        };
    }

    /// <summary>
    /// "Today" and "Yesterday" read better than a date to a child, and are what they need to
    /// recognise which game is which.
    /// </summary>
    private string DescribeWhen(DateTimeOffset savedAt)
    {
        var savedOn = DateOnly.FromDateTime(savedAt.LocalDateTime);
        var days = _clock.Today.DayNumber - savedOn.DayNumber;

        return days switch
        {
            <= 0 => savedAt.ToLocalTime().ToString("t", CultureInfo.CurrentCulture),
            1 => savedOn.ToString("ddd", CultureInfo.CurrentCulture),
            _ => savedOn.ToString("d MMM", CultureInfo.CurrentCulture),
        };
    }

    [RelayCommand]
    private async Task ResumeAsync(SavedGameCard card)
    {
        ArgumentNullException.ThrowIfNull(card);

        await _navigation.GoToAsync(
            Routes.Game,
            new Dictionary<string, object> { [GameViewModel.SaveIdParameter] = card.Id.ToString("D") });
    }

    [RelayCommand]
    private async Task DeleteAsync(SavedGameCard card)
    {
        ArgumentNullException.ThrowIfNull(card);

        try
        {
            await _saveGames.DeleteAsync(card.Id);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception exception)
        {
            _diagnostics.Report(PersistenceOperation.DeleteGame, exception, card.Id);
            _failedDelete = card;
            HasPersistenceFailure = true;
            return;
        }

        // Removed in place: the other cards have not changed, and reloading them regenerated
        // every other generated save's picture.
        _failedDelete = null;
        Saves.Remove(card);
        UpdateListState();
    }

    [RelayCommand]
    private async Task RetryPersistenceAsync()
    {
        if (_failedDelete is { } card) await DeleteAsync(card);
        else await OnAppearingAsync();
    }

    [RelayCommand]
    private async Task NewGameAsync() => await _navigation.GoToAsync(Routes.NewGame);
}
