using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Squarebuzz.App.Drawing;
using Squarebuzz.App.Services;
using Squarebuzz.Core.Abstractions;
using Squarebuzz.Core.Model;

namespace Squarebuzz.App.ViewModels;

/// <summary>
/// The three cards a new player sees: what the numbers mean, why to mark X, and that a picture
/// appears. Skippable, and shown only once.
/// </summary>
public partial class OnboardingViewModel : LocalizedViewModel
{
    private const int StepCount = 3;

    private readonly ISettingsRepository _settingsRepository;
    private readonly INavigationService _navigation;

    public OnboardingViewModel(
        ILocalizationService strings,
        ISettingsRepository settingsRepository,
        INavigationService navigation)
        : base(strings)
    {
        _settingsRepository = settingsRepository;
        _navigation = navigation;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StepTitle))]
    [NotifyPropertyChangedFor(nameof(StepBody))]
    [NotifyPropertyChangedFor(nameof(NextLabel))]
    [NotifyPropertyChangedFor(nameof(IsLastStep))]
    [NotifyPropertyChangedFor(nameof(DemoPuzzle))]
    [NotifyPropertyChangedFor(nameof(MascotPose))]
    [NotifyPropertyChangedFor(nameof(IsOnStepOne))]
    [NotifyPropertyChangedFor(nameof(IsOnStepTwo))]
    [NotifyPropertyChangedFor(nameof(IsOnStepThree))]
    public partial int Step { get; private set; }

    // Three pips rather than a bound collection: the count is fixed at three, so booleans keep
    // both the ViewModel and the markup simpler than an ObservableCollection would.
    public bool IsOnStepOne => Step == 0;

    public bool IsOnStepTwo => Step == 1;

    public bool IsOnStepThree => Step == 2;

    public string SkipLabel => T("skip");

    public string StepTitle => T($"onb{Step + 1}t");

    public string StepBody => T($"onb{Step + 1}b");

    public bool IsLastStep => Step == StepCount - 1;

    public string NextLabel => IsLastStep ? T("letsPlay") : T("next");

    /// <summary>Cheer on the final card, where the picture is revealed.</summary>
    public MascotPose MascotPose => IsLastStep ? Drawing.MascotPose.Cheer : Drawing.MascotPose.Think;

    /// <summary>
    /// A tiny picture illustrating each card. The last card shows a complete heart, because
    /// that card's promise is "finish every line and a picture appears".
    /// </summary>
    public Puzzle DemoPuzzle => Step switch
    {
        0 => Puzzle.FromRows("demo1", "demo", "#FF8A3D", ["##.#.", ".....", ".....", ".....", "....."]),
        1 => Puzzle.FromRows("demo2", "demo", "#4FA8F5", ["##.#.", "..###", "#..#.", ".....", "....."]),
        _ => Puzzle.FromRows("demo3", "demo", "#FF6B8A", [".#.#.", "#####", "#####", ".###.", "..#.."]),
    };

    [RelayCommand]
    private async Task NextAsync()
    {
        if (!IsLastStep)
        {
            Step++;
            return;
        }

        await FinishAsync();
    }

    [RelayCommand]
    private async Task SkipAsync() => await FinishAsync();

    private async Task FinishAsync()
    {
        try
        {
            var settings = await _settingsRepository.LoadAsync();
            await _settingsRepository.SaveAsync(settings with { HasSeenOnboarding = true });
        }
        catch (Exception)
        {
            // Worst case the cards appear again next launch. Not worth blocking play over.
        }

        await _navigation.ResetToAsync(Routes.Menu);
    }
}
