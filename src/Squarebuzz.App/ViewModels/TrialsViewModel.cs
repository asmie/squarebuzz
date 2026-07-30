using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Squarebuzz.App.Services;
using Squarebuzz.Core.Abstractions;
using Squarebuzz.Core.Model;
using Squarebuzz.Core.Progression;

namespace Squarebuzz.App.ViewModels;

/// <summary>One trophy, earned or not.</summary>
public sealed class TrophyCard
{
    public required TrophyId Id { get; init; }

    public required string Icon { get; init; }

    public required string Name { get; init; }

    /// <summary>How it is won - worth saying, since the rules are not otherwise discoverable.</summary>
    public required string Hint { get; init; }

    public required bool IsEarned { get; init; }

    /// <summary>Date earned, or a dash.</summary>
    public required string EarnedOn { get; init; }

    public double Opacity => IsEarned ? 1 : 0.5;

    /// <summary>
    /// Name, whether it is earned, and how it is won, as one sentence. The emoji icon and the
    /// gold border carry all of that visually and none of it otherwise.
    /// </summary>
    public required string Description { get; init; }
}

/// <summary>
/// Trials: today's puzzle and the trophy cabinet.
/// </summary>
/// <remarks>
/// The prototype also sketched timed modes and a "puzzle path". Both need work the domain does
/// not have yet - a countdown, and a progression model - so they are deliberately absent rather
/// than present and hollow.
/// </remarks>
public partial class TrialsViewModel : LocalizedViewModel
{
    /// <summary>Icons per trophy. The prototype had a matching row of emoji.</summary>
    private static readonly Dictionary<TrophyId, string> Icons = new()
    {
        [TrophyId.FirstPicture] = "🖼",
        [TrophyId.WeekStreak] = "🔥",
        [TrophyId.NoHints] = "🧠",
        [TrophyId.Speedy] = "⚡",
        [TrophyId.HundredBlocks] = "🧱",
        [TrophyId.DinoFan] = "🦕",
        [TrophyId.NightOwl] = "🦉",
        [TrophyId.PerfectTen] = "💯",
        [TrophyId.Collector] = "🏆",
    };

    private readonly IProgressRepository _progress;
    private readonly INavigationService _navigation;
    private readonly IClock _clock;

    public TrialsViewModel(
        ILocalizationService strings,
        IProgressRepository progress,
        INavigationService navigation,
        IClock clock)
        : base(strings)
    {
        _progress = progress;
        _navigation = navigation;
        _clock = clock;
    }

    public ObservableCollection<TrophyCard> Trophies { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDailyTab), nameof(IsTrophiesTab))]
    public partial bool ShowingTrophies { get; private set; }

    public bool IsDailyTab => !ShowingTrophies;

    public bool IsTrophiesTab => ShowingTrophies;

    [ObservableProperty]
    public partial bool IsDailyAvailable { get; private set; } = true;

    [ObservableProperty]
    public partial string TodayLabel { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string EarnedSummary { get; private set; } = string.Empty;

    public string Heading => T("trials");

    public string DailyTabLabel => T("dailyTitle");

    public string TrophiesTabLabel => T("trophies");

    public string DailyTitle => T("dailyTitle");

    public string DailySubtitle => T("dailySub");

    public string PlayLabel => T("play");

    public string DailyDoneLabel => T("dailyDone");

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
        var today = _clock.Today;
        TodayLabel = today.ToString("dddd, d MMMM", CultureInfo.CurrentCulture);

        PlayerProgress progress;
        IReadOnlyList<EarnedTrophy> earned;

        try
        {
            progress = await _progress.GetProgressAsync();
            earned = await _progress.GetTrophiesAsync();
        }
        catch (Exception)
        {
            progress = PlayerProgress.Empty;
            earned = [];
        }

        IsDailyAvailable = DailyPuzzle.IsAvailable(progress, today);

        var earnedById = earned.ToDictionary(e => e.Trophy);

        Trophies.Clear();

        // Enum order is the display order, and the ids are persisted, so the cabinet keeps a
        // stable layout as trophies are won.
        var ordinal = 1;
        foreach (var trophy in Enum.GetValues<TrophyId>())
        {
            var record = earnedById.TryGetValue(trophy, out var found) ? found : (EarnedTrophy?)null;

            Trophies.Add(new TrophyCard
            {
                Id = trophy,
                Icon = Icons.GetValueOrDefault(trophy, "🏅"),
                Name = T($"tr{ordinal}"),
                Hint = T($"trophyHint{ordinal}"),
                IsEarned = record is not null,
                EarnedOn = record is null
                    ? "—"
                    : record.EarnedOn.ToString("d MMM yyyy", CultureInfo.CurrentCulture),
                Description = Strings.Format(
                    record is null ? "a11yTrophyLocked" : "a11yTrophyEarned",
                    T($"tr{ordinal}"),
                    T($"trophyHint{ordinal}")),
            });

            ordinal++;
        }

        EarnedSummary = Strings.Format("galleryFound", earnedById.Count, Trophies.Count);
    }

    [RelayCommand]
    private void ShowDaily() => ShowingTrophies = false;

    [RelayCommand]
    private void ShowTrophies() => ShowingTrophies = true;

    [RelayCommand]
    private async Task PlayDailyAsync()
    {
        if (!IsDailyAvailable)
        {
            return;
        }

        await _navigation.GoToAsync(
            Routes.Game,
            new Dictionary<string, object> { [GameViewModel.DailyParameter] = "1" });
    }
}
