using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Squarebuzz.App.Services;
using Squarebuzz.Core.Abstractions;
using Squarebuzz.Core.Model;

namespace Squarebuzz.App.ViewModels;

/// <summary>
/// Options: sound, appearance, controls, language, and the grown-ups' section.
/// </summary>
/// <remarks>
/// Every change is applied immediately and written straight through - there is no Save button,
/// because a child should not have to understand one. Theme and language also take effect at
/// once, which is why this screen sets them on the services as well as persisting them.
/// </remarks>
public partial class OptionsViewModel : LocalizedViewModel
{
    private readonly ISettingsRepository _settingsRepository;
    private readonly IProgressRepository _progress;
    private readonly IThemeService _theme;
    private readonly INavigationService _navigation;

    private GameSettings _settings = GameSettings.Default;

    /// <summary>Suppresses write-back while the screen is populating its own controls.</summary>
    private bool _isLoading;

    public OptionsViewModel(
        ILocalizationService strings,
        ISettingsRepository settingsRepository,
        IProgressRepository progress,
        IThemeService theme,
        INavigationService navigation)
        : base(strings)
    {
        _settingsRepository = settingsRepository;
        _progress = progress;
        _theme = theme;
        _navigation = navigation;
    }

    // ---- Audio ----

    [ObservableProperty]
    public partial bool SoundEffects { get; set; }

    [ObservableProperty]
    public partial bool Music { get; set; }

    [ObservableProperty]
    public partial bool VoiceNarration { get; set; }

    [ObservableProperty]
    public partial bool Haptics { get; set; }

