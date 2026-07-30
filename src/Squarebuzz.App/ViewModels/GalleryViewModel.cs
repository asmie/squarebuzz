using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Squarebuzz.App.Services;
using Squarebuzz.Core.Abstractions;
using Squarebuzz.Core.Model;

namespace Squarebuzz.App.ViewModels;

/// <summary>One picture in the gallery, found or not.</summary>
public sealed class GalleryCard
{
    public required Puzzle Puzzle { get; init; }

    public required bool IsFound { get; init; }

    /// <summary>True for pictures in a pack the player has not unlocked.</summary>
    public required bool IsLocked { get; init; }

    /// <summary>The picture's name once found, otherwise "???".</summary>
    public required string Name { get; init; }

    /// <summary>Date first completed, or a dash.</summary>
    public required string FoundOn { get; init; }

    /// <summary>Stars earned, as filled and hollow glyphs. Empty until found.</summary>
    public required string Stars { get; init; }

    /// <summary>Best time, or empty.</summary>
    public required string BestTime { get; init; }

    /// <summary>Localised "Locked" caption, carried on the card so the template stays simple.</summary>
    public required string LockedLabel { get; init; }

    /// <summary>
    /// The whole card as one sentence. A grid of identical unlabelled tiles is what a screen
    /// reader sees otherwise, and an unfound card must not give its picture away here either.
    /// </summary>
    public required string Description { get; init; }

    public string Size => $"{Puzzle.Width}×{Puzzle.Height}";

    /// <summary>Found pictures show their colour; the rest stay hidden behind blocks.</summary>
    public bool IsMasked => !IsFound;

    public double Opacity => IsFound ? 1 : 0.75;
}

/// <summary>
/// The gallery: every authored picture, revealed as it is found.
/// </summary>
/// <remarks>
/// Serves two purposes at once, which is why it is reached from New Game rather than being a
/// menu entry of its own: it is the collection a child builds up, and it is how they pick a
/// specific picture to play.
/// </remarks>
public partial class GalleryViewModel : LocalizedViewModel
{
    private readonly IPuzzleRepository _puzzles;
    private readonly IProgressRepository _progress;
    private readonly INavigationService _navigation;

    public GalleryViewModel(
        ILocalizationService strings,
        IPuzzleRepository puzzles,
        IProgressRepository progress,
        INavigationService navigation)
        : base(strings)
    {
        _puzzles = puzzles;
        _progress = progress;
        _navigation = navigation;
    }

    public ObservableCollection<GalleryCard> Cards { get; } = [];

    [ObservableProperty]
    public partial string FoundSummary { get; private set; } = string.Empty;

    public string Heading => T("gallery");

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
        IReadOnlyList<SolvedPuzzle> solved;

        try
        {
            solved = await _progress.GetSolvedPuzzlesAsync();
        }
        catch (Exception)
        {
            solved = [];
        }

        var byId = solved.ToDictionary(s => s.PuzzleId, StringComparer.Ordinal);
        var lockedPacks = _puzzles.Packs
            .Where(p => p.Locked)
            .Select(p => p.Id)
            .ToHashSet(StringComparer.Ordinal);

        Cards.Clear();

        foreach (var puzzle in _puzzles.Puzzles)
        {
            var record = byId.GetValueOrDefault(puzzle.Id);
            var isFound = record is not null;

            Cards.Add(new GalleryCard
            {
                Puzzle = puzzle,
                IsFound = isFound,
                IsLocked = lockedPacks.Contains(puzzle.Pack),

                // Withholding the name is what makes finding one feel like a discovery.
                Name = isFound ? T($"Puzzle_{puzzle.Id}") : "???",
                FoundOn = record is null
                    ? "—"
                    : record.FirstSolvedOn.ToString("d MMM yyyy", CultureInfo.CurrentCulture),
                Stars = record is null
                    ? string.Empty
                    : new string('★', record.BestStars) + new string('☆', Math.Max(0, 3 - record.BestStars)),
                BestTime = record is null
                    ? string.Empty
                    : $"{(int)record.BestTime.TotalMinutes}:{record.BestTime.Seconds:00}",
                LockedLabel = T("locked"),
                Description = DescribeCard(
                    isFound, lockedPacks.Contains(puzzle.Pack), puzzle, record?.BestStars ?? 0),
            });
        }

        FoundSummary = Strings.Format("galleryFound", byId.Count, Cards.Count);
    }

    /// <summary>
    /// One sentence per card. Deliberately says nothing about an unfound picture beyond its size -
    /// naming it, or even hinting at its shape, would spoil the discovery for the one player who
    /// depends on this text instead of the artwork.
    /// </summary>
    private string DescribeCard(bool isFound, bool isLocked, Puzzle puzzle, int stars)
    {
        var size = $"{puzzle.Width}×{puzzle.Height}";

        if (isLocked)
        {
            return $"{T("a11yLockedCard")}, {size}";
        }

        return isFound
            ? Strings.Format("a11yFound", T($"Puzzle_{puzzle.Id}"), stars) + $", {size}"
            : $"{T("a11yNotFound")} {size}";
    }

    [RelayCommand]
    private async Task PlayAsync(GalleryCard card)
    {
        ArgumentNullException.ThrowIfNull(card);

        if (card.IsLocked)
        {
            return;
        }

        await _navigation.GoToAsync(
            Routes.Game,
            new Dictionary<string, object> { [GameViewModel.PuzzleIdParameter] = card.Puzzle.Id });
    }
}
