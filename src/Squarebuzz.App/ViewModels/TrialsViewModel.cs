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

/// <summary>One rung of the Timed Trial ladder, ready for the item template.</summary>
public sealed class TimedCard
{
    public required TimedTier Tier { get; init; }

    public required string Name { get; init; }

    public required string Subtitle { get; init; }

    public required string Description { get; init; }

    public string Clock => Tier.ClockText;
}

/// <summary>One stop on the Puzzle Path, ready for the item template.</summary>
public sealed class PathCard
{
    public required PathNode Node { get; init; }

    /// <summary>Star once finished, otherwise the stop's number. Locked nodes still show theirs.</summary>
    public required string Caption { get; init; }

    /// <summary>Spoken description, since a circle with a number in it says nothing on its own.</summary>
    public required string Description { get; init; }

    /// <summary>
    /// Sideways shift that makes the column of nodes read as a winding trail.
    /// </summary>
    /// <remarks>
    /// The eight-step cycle is the prototype's: 0, 26, 46, 26, 0, -26, -46, -26. It is a sampled
    /// sine wave, so the trail leans out and back rather than zig-zagging.
    /// </remarks>
    public required double Offset { get; init; }

    /// <summary>The current stop is drawn larger, as the one the player is meant to notice.</summary>
    public double Diameter => Node.State == PathNodeState.Current ? 62 : 52;

    public double FontSize => Node.State == PathNodeState.Current ? 20 : 17;

    public bool IsLast { get; init; }

    public PathNodeState State => Node.State;
}

/// <summary>
/// Trials: today's puzzle, the Puzzle Path and the trophy cabinet.
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
        _progress = progress;
        _puzzles = puzzles;
        _navigation = navigation;
        _clock = clock;
    }

    public ObservableCollection<TrophyCard> Trophies { get; } = [];

    public ObservableCollection<PathCard> Path { get; } = [];

    /// <summary>
    /// The three rungs, built once: they are fixed content, not player state.
    /// </summary>
    public ObservableCollection<TimedCard> Timed { get; } = [];

    [ObservableProperty]
    public partial string PathSummary { get; private set; } = string.Empty;

    /// <summary>
    /// Which tab is showing. An enum rather than a pair of flags now that there are three of them -
    /// two booleans cannot express "exactly one of three" without letting both be false.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(
        nameof(IsDailyTab), nameof(IsTimedTab), nameof(IsPathTab), nameof(IsTrophiesTab))]
    public partial TrialsTab Tab { get; private set; } = TrialsTab.Daily;

    public bool IsDailyTab => Tab == TrialsTab.Daily;

    public bool IsTimedTab => Tab == TrialsTab.Timed;

    public bool IsPathTab => Tab == TrialsTab.Path;

    public bool IsTrophiesTab => Tab == TrialsTab.Trophies;

    [ObservableProperty]
    public partial bool IsDailyAvailable { get; private set; } = true;

    [ObservableProperty]
    public partial string TodayLabel { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string EarnedSummary { get; private set; } = string.Empty;

    public string Heading => T("trials");

    public string DailyTabLabel => T("tabDaily");

    public string TimedTabLabel => T("tabTimed");

    public string PathTabLabel => T("tabPath");

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
        IReadOnlyList<SolvedPuzzle> solved;

        try
        {
            progress = await _progress.GetProgressAsync();
            earned = await _progress.GetTrophiesAsync();
            solved = await _progress.GetSolvedPuzzlesAsync();
        }
        catch (Exception)
        {
            progress = PlayerProgress.Empty;
            earned = [];
            solved = [];
        }

        BuildPath(solved);
        BuildTimedLadder();

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
    /// Rebuilds the trail from the solved table. No state of its own - see <see cref="PuzzlePath"/>.
    /// </summary>
    private void BuildPath(IReadOnlyList<SolvedPuzzle> solved)
    {
        // The eight-step lean from the prototype, sampled from a sine so the trail curves.
        double[] offsets = [0, 26, 46, 26, 0, -26, -46, -26];

        var nodes = PuzzlePath.Build(_puzzles.Puzzles, [.. solved.Select(s => s.PuzzleId)]);

        Path.Clear();

        for (var i = 0; i < nodes.Count; i++)
        {
            var node = nodes[i];

            Path.Add(new PathCard
            {
                Node = node,
                Caption = node.State == PathNodeState.Done ? "★" : node.Number.ToString(CultureInfo.CurrentCulture),
                Description = DescribeNode(node),
                Offset = offsets[i % offsets.Length],
                IsLast = i == nodes.Count - 1,
            });
        }

        PathSummary = Strings.Format("galleryFound", PuzzlePath.CountDone(nodes), nodes.Count);
    }

    /// <summary>
    /// One sentence per stop. A numbered circle conveys nothing without sight of the whole trail,
    /// and an unfinished picture must not be named here any more than it is in the Gallery.
    /// </summary>
    private string DescribeNode(PathNode node) => node.State switch
    {
        PathNodeState.Done => Strings.Format("a11yPathDone", node.Number, T($"Puzzle_{node.PuzzleId}")),
        PathNodeState.Current => Strings.Format("a11yPathCurrent", node.Number, node.Size),
        _ => Strings.Format("a11yPathLocked", node.Number),
    };

    [RelayCommand]
    private void ShowDaily() => Tab = TrialsTab.Daily;

    [RelayCommand]
    private void ShowTimed() => Tab = TrialsTab.Timed;

    [RelayCommand]
    private void ShowPath() => Tab = TrialsTab.Path;

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

    /// <summary>Starts the picture at a stop, unless it is still locked.</summary>
    [RelayCommand]
    private async Task PlayNodeAsync(PathCard? card)
    {
        if (card is null || !card.Node.IsPlayable)
        {
            return;
        }

        await _navigation.GoToAsync(
            Routes.Game,
            new Dictionary<string, object> { [GameViewModel.PuzzleIdParameter] = card.Node.PuzzleId });
    }

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
/// <remarks>
/// The prototype had a fourth, Timed Trial, which needs a countdown inside <c>GameSession</c> that
/// does not exist yet. Absent rather than present and hollow.
/// </remarks>
public enum TrialsTab
{
    Daily,
    Timed,
    Path,
    Trophies,
}
