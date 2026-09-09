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

    /// <summary>e.g. "34 of 58 blocks".</summary>
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

    public ContinueViewModel(
        ILocalizationService strings,
        ISaveGameRepository saveGames,
        GameSessionFactory sessions,
        INavigationService navigation,
        IClock clock)
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
    }

    public ObservableCollection<SavedGameCard> Saves { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSaves))]
    public partial bool IsEmpty { get; private set; } = true;

    public bool HasSaves => !IsEmpty;

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
        Saves.Clear();

        IReadOnlyList<SavedGame> saves;

        try
        {
            saves = await _saveGames.GetAllAsync();
        }
        catch (Exception)
        {
            IsEmpty = true;
            return;
        }

        foreach (var save in saves)
        {
            // A save whose picture can no longer be produced is skipped rather than crashing the
            // list - it would only happen if authored content was removed between releases.
            try
            {
                Saves.Add(BuildCard(save));
            }
            catch (Exception)
            {
                continue;
            }
        }

        IsEmpty = Saves.Count == 0;
    }

    private SavedGameCard BuildCard(SavedGame save)
    {
        var puzzle = _sessions.ResolvePuzzle(save);

        var total = CountFilledCells(puzzle);
        var done = Math.Min(save.FilledCount, total);

        // A campaign level is named by its number; the mystery picture behind a generated
        // quick game stays a mystery, and an authored picture keeps its name.
        var name = save.Level is { } level && puzzle.IsGenerated
            ? Strings.Format("levelN", level)
            : puzzle.IsGenerated ? T("Puzzle_gen") : T($"Puzzle_{puzzle.Id}");
        var elapsed = $"{(int)save.Elapsed.TotalMinutes}:{save.Elapsed.Seconds:00}";

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

    private static int CountFilledCells(Puzzle puzzle)
    {
        var count = 0;
        var solution = puzzle.Solution;

        for (var i = 0; i < solution.Length; i++)
        {
            if (solution[i])
            {
                count++;
            }
        }

        return count;
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
        catch (Exception)
        {
            return;
        }

        Saves.Remove(card);
        IsEmpty = Saves.Count == 0;
    }

    [RelayCommand]
    private async Task NewGameAsync() => await _navigation.GoToAsync(Routes.NewGame);
}
