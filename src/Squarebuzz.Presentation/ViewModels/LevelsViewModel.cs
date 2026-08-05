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

/// <summary>One level on the map, ready for the item template.</summary>
public sealed class LevelCard
{
    public required int Number { get; init; }

    public required LevelNodeState State { get; init; }

    /// <summary>Star once finished, otherwise the level's number. Locked levels still show theirs.</summary>
    public required string Caption { get; init; }

    /// <summary>Spoken description, since a circle with a number in it says nothing on its own.</summary>
    public required string Description { get; init; }

    /// <summary>True when this level reveals an authored picture - it gets a little badge.</summary>
    public required bool IsMilestone { get; init; }

    /// <summary>
    /// Sideways shift that makes the column of nodes read as a winding trail.
    /// </summary>
    /// <remarks>
    /// The eight-step cycle is the prototype's: 0, 26, 46, 26, 0, -26, -46, -26. It is a sampled
    /// sine wave, so the trail leans out and back rather than zig-zagging.
    /// </remarks>
    public required double Offset { get; init; }

    public bool IsLast { get; init; }

    /// <summary>The current level is drawn larger, as the one the player is meant to notice.</summary>
    public double Diameter => State == LevelNodeState.Current ? 62 : 52;

    public double FontSize => State == LevelNodeState.Current ? 20 : 17;

    public bool IsPlayable => State != LevelNodeState.Locked;
}

/// <summary>Fifty levels under one header, the unit the map's list groups by.</summary>
public sealed class LevelGroup(string title) : List<LevelCard>
{
    public string Title { get; } = title;
}

/// <summary>
/// The level map behind the Play button: six hundred stops, grouped in fifties, with the
/// player's next level front and centre.
/// </summary>
public partial class LevelsViewModel : LocalizedViewModel
{
    /// <summary>Levels per group header. Fifty keeps the headers meaningful and the groups few.</summary>
    private const int GroupSize = 50;

    private readonly IProgressRepository _progress;
    private readonly IPuzzleRepository _puzzles;
    private readonly INavigationService _navigation;

    public LevelsViewModel(
        ILocalizationService strings,
        IProgressRepository progress,
        IPuzzleRepository puzzles,
        INavigationService navigation)
        : base(strings)
    {
        _progress = progress;
        _puzzles = puzzles;
        _navigation = navigation;
    }

    /// <summary>The whole campaign. A CollectionView virtualizes this; nothing else could show 600 nodes.</summary>
    public ObservableCollection<LevelGroup> Groups { get; } = [];

    [ObservableProperty]
    public partial string Summary { get; private set; } = string.Empty;

    /// <summary>The next level to play, for the page to scroll to. Null until loaded.</summary>
    public LevelCard? CurrentCard { get; private set; }

    /// <summary>The group holding <see cref="CurrentCard"/> - grouped ScrollTo needs both.</summary>
    public LevelGroup? CurrentGroup { get; private set; }

    public string Heading => T("levelsTitle");

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
        int highest;

        try
        {
            highest = (await _progress.GetProgressAsync()).HighestLevelCompleted;
        }
        catch (Exception)
        {
            // A broken read shows a fresh map rather than no map; playing level 1 is always safe.
            highest = 0;
        }

        // The eight-step lean from the prototype, sampled from a sine so the trail curves.
        double[] offsets = [0, 26, 46, 26, 0, -26, -46, -26];

        var specs = LevelCatalog.All(_puzzles.Puzzles);

        Groups.Clear();
        CurrentCard = null;
        CurrentGroup = null;

        LevelGroup? group = null;

        for (var i = 0; i < specs.Count; i++)
        {
            var spec = specs[i];

            if (i % GroupSize == 0)
            {
                var lastInGroup = Math.Min(spec.Level + GroupSize - 1, LevelCatalog.LevelCount);
                group = new LevelGroup(Strings.Format("levelsRange", spec.Level, lastInGroup));
                Groups.Add(group);
            }

            var state = spec.Level <= highest
                ? LevelNodeState.Done
                : spec.Level == highest + 1 ? LevelNodeState.Current : LevelNodeState.Locked;

            var card = new LevelCard
            {
                Number = spec.Level,
                State = state,
                Caption = state == LevelNodeState.Done ? "★" : spec.Level.ToString(CultureInfo.CurrentCulture),
                Description = Describe(spec, state),
                IsMilestone = spec.IsMilestone,
                Offset = offsets[i % offsets.Length],
                IsLast = i == specs.Count - 1,
            };

            group!.Add(card);

            if (state == LevelNodeState.Current)
            {
                CurrentCard = card;
                CurrentGroup = group;
            }
        }

        Summary = Strings.Format("levelsSummary", Math.Min(highest, LevelCatalog.LevelCount), LevelCatalog.LevelCount);
    }

    /// <summary>
    /// One sentence per level. The milestone badge is decorative, so the surprise it promises is
    /// spoken too - without naming the picture, exactly as the Gallery keeps its secrets.
    /// </summary>
    private string Describe(LevelSpec spec, LevelNodeState state)
    {
        var sentence = state switch
        {
            LevelNodeState.Done => Strings.Format("a11yLevelDone", spec.Level),
            LevelNodeState.Current => Strings.Format("a11yLevelCurrent", spec.Level, spec.Size),
            _ => Strings.Format("a11yLevelLocked", spec.Level),
        };

        return spec.IsMilestone ? $"{sentence} {T("a11yMilestone")}" : sentence;
    }

    /// <summary>Starts a level, unless it is still locked.</summary>
    [RelayCommand]
    private async Task PlayLevelAsync(LevelCard? card)
    {
        if (card is null || !card.IsPlayable)
        {
            return;
        }

        await _navigation.GoToAsync(
            Routes.Game,
            new Dictionary<string, object>
            {
                [GameViewModel.LevelParameter] = card.Number.ToString(CultureInfo.InvariantCulture),
            });
    }
}