    // ---- Look and feel ----

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLightTheme), nameof(IsDarkTheme), nameof(IsColorBlind))]
    public partial GameTheme Theme { get; set; }

    /// <summary>
    /// Two-way companion for the colour-blind switch.
    /// </summary>
    /// <remarks>
    /// The palette is a switch to the player but a third <see cref="GameTheme"/> underneath, and
    /// a <c>Switch</c> cannot bind to a computed read-only flag. This settable property bridges
    /// the two, and turning it off returns to light because "not colour-blind" has to land
    /// somewhere concrete.
    /// </remarks>
    [ObservableProperty]
    public partial bool ColorBlindEnabled { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsTangerine), nameof(IsGrape), nameof(IsTrio))]
    public partial GameAccent Accent { get; set; }

    [ObservableProperty]
    public partial bool BigNumbers { get; set; }

    public bool IsLightTheme => Theme == GameTheme.Light;

    public bool IsDarkTheme => Theme == GameTheme.Dark;

    public bool IsColorBlind => Theme == GameTheme.ColorBlind;

    public bool IsTangerine => Accent == GameAccent.Tangerine;

    public bool IsGrape => Accent == GameAccent.Grape;

    public bool IsTrio => Accent == GameAccent.Trio;

    // ---- Controls ----

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsModeButton), nameof(IsHoldToCross))]
    public partial TapBehaviour TapBehaviour { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLeftHanded), nameof(IsRightHanded))]
    public partial Handedness Handedness { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CellZoomText))]
    public partial int CellZoomPercent { get; set; }

    [ObservableProperty]
    public partial bool ShowMagnifier { get; set; }

    public bool IsModeButton => TapBehaviour == TapBehaviour.ModeButton;

    public bool IsHoldToCross => TapBehaviour == TapBehaviour.HoldToCross;

    public bool IsLeftHanded => Handedness == Handedness.Left;

    public bool IsRightHanded => Handedness == Handedness.Right;

    public string CellZoomText => $"{CellZoomPercent}%";

    // ---- Helpers ----

    [ObservableProperty]
    public partial bool AutoCross { get; set; }

    [ObservableProperty]
    public partial bool WarnOnMistakes { get; set; }

    [ObservableProperty]
    public partial bool ShowTimer { get; set; }

    [ObservableProperty]
    public partial bool AllowHints { get; set; }

    // ---- Language ----

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEnglish), nameof(IsPolish), nameof(IsSpanish))]
    public partial AppLanguage Language { get; set; }

    public bool IsEnglish => Language == AppLanguage.English;

    public bool IsPolish => Language == AppLanguage.Polish;

    public bool IsSpanish => Language == AppLanguage.Spanish;

    // ---- Parent gate ----

    /// <summary>
    /// A small multiplication, so a young child cannot wander into a destructive action alone.
    /// It is a speed bump, not security - and it is honest about being one.
    /// </summary>
    [ObservableProperty]
    public partial bool IsGateOpen { get; private set; }

    [ObservableProperty]
    public partial string GateQuestion { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string GateAnswer { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool GateFailed { get; private set; }

    [ObservableProperty]
    public partial bool IsResetConfirmOpen { get; private set; }

    private int _gateExpectedAnswer;

    // ---- Section and row labels ----

    public string AudioSection => T("audio");

    public string LookSection => T("look");

    public string ControlsSection => T("controls");

    public string HelpersSection => T("helpers");

    public string LanguageSection => T("langGroup");

    public string ParentSection => T("parentZone");

    public string SoundLabel => T("sound");

    public string MusicLabel => T("music");

    public string VoiceLabel => T("voice");

    public string HapticsLabel => T("haptics");

    public string ThemeLabel => T("theme");

    public string LightLabel => T("light");

    public string DarkLabel => T("dark");

    public string ColorBlindLabel => T("cbPalette");

    public string ColorBlindNote => T("cbSub");

    public string AccentLabel => T("accentTheme");

    public string TangerineLabel => T("accTangerine");

    public string GrapeLabel => T("accGrape");

    public string TrioLabel => T("accTrio");

    public string BigNumbersLabel => T("bigNumbers");

    public string TapBehaviourLabel => T("tapBehaviour");

    public string HoldLabel => T("tapFillLong");

    public string ModeLabel => T("tapMode");

    public string HandedLabel => T("handed");

    public string LeftLabel => T("leftH");

    public string RightLabel => T("rightH");

    public string CellSizeLabel => T("cellSize");

    // The magnifier is new, so these keys were added to AppStrings.resx rather than ported.
    // English only for now, which is exactly how the partial pl/es satellites already behave.
    public string MagnifierLabel => T("magnifier");

    public string MagnifierNote => T("magnifierSub");

    public string AutoCrossLabel => T("autoCross");

    public string WarnLabel => T("warnMistakes");

    public string TimerLabel => T("showTimer");

    public string HintsLabel => T("allowHints");

    public string ResetLabel => T("resetProgress");

    public string ResetAction => T("reset");

    public string ResetTitle => T("resetTitle");

    public string ResetBody => T("resetBody");

    public string ResetConfirm => T("resetYes");

    public string CancelLabel => T("cancel");

    /// <summary>Shown when the sum is answered wrongly. The prototype's own wording.</summary>
    public string GateRetryMessage => T("gateRetry");

    public string GrownUpsLabel => T("grownUps");

    public string VersionLabel => T("version");

    public string NoAdsLabel => T("noAds");

    public override async Task OnAppearingAsync()
    {
        _isLoading = true;

        try
        {
            _settings = await _settingsRepository.LoadAsync();
        }
        catch (Exception)
        {
            _settings = GameSettings.Default;
        }

        SoundEffects = _settings.SoundEffects;
        Music = _settings.Music;
        VoiceNarration = _settings.VoiceNarration;
        Haptics = _settings.Haptics;

        Theme = _settings.Theme;
        ColorBlindEnabled = _settings.Theme == GameTheme.ColorBlind;
        Accent = _settings.Accent;
        BigNumbers = _settings.BigNumbers;

        TapBehaviour = _settings.TapBehaviour;
        Handedness = _settings.Handedness;
        CellZoomPercent = _settings.CellZoomPercent;
        ShowMagnifier = _settings.ShowMagnifier;

        AutoCross = _settings.Helpers.AutoCross;
        WarnOnMistakes = _settings.Helpers.WarnOnMistakes;
        ShowTimer = _settings.Helpers.ShowTimer;
        AllowHints = _settings.Helpers.AllowHints;

        Language = _settings.Language;

        _isLoading = false;
    }

    // Every toggle funnels into the same persist step, so no setting can be forgotten.
    partial void OnSoundEffectsChanged(bool value) => Persist();

    partial void OnMusicChanged(bool value) => Persist();

    partial void OnVoiceNarrationChanged(bool value) => Persist();

    partial void OnHapticsChanged(bool value) => Persist();

    partial void OnBigNumbersChanged(bool value) => Persist();

    partial void OnShowMagnifierChanged(bool value) => Persist();

    partial void OnAutoCrossChanged(bool value) => Persist();

    partial void OnWarnOnMistakesChanged(bool value) => Persist();

    partial void OnShowTimerChanged(bool value) => Persist();

    partial void OnAllowHintsChanged(bool value) => Persist();

    partial void OnCellZoomPercentChanged(int value) => Persist();

    partial void OnThemeChanged(GameTheme value)
    {
        _theme.Apply(value, Accent);

        // Keep the switch in step when the theme changed from the Light/Dark buttons instead.
        if (!_isLoading)
        {
            _isLoading = true;
            ColorBlindEnabled = value == GameTheme.ColorBlind;
            _isLoading = false;
        }

        Persist();
    }

    partial void OnColorBlindEnabledChanged(bool value)
    {
        if (_isLoading)
        {
            return;
        }

        Theme = value ? GameTheme.ColorBlind : GameTheme.Light;
    }

    partial void OnAccentChanged(GameAccent value)
    {
        _theme.Apply(Theme, value);
        Persist();
    }

    partial void OnLanguageChanged(AppLanguage value)
    {
        // Applied before persisting so the screen relabels itself the moment it is tapped.
        Strings.SetLanguage(value);
        Persist();
    }

    [RelayCommand]
    private void SelectTheme(string theme) => Theme = theme switch
    {
        "dark" => GameTheme.Dark,
        "cb" => GameTheme.ColorBlind,
        _ => GameTheme.Light,
    };

    [RelayCommand]
    private void SelectAccent(string accent) => Accent = accent switch
    {
        "grape" => GameAccent.Grape,
        "trio" => GameAccent.Trio,
        _ => GameAccent.Tangerine,
    };

    [RelayCommand]
    private void SelectTapBehaviour(string behaviour) =>
        TapBehaviour = behaviour == "hold" ? TapBehaviour.HoldToCross : TapBehaviour.ModeButton;

    [RelayCommand]
    private void SelectHandedness(string hand) =>
        Handedness = hand == "left" ? Handedness.Left : Handedness.Right;

    [RelayCommand]
    private void SelectLanguage(string language) => Language = AppLanguages.FromCultureCode(language);

    [RelayCommand]
    private void ZoomIn() =>
        CellZoomPercent = Math.Min(GameSettings.MaxCellZoomPercent, CellZoomPercent + 10);

    [RelayCommand]
    private void ZoomOut() =>
        CellZoomPercent = Math.Max(GameSettings.MinCellZoomPercent, CellZoomPercent - 10);

    [RelayCommand]
    private void OpenResetGate()
    {
        // Deliberately varied each time, so it cannot be learned by rote.
        var left = 3 + (Environment.TickCount % 6);
        var right = 3 + ((Environment.TickCount / 7) % 6);

        _gateExpectedAnswer = left * right;
        GateQuestion = $"{left} × {right} = ?";
        GateAnswer = string.Empty;
        GateFailed = false;
        IsGateOpen = true;
    }

    [RelayCommand]
    private void SubmitGate()
    {
        if (int.TryParse(GateAnswer, out var answer) && answer == _gateExpectedAnswer)
        {
            IsGateOpen = false;
            IsResetConfirmOpen = true;
            return;
        }

        GateFailed = true;
        GateAnswer = string.Empty;
    }

    [RelayCommand]
    private void CancelGate()
    {
        IsGateOpen = false;
        IsResetConfirmOpen = false;
    }

    [RelayCommand]
    private async Task ConfirmResetAsync()
    {
        IsResetConfirmOpen = false;

        try
        {
            // Settings survive deliberately: erasing progress must not also undo a child's
            // accessibility choices. See SqliteProgressRepository.ResetAsync.
            await _progress.ResetAsync();
        }
        catch (Exception)
        {
            // Nothing useful to tell a parent here; the next screen will simply show zeroes.
        }

        await _navigation.ResetToAsync(Routes.Menu);
    }

    private void Persist()
    {
        if (_isLoading)
        {
            return;
        }

        _settings = _settings with
        {
            SoundEffects = SoundEffects,
            Music = Music,
            VoiceNarration = VoiceNarration,
            Haptics = Haptics,
            Theme = Theme,
            Accent = Accent,
            BigNumbers = BigNumbers,
            TapBehaviour = TapBehaviour,
            Handedness = Handedness,
            CellZoomPercent = CellZoomPercent,
            ShowMagnifier = ShowMagnifier,
            Language = Language,
            Helpers = new HelperSettings
            {
                AutoCross = AutoCross,
                WarnOnMistakes = WarnOnMistakes,
                ShowTimer = ShowTimer,
                AllowHints = AllowHints,
            },
        };

        // Fire and forget: a settings write is small, and blocking a toggle on disk I/O would
        // make the switches feel sticky. Failures are logged by the repository, not surfaced.
        _ = SaveAsync(_settings);
    }

    private async Task SaveAsync(GameSettings settings)
    {
        try
        {
            await _settingsRepository.SaveAsync(settings);
        }
        catch (Exception)
        {
            // A lost preference is a minor annoyance; an error dialog mid-toggle is worse.
        }
    }
}
