using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Squarebuzz.App.Drawing;
using Squarebuzz.App.Services;
using Squarebuzz.Core.Abstractions;
using Squarebuzz.Core.Progression;

namespace Squarebuzz.App.ViewModels;

/// <summary>
/// The home screen: mascot, a greeting, the player's stars and streak, and the ways in - the
/// Play campaign first, a quick custom game second, and the rest below.
/// </summary>
public partial class MenuViewModel : LocalizedViewModel
{
    private readonly IProgressRepository _progress;
    private readonly ISaveGameRepository _saveGames;
    private readonly INavigationService _navigation;
    private readonly IClock _clock;

    public MenuViewModel(
        ILocalizationService strings,
        IProgressRepository progress,
        ISaveGameRepository saveGames,
        INavigationService navigation,
        IClock clock)
        : base(strings)
    {
        _progress = progress;
        _saveGames = saveGames;
        _navigation = navigation;
        _clock = clock;
    }

    [ObservableProperty]
    public partial int Stars { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MascotPose))]
    public partial int Streak { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ContinueSubtitle))]
    [NotifyPropertyChangedFor(nameof(HasSaves))]
    public partial int SaveCount { get; private set; }

    [ObservableProperty]
    public partial string Greeting { get; private set; } = string.Empty;

    /// <summary>The mascot celebrates a live streak and simply waits otherwise.</summary>
    public MascotPose MascotPose => Streak > 0 ? MascotPose.Cheer : MascotPose.Idle;

    public bool HasSaves => SaveCount > 0;

    /// <summary>Highest campaign level completed, for the Play button's "Level N" subtitle.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PlaySubtitle))]
    public partial int HighestLevel { get; private set; }

    public string PlayLabel => T("play");

    /// <summary>The next level to play - the last one, once everything is finished.</summary>
    public string PlaySubtitle =>
        Strings.Format("levelN", Math.Min(HighestLevel + 1, LevelCatalog.LevelCount));

    public string QuickGameLabel => T("quickGame");

    public string QuickGameSubtitle => T("quickGameSub");

    public string ContinueLabel => T("continueGame");

    public string TrialsLabel => T("trials");

    public string OptionsLabel => T("options");

    public string AboutLabel => T("about");

    public string TrialsSubtitle => T("dailyReady");

    public string OptionsSubtitle => T("settingsSub");

    public string AboutSubtitle => T("aboutSub");

    public string HowToLabel => T("howToTitle");

    /// <summary>Either a count of unfinished puzzles or an invitation to start one.</summary>
    public string ContinueSubtitle => SaveCount > 0
        ? Strings.Format("inProgress", SaveCount)
        : T("noSaves");

    public override async Task OnAppearingAsync()
    {
        // Refreshed on every appearance, not just construction: the player returns here after
        // finishing a puzzle and the star count must already reflect it.
        try
        {
            var progress = await _progress.GetProgressAsync();

            Stars = progress.Stars;
            Streak = progress.Streak;
            HighestLevel = progress.HighestLevelCompleted;
            SaveCount = await _saveGames.CountAsync();
        }
        catch (Exception)
        {
            // Menu still works without the numbers; better a zero than a dead end.
        }

        Greeting = PickGreeting();
    }

    /// <summary>
    /// Picks the mascot's line.
    /// </summary>
    /// <remarks>
    /// A brand-new player must get the invitation ("Ready when you are"), never the
    /// congratulation ("You're on a roll") - being told you are on a roll before your first
    /// puzzle reads as the game not paying attention. Only once there is progress do the other
    /// lines come into rotation, keyed off the day so the greeting is stable within a session.
    /// </remarks>
    private string PickGreeting()
    {
        if (Stars == 0 && Streak == 0)
        {
            return T("m_menu1");
        }

        // Alternates between "on a roll" and "found new pictures" on later visits.
        var index = 2 + (_clock.Today.DayNumber % 2);
        return T($"m_menu{index}");
    }

    /// <summary>The front door of the game: the 600-level campaign.</summary>
    [RelayCommand]
    private async Task PlayAsync() => await _navigation.GoToAsync(Routes.Levels);

    /// <summary>The old New Game flow, now called Quick game: pick a size and pack yourself.</summary>
    [RelayCommand]
    private async Task NewGameAsync() => await _navigation.GoToAsync(Routes.NewGame);

    [RelayCommand]
    private async Task ContinueAsync() => await _navigation.GoToAsync(Routes.Continue);

    [RelayCommand]
    private async Task TrialsAsync() => await _navigation.GoToAsync(Routes.Trials);

    [RelayCommand]
    private async Task OptionsAsync() => await _navigation.GoToAsync(Routes.Options);

    [RelayCommand]
    private async Task AboutAsync() => await _navigation.GoToAsync(Routes.About);

    /// <summary>
    /// The lessons, one tap from the front door. The design wants them reachable without a game
    /// in progress - a child should not have to start a puzzle to re-read how puzzles work.
    /// </summary>
    [RelayCommand]
    private async Task HowToAsync() => await _navigation.GoToAsync(Routes.HowTo);
}
