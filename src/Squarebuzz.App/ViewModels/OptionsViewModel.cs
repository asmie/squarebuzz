using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Squarebuzz.App.Services;
using Squarebuzz.Core.Abstractions;
using Squarebuzz.Core.Model;

namespace Squarebuzz.App.ViewModels;

/// <summary>One language chip in Options.</summary>
public sealed partial class LanguageOption : ObservableObject
{
    /// <summary>Culture code, which is also the command parameter.</summary>
    public required string Code { get; init; }

    /// <summary>The language's own name for itself - never translated.</summary>
    public required string Endonym { get; init; }

    [ObservableProperty]
    public partial bool IsSelected { get; set; }
}

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

    /// <summary>Tail of the save chain - see <see cref="SaveAfterAsync"/>.</summary>
    private Task _pendingSave = Task.CompletedTask;
    private readonly IThemeService _theme;
    private readonly INavigationService _navigation;
    private readonly IScreenTimeMonitor _screenTime;
    private readonly IAudioService _audio;
    private readonly INarrationService _narration;

    private GameSettings _settings = GameSettings.Default;

    /// <summary>Suppresses write-back while the screen is populating its own controls.</summary>
    private bool _isLoading;

    public OptionsViewModel(
        ILocalizationService strings,
        ISettingsRepository settingsRepository,
        IProgressRepository progress,
        IThemeService theme,
        INavigationService navigation,
        IScreenTimeMonitor screenTime,
        IAudioService audio,
        INarrationService narration)
        : base(strings)
    {
        _settingsRepository = settingsRepository;
        _progress = progress;
        _theme = theme;
        _navigation = navigation;
        _screenTime = screenTime;
        _audio = audio;
        _narration = narration;

        Gate = new ParentGate(strings);
    }

    // ---- Audio ----

    [ObservableProperty]
    public partial bool SoundEffects { get; set; }

    [ObservableProperty]
    public partial bool Music { get; set; }

    [ObservableProperty]
    public partial bool VoiceNarration { get; set; }

    /// <summary>
    /// True when the device has no voice installed for the chosen language, in which case the
    /// switch is shown with a note saying so rather than left to fail silently.
    /// </summary>
    public bool HasNoVoice => !_narration.IsAvailable;

    [ObservableProperty]
    public partial bool Haptics { get; set; }

    // ---- Look and feel ----

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLightTheme), nameof(IsDarkTheme), nameof(IsColorBlind))]
    public partial GameTheme Theme { get; set; }

    /// <summary>
    /// The "Auto" third option next to Light and Dark: light/dark comes from the phone.
    /// </summary>
    /// <remarks>
    /// Stored alongside <see cref="Theme"/> rather than as a fourth <see cref="GameTheme"/>,
    /// because the player's explicit choice still has to be remembered - turning Auto off has
    /// to go back to the theme they picked, not to an arbitrary default.
    /// </remarks>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLightTheme), nameof(IsDarkTheme), nameof(IsAutoTheme))]
    public partial bool FollowSystemTheme { get; set; }

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

    // Auto is a third state of the same control, so Light and Dark have to read as unselected
    // while it is on - otherwise two segments look active at once.
    public bool IsLightTheme => !FollowSystemTheme && Theme == GameTheme.Light;

    public bool IsDarkTheme => !FollowSystemTheme && Theme == GameTheme.Dark;

    public bool IsAutoTheme => FollowSystemTheme;

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
    public partial AppLanguage Language { get; set; }

    /// <summary>
    /// Every shipped language as a chip. A list rather than one flag per language: at twenty-two
    /// of them, a property and a converter each would be unreadable, and the set is content -
    /// see <see cref="AppLanguages.All"/>.
    /// </summary>
    public ObservableCollection<LanguageOption> Languages { get; } =
    [
        .. AppLanguages.All.Select(info => new LanguageOption
        {
            Code = info.CultureCode,
            Endonym = info.Endonym,
        })
    ];

    // ---- Parent gate ----

    /// <summary>The shared grown-ups' check in front of the reset. See <see cref="ParentGate"/>.</summary>
    public ParentGate Gate { get; }

    [ObservableProperty]
    public partial bool IsResetConfirmOpen { get; private set; }

    /// <summary>Minutes of play before the break reminder, or null for off.</summary>
    /// <remarks>
    /// Not behind the parent gate. The gate exists to stop a child wiping their own progress;
    /// choosing when to be reminded of a break is not destructive, and putting a sum in front
    /// of it would only stop parents using it.
    /// </remarks>
    [ObservableProperty]
    [NotifyPropertyChangedFor(
        nameof(IsScreenTimeOff),
        nameof(IsScreenTime15),
        nameof(IsScreenTime30),
        nameof(IsScreenTime60))]
    public partial int? ScreenTimeLimitMinutes { get; set; }

    public bool IsScreenTimeOff => ScreenTimeLimitMinutes is null;

    public bool IsScreenTime15 => ScreenTimeLimitMinutes == 15;

    public bool IsScreenTime30 => ScreenTimeLimitMinutes == 30;

    public bool IsScreenTime60 => ScreenTimeLimitMinutes == 60;

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

    public string NoVoiceNote => T("voiceMissing");

    public string HapticsLabel => T("haptics");

    public string ThemeLabel => T("theme");

    public string LightLabel => T("light");

    public string DarkLabel => T("dark");

    public string AutoLabel => T("auto");

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

    // "−" and "+" alone are read as "minus" and "plus", which says nothing about what they change.
    public string SmallerLabel => T("a11ySmaller");

    public string BiggerLabel => T("a11yBigger");

    // The magnifier is new, so these keys were added to AppStrings.resx rather than ported.
    // English only for now, which is exactly how the partial pl/es satellites already behave.
    public string MagnifierLabel => T("magnifier");

    public string MagnifierNote => T("magnifierSub");

    public string AutoCrossLabel => T("autoCross");

    public string WarnLabel => T("warnMistakes");

    public string TimerLabel => T("showTimer");

    public string HintsLabel => T("allowHints");

    public string ScreenTimeLabel => T("screenTime");

    public string ScreenTimeOffLabel => T("stOff");

    public string ScreenTime15Label => T("st15");

    public string ScreenTime30Label => T("st30");

    public string ScreenTime60Label => T("st60");

    public string PrivacyLabel => T("privacy");

    public string PrivacyOpenLabel => T("open");

    public string ResetLabel => T("resetProgress");

    public string ResetAction => T("reset");

    public string ResetTitle => T("resetTitle");

    public string ResetBody => T("resetBody");

    public string ResetConfirm => T("resetYes");

    public string CancelLabel => T("cancel");


    public string VersionLabel => T("version");

    public string NoAdsLabel => T("noAds");

    /// <summary>
    /// Shown after the gated privacy row is unlocked. Like About's links, the destination does
    /// not exist yet, so this says so rather than pretending.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNotice))]
    public partial string Notice { get; private set; } = string.Empty;

    public bool HasNotice => !string.IsNullOrEmpty(Notice);

    /// <summary>Behind the gate: privacy is grown-up reading, and would lead outside the app.</summary>
    [RelayCommand]
    private void Privacy() => Gate.Open(() =>
    {
        ShowNotice($"🔓 {T("privacy")}");
        return Task.CompletedTask;
    });

    private void ShowNotice(string message)
    {
        Notice = message;

        _ = Task.Delay(2500).ContinueWith(
            _ => MainThread.BeginInvokeOnMainThread(() =>
            {
                if (Notice == message)
                {
                    Notice = string.Empty;
                }
            }),
            TaskScheduler.Default);
    }

    protected override void OnLanguageChangedCore() => Gate.RefreshLabels();

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
        FollowSystemTheme = _settings.FollowSystemTheme;
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
        ScreenTimeLimitMinutes = _settings.ScreenTimeLimitMinutes;

        // Explicitly, not only through the change hook: assigning the language it already holds
        // raises nothing, so a screen reopened on a non-default language would show no chip lit.
        SyncLanguageSelection(Language);

        _isLoading = false;

        // The splash already probed for a voice; this just re-reads the answer, since the note
        // under the switch is bound to it and this screen may be built long afterwards.
        OnPropertyChanged(nameof(HasNoVoice));
    }

    // Every control funnels into the same persist step. The generator only calls these hooks for
    // properties that declare one, though, so a missing hook is a silently unsaved setting -
    // Handedness and TapBehaviour were both listed in Persist() but had no hook, so they only
    // ever reached the database if the player happened to change something else afterwards.
    partial void OnSoundEffectsChanged(bool value)
    {
        _audio.Configure(value, Music);

        // Play the sound the switch just turned on. Silence would be an ambiguous answer to
        // "did that work?", and a child needs to hear what they have chosen.
        //
        // Only when the player did it. This hook also runs while the screen is copying saved
        // settings into its own controls, and opening Options should not make a noise - which is
        // exactly what it did until dumpsys showed the effect player had been started on arrival.
        if (value && !_isLoading)
        {
            _audio.Play(GameSound.Fill);
        }

        Persist();
    }

    partial void OnMusicChanged(bool value)
    {
        _audio.Configure(SoundEffects, value);
        Persist();
    }

    partial void OnVoiceNarrationChanged(bool value)
    {
        _narration.Configure(value);

        // Same reasoning as the effects switch: the confirmation is the feature demonstrating
        // itself, and it is the only way a player finds out their device has no voice installed.
        if (value && !_isLoading)
        {
            _narration.Speak(T("voiceReady"));
        }

        Persist();
    }

    partial void OnHapticsChanged(bool value) => Persist();

    partial void OnBigNumbersChanged(bool value) => Persist();

    partial void OnShowMagnifierChanged(bool value) => Persist();

    partial void OnAutoCrossChanged(bool value) => Persist();

    partial void OnWarnOnMistakesChanged(bool value) => Persist();

    partial void OnShowTimerChanged(bool value) => Persist();

    partial void OnAllowHintsChanged(bool value) => Persist();

    partial void OnCellZoomPercentChanged(int value) => Persist();

    partial void OnHandednessChanged(Handedness value) => Persist();

    partial void OnTapBehaviourChanged(TapBehaviour value) => Persist();

    /// <summary>
    /// True while <see cref="OnAppearingAsync"/> is copying stored settings onto the controls, so
    /// a hook can tell "the player changed this" from "the screen is being populated".
    /// </summary>
    /// <remarks>
    /// Side effects have to check it, not only <see cref="Persist"/>. Everything on this screen
    /// is already in force - the splash applied it at launch from the very same row - so
    /// re-applying it while the controls are filled in is pure repetition. Opening Options rebuilt
    /// the whole resource dictionary three times over, once each for the theme, the follow-system
    /// switch and the accent.
    /// </remarks>
    private bool IsPopulatingControls => _isLoading;

    partial void OnThemeChanged(GameTheme value)
    {
        if (IsPopulatingControls)
        {
            return;
        }

        _theme.Apply(value, Accent, FollowSystemTheme);

        // Keep the switch in step when the theme changed from the Light/Dark buttons instead.
        _isLoading = true;
        ColorBlindEnabled = value == GameTheme.ColorBlind;
        _isLoading = false;

        Persist();
    }

    partial void OnFollowSystemThemeChanged(bool value)
    {
        if (IsPopulatingControls)
        {
            return;
        }

        _theme.Apply(Theme, Accent, value);
        Persist();
    }

    partial void OnScreenTimeLimitMinutesChanged(int? value)
    {
        if (IsPopulatingControls)
        {
            return;
        }

        // Applied to the live monitor as well as persisted, so a parent who sets a limit
        // mid-afternoon does not have to restart the game for it to count. Not while populating:
        // the splash already armed the monitor, and re-configuring it here would restart the
        // count every time a parent looked at this screen.
        _screenTime.Configure(value);
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
        if (IsPopulatingControls)
        {
            return;
        }

        _theme.Apply(Theme, value, FollowSystemTheme);
        Persist();
    }

    partial void OnLanguageChanged(AppLanguage value)
    {
        SyncLanguageSelection(value);

        // Applied before persisting so the screen relabels itself the moment it is tapped.
        Strings.SetLanguage(value);

        // A voice is per-language, so switching to Polish on a device with only an English one
        // has to turn the note on and narration off. Fire-and-forget: enumerating voices is slow
        // enough to stall the button, and nothing else waits on the answer.
        _ = RefreshNarrationVoiceAsync(value);

        Persist();
    }

    private async Task RefreshNarrationVoiceAsync(AppLanguage language)
    {
        await _narration.PrepareAsync(language);

        OnPropertyChanged(nameof(HasNoVoice));
    }

    [RelayCommand]
    private void SelectTheme(string theme)
    {
        if (theme == "auto")
        {
            FollowSystemTheme = true;
            return;
        }

        // Picking a brightness explicitly is also how Auto is turned off. Order matters: clear
        // the flag first so the Apply that OnThemeChanged fires is not still following the OS.
        FollowSystemTheme = false;

        Theme = theme switch
        {
            "dark" => GameTheme.Dark,
            "cb" => GameTheme.ColorBlind,
            _ => GameTheme.Light,
        };
    }

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

    /// <summary>Marks the chosen chip, so the highlight follows the setting however it changed.</summary>
    private void SyncLanguageSelection(AppLanguage language)
    {
        var code = language.ToCultureCode();

        foreach (var option in Languages)
        {
            option.IsSelected = string.Equals(option.Code, code, StringComparison.Ordinal);
        }
    }

    [RelayCommand]
    private void SelectScreenTime(string minutes) =>
        ScreenTimeLimitMinutes = int.TryParse(minutes, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;

    [RelayCommand]
    private void ZoomIn() =>
        CellZoomPercent = Math.Min(GameSettings.MaxCellZoomPercent, CellZoomPercent + 10);

    [RelayCommand]
    private void ZoomOut() =>
        CellZoomPercent = Math.Max(GameSettings.MinCellZoomPercent, CellZoomPercent - 10);

    [RelayCommand]
    private void OpenResetGate() => Gate.Open(() =>
    {
        IsResetConfirmOpen = true;
        return Task.CompletedTask;
    });

    [RelayCommand]
    private void CancelReset() => IsResetConfirmOpen = false;

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
            FollowSystemTheme = FollowSystemTheme,
            Accent = Accent,
            BigNumbers = BigNumbers,
            ScreenTimeLimitMinutes = ScreenTimeLimitMinutes,
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

        // Fire and forget so the switches never feel sticky - but *chained*, not loose: two
        // quick toggles otherwise race their writes through the thread pool, and the older
        // snapshot can commit last, quietly undoing the newer change.
        _pendingSave = SaveAfterAsync(_pendingSave, _settings);
    }

    private async Task SaveAfterAsync(Task previous, GameSettings settings)
    {
        // Never faults: the body below swallows its own exception, so awaiting the chain is safe.
        await previous.ConfigureAwait(false);

        try
        {
            await _settingsRepository.SaveAsync(settings).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // A lost preference is a minor annoyance; an error dialog mid-toggle is worse.
        }
    }
}
