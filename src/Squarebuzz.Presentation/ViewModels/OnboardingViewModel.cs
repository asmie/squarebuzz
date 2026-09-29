using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Squarebuzz.Presentation.Navigation;
using Squarebuzz.Presentation.Services;
using Squarebuzz.Core.Abstractions;
using Squarebuzz.Core.Model;

namespace Squarebuzz.Presentation.ViewModels;

/// <summary>
/// The three cards a new player sees: what the numbers mean, why to mark X, and that a picture
/// appears. Skippable, and shown only once.
/// </summary>
public partial class OnboardingViewModel : LocalizedViewModel
{
    private const int StepCount = 3;

    private readonly ISettingsRepository _settingsRepository;
    private readonly INavigationService _navigation;
    private readonly INarrationService _narration;

    public OnboardingViewModel(
        ILocalizationService strings,
        ISettingsRepository settingsRepository,
        INavigationService navigation,
        INarrationService narration)
        : base(strings)
    {
        ArgumentNullException.ThrowIfNull(settingsRepository);
        ArgumentNullException.ThrowIfNull(navigation);
        ArgumentNullException.ThrowIfNull(narration);

        _settingsRepository = settingsRepository;
        _navigation = navigation;
        _narration = narration;
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
    public MascotPose MascotPose => IsLastStep ? MascotPose.Cheer : MascotPose.Think;

    /// <summary>
    /// A tiny picture illustrating each card. The last card shows a complete heart, because
    /// that card's promise is "finish every line and a picture appears".
    /// </summary>
    /// <remarks>
    /// Built once: a puzzle is immutable, and rebuilding one on every read re-derived all of its
    /// clues each time a binding asked.
    /// </remarks>
    public Puzzle DemoPuzzle => DemoPuzzles[Math.Clamp(Step, 0, DemoPuzzles.Length - 1)];

    private static readonly Puzzle[] DemoPuzzles =
    [
        Puzzle.FromRows("demo1", "demo", "#FF8A3D", ["##.#.", ".....", ".....", ".....", "....."]),
        Puzzle.FromRows("demo2", "demo", "#4FA8F5", ["##.#.", "..###", "#..#.", ".....", "....."]),
        Puzzle.FromRows("demo3", "demo", "#FF6B8A", [".#.#.", "#####", "#####", ".###.", "..#.."]),
    ];

    /// <summary>
    /// Reads the card aloud. These three cards explain the entire game in prose, to a player who
    /// by definition has never seen it - so if narration is ever going to matter, it is here.
    /// </summary>
    public override Task OnAppearingAsync()
    {
        NarrateStep();

        return Task.CompletedTask;
    }

    partial void OnStepChanged(int value) => NarrateStep();

    private void NarrateStep() => _narration.Speak($"{StepTitle}. {StepBody}");

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
        // Stop onboarding narration before navigating to the menu.
        _narration.StopSpeaking();

        // Handle the settings read and completion write independently so a read failure does not
        // prevent recording that onboarding is finished.
        GameSettings settings;

        try
        {
            settings = await _settingsRepository.LoadAsync();
        }
        catch (Exception)
        {
            settings = GameSettings.Default;
        }

        try
        {
            // Only the flag: after a failed read the snapshot is the defaults, and writing it all
            // back would reset a language the player had already picked.
            await _settingsRepository.SaveChangesAsync(settings, settings with { HasSeenOnboarding = true });
        }
        catch (Exception)
        {
            // Worst case the cards appear again next launch. Not worth blocking play over.
        }

        await _navigation.ResetToAsync(Routes.Menu);
    }
}
