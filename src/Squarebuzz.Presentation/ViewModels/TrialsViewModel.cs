using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Squarebuzz.Presentation.Navigation;
using Squarebuzz.Presentation.Services;
using Squarebuzz.Core.Abstractions;
using Squarebuzz.Core.Model;
using Squarebuzz.Core.Progression;

namespace Squarebuzz.Presentation.ViewModels;

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

/// <summary>One cell of the Daily tab's month calendar, ready for the item template.</summary>
public sealed class CalendarDayCell
{
    /// <summary>Day of the month, or empty for the blanks padding the first week.</summary>
    public required string Label { get; init; }

    public required bool IsDone { get; init; }

    public required bool IsToday { get; init; }
}

/// <summary>One rung of the Timed Trial ladder, ready for the item template.</summary>
public sealed class TimedCard
{
    public required TimedTier Tier { get; init; }

    public required string Name { get; init; }

    public required string Subtitle { get; init; }

    public required string Description { get; init; }

    public string Clock => Tier.ClockText;
}

/// <summary>
/// Trials: today's puzzle, the timed tiers and the trophy cabinet. The campaign that used to
/// live here as the Puzzle Path has its own screen now - see <see cref="LevelsViewModel"/>.
/// </summary>
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
        [TrophyId.BeatTheClock] = "⏱",
        [TrophyId.MarathonChamp] = "🏁",
        [TrophyId.MonthStreak] = "📅",
        [TrophyId.BigPicture] = "🗺",
        [TrophyId.ThousandBlocks] = "🏰",
        [TrophyId.StarGazer] = "🌟",
        [TrophyId.Explorer] = "🧭",
        [TrophyId.PackMaster] = "🎒",
        [TrophyId.Flawless] = "💎",
    };

    private readonly IProgressRepository _progress;
    private readonly IPuzzleRepository _puzzles;
    private readonly INavigationService _navigation;
    private readonly IClock _clock;

    public TrialsViewModel(
        ILocalizationService strings,
        IProgressRepository progress,
        IPuzzleRepository puzzles,
        INavigationService navigation,
        IClock clock)
        : base(strings)
    {
        ArgumentNullException.ThrowIfNull(progress);
        ArgumentNullException.ThrowIfNull(puzzles);
        ArgumentNullException.ThrowIfNull(navigation);
        ArgumentNullException.ThrowIfNull(clock);

        _progress = progress;
        _puzzles = puzzles;
        _navigation = navigation;
        _clock = clock;
    }

    public ObservableCollection<TrophyCard> Trophies { get; } = [];

    /// <summary>
    /// The ladder's rungs, built once: they are fixed content, not player state.
    /// </summary>
    public ObservableCollection<TimedCard> Timed { get; } = [];

    /// <summary>
    /// Which tab is showing. An enum rather than a pair of flags: two booleans cannot express
    /// "exactly one of three" without letting both be false.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDailyTab), nameof(IsTimedTab), nameof(IsTrophiesTab))]
    public partial TrialsTab Tab { get; private set; } = TrialsTab.Daily;

    public bool IsDailyTab => Tab == TrialsTab.Daily;

    public bool IsTimedTab => Tab == TrialsTab.Timed;

    public bool IsTrophiesTab => Tab == TrialsTab.Trophies;

    [ObservableProperty]
    public partial bool IsDailyAvailable { get; private set; } = true;

    [ObservableProperty]
    public partial string TodayLabel { get; private set; } = string.Empty;

    /// <summary>The streak flame on the Daily card, e.g. "🔥 3".</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StreakDescription))]
    public partial int Streak { get; private set; }

    public string StreakDescription => Strings.Format("a11yStreak", Streak);

    /// <summary>This month's calendar cells - blanks pad the first week.</summary>
    public ObservableCollection<CalendarDayCell> CalendarDays { get; } = [];

    [ObservableProperty]
    public partial string MonthLabel { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string CalendarDescription { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string EarnedSummary { get; private set; } = string.Empty;

    public string Heading => T("trials");

    public string DailyTabLabel => T("tabDaily");

    public string TimedTabLabel => T("tabTimed");

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

        BuildTimedLadder();

        IsDailyAvailable = DailyPuzzle.IsAvailable(progress, today);
        Streak = progress.Streak;

        await BuildCalendarAsync(today);

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

    /// <summary>
    /// The ladder is fixed content, so it is built once and then left alone. Rebuilt on a language
    /// change, though, which is why it lives here rather than in the constructor.
    /// </summary>
    private void BuildTimedLadder()
    {
        Timed.Clear();

        foreach (var tier in TimedTrial.Tiers)
        {
            var name = T($"timedName{tier.Tier}");
            var subtitle = T($"timedSub{tier.Tier}");

            Timed.Add(new TimedCard
            {
                Tier = tier,
                Name = name,
                Subtitle = subtitle,
                Description = Strings.Format("a11yTimedTier", name, subtitle, tier.ClockText),
            });
        }
    }

    /// <summary>
    /// The design's month calendar: which days this month the daily was finished. Real history
    /// from the daily-completion table, not a guess derived from the streak.
    /// </summary>
    private async Task BuildCalendarAsync(DateOnly today)
    {
        IReadOnlyList<DateOnly> completions;

        try
        {
            completions = await _progress.GetDailyCompletionsAsync();
        }
        catch (Exception)
        {
            completions = [];
        }

        MonthLabel = today.ToString("MMMM yyyy", CultureInfo.CurrentCulture);

        var cells = DailyCalendar.Build(
            today,
            completions,
            CultureInfo.CurrentCulture.DateTimeFormat.FirstDayOfWeek);

        CalendarDays.Clear();

        foreach (var cell in cells)
        {
            CalendarDays.Add(new CalendarDayCell
            {
                Label = cell.IsBlank ? string.Empty : cell.Day.ToString(CultureInfo.CurrentCulture),
                IsDone = cell.IsDone,
                IsToday = cell.IsToday,
            });
        }

        CalendarDescription = Strings.Format(
            "a11yDailyCalendar",
            cells.Count(c => c.IsDone));
    }

    [RelayCommand]
    private void ShowDaily() => Tab = TrialsTab.Daily;

    [RelayCommand]
    private void ShowTimed() => Tab = TrialsTab.Timed;

    /// <summary>Starts a timed trial.</summary>
    [RelayCommand]
    private async Task PlayTimedAsync(TimedCard? card)
    {
        if (card is null)
        {
            return;
        }

        await _navigation.GoToAsync(
            Routes.Game,
            new Dictionary<string, object>
            {
                [GameViewModel.TimedTierParameter] = card.Tier.Tier.ToString(CultureInfo.InvariantCulture),
            });
    }

    [RelayCommand]
    private void ShowTrophies() => Tab = TrialsTab.Trophies;

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

/// <summary>The tabs on the Trials screen.</summary>
public enum TrialsTab
{
    Daily,
    Timed,
    Trophies,
}
