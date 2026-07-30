using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Squarebuzz.App.Services;
using Squarebuzz.Core.Abstractions;
using Squarebuzz.Core.Model;

namespace Squarebuzz.App.ViewModels;

/// <summary>One selectable grid size.</summary>
public sealed partial class SizeOption : ObservableObject
{
    public required int Size { get; init; }

    public required string Label { get; init; }

    /// <summary>True when this size needs a bigger screen than the device has.</summary>
    public required bool IsLocked { get; init; }

    public required string LockedNote { get; init; }

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    public string Caption => $"{Size}×{Size}";
}

/// <summary>One difficulty step, shown as a row of stars.</summary>
public sealed partial class DifficultyOption : ObservableObject
{
    public required int Level { get; init; }

    public required string Label { get; init; }

    public string Stars => new('★', Level);

    [ObservableProperty]
    public partial bool IsSelected { get; set; }
}

/// <summary>One picture pack.</summary>
public sealed partial class PackOption : ObservableObject
{
    public required string Id { get; init; }

    public required string Icon { get; init; }

    public required string Label { get; init; }

    public required bool IsLocked { get; init; }

    [ObservableProperty]
    public partial bool IsSelected { get; set; }
}

/// <summary>
/// The New Game screen: how big, how tricky, how strict, and which pictures.
/// </summary>
public partial class NewGameViewModel : LocalizedViewModel
{
    private readonly IPuzzleRepository _puzzles;
    private readonly ISettingsRepository _settingsRepository;
    private readonly INavigationService _navigation;
    private readonly IDeviceScreen _screen;

    private GameSettings _settings = GameSettings.Default;

    public NewGameViewModel(
        ILocalizationService strings,
        IPuzzleRepository puzzles,
        ISettingsRepository settingsRepository,
        INavigationService navigation,
        IDeviceScreen screen)
        : base(strings)
    {
        _puzzles = puzzles;
        _settingsRepository = settingsRepository;
        _navigation = navigation;
        _screen = screen;
    }

    public ObservableCollection<SizeOption> Sizes { get; } = [];

    public ObservableCollection<DifficultyOption> Difficulties { get; } = [];

    public ObservableCollection<PackOption> Packs { get; } = [];

    [ObservableProperty]
    public partial int SelectedSize { get; private set; } = GridSize.Tiny;

    [ObservableProperty]
    public partial int SelectedDifficulty { get; private set; } = 2;

    [ObservableProperty]
    public partial string SelectedPackId { get; private set; } = "animals";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRelaxed))]
    [NotifyPropertyChangedFor(nameof(IsSharp))]
    [NotifyPropertyChangedFor(nameof(ChallengeNote))]
    public partial ChallengeLevel SelectedChallenge { get; private set; } = ChallengeLevel.Relaxed;

    public bool IsRelaxed => SelectedChallenge == ChallengeLevel.Relaxed;

    public bool IsSharp => SelectedChallenge == ChallengeLevel.Sharp;

    public string GridSizeTitle => T("gridSize");

    public string GridSizeHelp => T("gridSizeHelp");

    public string DifficultyTitle => T("difficulty");

    public string ChallengeTitle => T("challenge");

    public string ChallengeHelp => T("challengeHelp");

    public string RelaxedLabel => T("relaxed");

    public string SharpLabel => T("sharp");

    public string PackTitle => T("pack");

    public string StartLabel => T("start");

    public string GalleryLabel => T("gallery");

    public string ChallengeNote => IsSharp ? T("sharpSub") : T("relaxedSub");

    public override async Task OnAppearingAsync()
    {
        try
        {
            _settings = await _settingsRepository.LoadAsync();
        }
        catch (Exception)
        {
            _settings = GameSettings.Default;
        }

        SelectedSize = _settings.LastSize;
        SelectedDifficulty = _settings.LastDifficulty;
        SelectedPackId = _settings.LastPackId;
        SelectedChallenge = _settings.LastChallenge;

        BuildOptions();
    }

    protected override void OnLanguageChangedCore() => BuildOptions();

    private void BuildOptions()
    {
        var sizeLabels = new Dictionary<int, string>
        {
            [GridSize.Tiny] = T("tiny"),
            [GridSize.Normal] = T("normal"),
            [GridSize.Big] = T("big"),
            [GridSize.Huge] = T("huge"),
            [GridSize.Giant] = T("giant"),
        };

        Sizes.Clear();
        foreach (var size in GridSize.All)
        {
            // The giant grid cannot show hittable cells on a phone, so it is offered only where
            // there is room. See BoardLayout for the arithmetic behind that rule.
            var locked = GridSize.RequiresLargeScreen(size) && !_screen.IsLargeScreen;

            Sizes.Add(new SizeOption
            {
                Size = size,
                Label = sizeLabels[size],
                IsLocked = locked,
                LockedNote = T("tabletOnly"),
                IsSelected = size == SelectedSize,
            });
        }

        var difficultyLabels = new[] { T("veryEasy"), T("easy"), T("medium"), T("hard"), T("expert") };

        Difficulties.Clear();
        for (var level = 1; level <= 5; level++)
        {
            Difficulties.Add(new DifficultyOption
            {
                Level = level,
                Label = difficultyLabels[level - 1],
                IsSelected = level == SelectedDifficulty,
            });
        }

        Packs.Clear();
        foreach (var pack in _puzzles.Packs)
        {
            Packs.Add(new PackOption
            {
                Id = pack.Id,
                Icon = pack.Icon,
                Label = T($"Pack_{pack.Id}"),
                IsLocked = pack.Locked,
                IsSelected = pack.Id == SelectedPackId,
            });
        }
    }

    [RelayCommand]
    private void SelectSize(SizeOption option)
    {
        if (option.IsLocked)
        {
            return;
        }

        SelectedSize = option.Size;

        foreach (var candidate in Sizes)
        {
            candidate.IsSelected = candidate.Size == option.Size;
        }
    }

    [RelayCommand]
    private void SelectDifficulty(DifficultyOption option)
    {
        SelectedDifficulty = option.Level;

        foreach (var candidate in Difficulties)
        {
            candidate.IsSelected = candidate.Level == option.Level;
        }
    }

    [RelayCommand]
    private void SelectPack(PackOption option)
    {
        if (option.IsLocked)
        {
            return;
        }

        SelectedPackId = option.Id;

        foreach (var candidate in Packs)
        {
            candidate.IsSelected = candidate.Id == option.Id;
        }
    }

    [RelayCommand]
    private void SelectChallenge(string level) =>
        SelectedChallenge = string.Equals(level, "sharp", StringComparison.OrdinalIgnoreCase)
            ? ChallengeLevel.Sharp
            : ChallengeLevel.Relaxed;

    /// <summary>
    /// Opens the gallery, which doubles as a picture picker - the prototype reached it from this
    /// screen for the same reason.
    /// </summary>
    [RelayCommand]
    private async Task OpenGalleryAsync() => await _navigation.GoToAsync(Routes.Gallery);

    [RelayCommand]
    private async Task StartAsync()
    {
        // The choices are saved so the next New Game - and the Continue tile - reopen on them.
        try
        {
            await _settingsRepository.SaveAsync(_settings with
            {
                LastSize = SelectedSize,
                LastDifficulty = SelectedDifficulty,
                LastPackId = SelectedPackId,
                LastChallenge = SelectedChallenge,
            });
        }
        catch (Exception)
        {
            // Losing the preference is a small annoyance; refusing to start the game is not.
        }

        await _navigation.GoToAsync(Routes.Game);
    }
}
