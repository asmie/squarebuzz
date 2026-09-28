using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Squarebuzz.Presentation.Navigation;
using Squarebuzz.Presentation.Services;
using Squarebuzz.Core.Abstractions;
using Squarebuzz.Core.Model;

namespace Squarebuzz.Presentation.ViewModels;

/// <summary>One row of the language picker in Options.</summary>
public sealed class LanguageOption
{
    /// <summary>Culture code, which is how the row maps back onto <see cref="AppLanguage"/>.</summary>
    public required string Code { get; init; }

    /// <summary>The language's own name for itself - never translated.</summary>
    public required string Endonym { get; init; }
}

/// <summary>Coordinates sound, display, input, language and parent settings.</summary>
/// <remarks>
/// Changes apply immediately and persist without a separate Save action.
/// </remarks>
public partial class OptionsViewModel : LocalizedViewModel
{
    private readonly ISettingsRepository _settingsRepository;
    private readonly GameCompletionService _completions;

    private readonly IThemeService _theme;
    private readonly INavigationService _navigation;
    private readonly IScreenTimeMonitor _screenTime;
    private readonly IAudioService _audio;
    private readonly INarrationService _narration;
    private readonly IUiThread _uiThread;

    private GameSettings _settings = GameSettings.Default;

    /// <summary>Suppresses write-back while the screen is populating its own controls.</summary>
    private bool _isLoading;
    private bool _loadFailed;
    private bool _saveFailed;
    private bool _resetFailed;

    public OptionsViewModel(
        ILocalizationService strings,
        ISettingsRepository settingsRepository,
        GameCompletionService completions,
        IThemeService theme,
        INavigationService navigation,
        IScreenTimeMonitor screenTime,
        IAudioService audio,
        INarrationService narration,
        IUiThread uiThread)
        : base(strings)
    {
        ArgumentNullException.ThrowIfNull(settingsRepository);
        ArgumentNullException.ThrowIfNull(completions);
        ArgumentNullException.ThrowIfNull(theme);
        ArgumentNullException.ThrowIfNull(navigation);
        ArgumentNullException.ThrowIfNull(screenTime);
        ArgumentNullException.ThrowIfNull(audio);
        ArgumentNullException.ThrowIfNull(narration);
        ArgumentNullException.ThrowIfNull(uiThread);

        _settingsRepository = settingsRepository;
        _completions = completions;
        _theme = theme;
        _navigation = navigation;
        _screenTime = screenTime;
        _audio = audio;
        _narration = narration;
        _uiThread = uiThread;

        Gate = new ParentGate(strings);
        Gate.PropertyChanged += OnGateChanged;

        // Notify colour-converter bindings when the theme changes. Dispose removes the subscription.
        _theme.Changed += OnThemeServiceChanged;
    }

    /// <summary>
    /// Re-raises every binding so the selection chips re-read the palette now in force.
    /// </summary>
    /// <remarks>
    /// An empty name means "everything changed", the same lever <see cref="LocalizedViewModel"/>
    /// pulls for a language change. It raises the event only - the generated
    /// <c>On&lt;Name&gt;Changed</c> hooks fire on assignment, so nothing here re-enters Persist.
    /// </remarks>
    private void OnThemeServiceChanged(object? sender, EventArgs e) => OnPropertyChanged(string.Empty);

    private void OnGateChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ParentGate.IsOpen) or null or "")
            OnPropertyChanged(nameof(HasOptionsOverlay));
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _theme.Changed -= OnThemeServiceChanged;
            Gate.PropertyChanged -= OnGateChanged;
        }

        base.Dispose(disposing);
    }

    // ---- Audio ----

    [ObservableProperty]
    public partial bool SoundEffects { get; set; }

    [ObservableProperty]
    public partial bool Music { get; set; }

    [ObservableProperty]
    public partial bool VoiceNarration { get; set; }

    /// <summary>Indicates that narration has no installed voice for the chosen language.</summary>
    public bool HasNoVoice => !_narration.IsAvailable;

    [ObservableProperty]
    public partial bool Haptics { get; set; }

    // ---- Look and feel ----

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLightTheme), nameof(IsDarkTheme), nameof(IsColorBlind))]
    public partial GameTheme Theme { get; set; }

    /// <summary>Follows the system brightness while retaining the explicit theme for when Auto is disabled.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLightTheme), nameof(IsDarkTheme), nameof(IsAutoTheme))]
    public partial bool FollowSystemTheme { get; set; }

    /// <summary>Writable binding for the colour-blind palette switch. Disabling it selects the light theme.</summary>
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
    [NotifyPropertyChangedFor(nameof(CanEditHintLimit), nameof(HasHintLimitError))]
    public partial bool AllowHints { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEditHintLimit), nameof(HasHintLimitError))]
    public partial bool UnlimitedHints { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasHintLimitError))]
    public partial string HintLimitText { get; set; } = "3";

    public bool CanEditHintLimit => AllowHints && !UnlimitedHints;

    public bool HasHintLimitError => CanEditHintLimit && !TryGetHintLimit(out _);

    // ---- Language ----

    [ObservableProperty]
    public partial AppLanguage Language { get; set; }

    /// <summary>
    /// The picker's current row. Kept beside <see cref="Language"/> rather than replacing it
    /// because a <c>Picker</c> binds to an item from its own <see cref="Languages"/> list, while
    /// everything else in the app - persistence, the localisation service - speaks
    /// <see cref="AppLanguage"/>.
    /// </summary>
    [ObservableProperty]
    public partial LanguageOption? SelectedLanguage { get; set; }

    /// <summary>Supported languages in picker order, from AppLanguages.All.</summary>
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
    [NotifyPropertyChangedFor(nameof(HasOptionsOverlay))]
    public partial bool IsResetConfirmOpen { get; private set; }

    /// <summary>Excludes settings covered by the gate or reset confirmation from accessibility.</summary>
    public bool HasOptionsOverlay => Gate.IsOpen || IsResetConfirmOpen;

    /// <summary>Minutes of play before a break reminder, or null to disable it. This setting is outside the parent gate.</summary>
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

    public string Heading => T("options");

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

    // Magnifier labels use the shared localised resources.
    public string MagnifierLabel => T("magnifier");

    public string MagnifierNote => T("magnifierSub");

    public string AutoCrossLabel => T("autoCross");

    public string WarnLabel => T("warnMistakes");

    public string TimerLabel => T("showTimer");

    public string HintsLabel => T("allowHints");

    public string HintLimitLabel => $"{T("hintLimit")} ({T("relaxed")})";

    public string UnlimitedHintsLabel => T("unlimited");

    public string HintLimitError => T("hintLimitInvalid");

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

    /// <summary>Placeholder privacy message shown after the parent gate is passed.</summary>
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
            _ => _uiThread.BeginInvokeOnMainThread(() =>
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
            _loadFailed = false;
            _saveFailed = false;
        }
        catch (OperationCanceledException)
        {
            _isLoading = false;
            return;
        }
        catch (Exception)
        {
            // Keep the last known values; a failed read is not a fresh installation.
            _loadFailed = true;
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
        HintLimitText = (_settings.Helpers.HintBudget.Limit ?? 3).ToString(CultureInfo.CurrentCulture);
        UnlimitedHints = _settings.Helpers.HintBudget.Limit is null;

        Language = _settings.Language;
        ScreenTimeLimitMinutes = _settings.ScreenTimeLimitMinutes;

        // Synchronize the picker even when assigning the current language does not raise a change event.
        SyncLanguageSelection(Language);

        _isLoading = false;
        NotifyPersistenceState();

        // The splash already probed for a voice; this just re-reads the answer, since the note
        // under the switch is bound to it and this screen may be built long afterwards.
        OnPropertyChanged(nameof(HasNoVoice));
    }

    // Every editable setting needs a change hook that calls Persist.
    partial void OnSoundEffectsChanged(bool value)
    {
        _audio.Configure(value, Music);

        // Preview sound only for a user change, not while loading saved settings.
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

        // Preview narration after a user change so voice availability is apparent.
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

    partial void OnHintLimitTextChanged(string value)
    {
        if (TryGetHintLimit(out _))
        {
            Persist();
        }
    }

    partial void OnUnlimitedHintsChanged(bool value)
    {
        if (!value && !TryGetHintLimit(out _))
        {
            HintLimitText = "3";
        }

        Persist();
    }

    private bool TryGetHintLimit(out int limit) =>
        int.TryParse(HintLimitText, NumberStyles.Integer, CultureInfo.CurrentCulture, out limit) && limit >= 1;

    private HintBudget SelectedHintBudget() => UnlimitedHints
        ? HintBudget.Unlimited
        : TryGetHintLimit(out var limit) ? new HintBudget(limit) : _settings.Helpers.HintBudget;

    partial void OnCellZoomPercentChanged(int value) => Persist();

    partial void OnHandednessChanged(Handedness value) => Persist();

    partial void OnTapBehaviourChanged(TapBehaviour value) => Persist();

    /// <summary>True while stored settings are being copied to the controls.</summary>
    /// <remarks>
    /// Change hooks must skip persistence and previews during population.
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

        // Update the active break monitor when the preference changes. Skip this during population
        // to retain the elapsed interval already configured at startup.
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

        // Refresh installed-voice availability asynchronously when the language changes.
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

    /// <summary>Applies a picker selection. Ignore null while the picker rebuilds its items.</summary>
    partial void OnSelectedLanguageChanged(LanguageOption? value)
    {
        if (value is not null)
        {
            Language = AppLanguages.FromCultureCode(value.Code);
        }
    }

    /// <summary>
    /// Points the picker at the current language, so it follows the setting however it changed -
    /// including the load on appearing, where nothing was tapped at all.
    /// </summary>
    private void SyncLanguageSelection(AppLanguage language)
    {
        var code = language.ToCultureCode();

        SelectedLanguage = Languages.FirstOrDefault(
            option => string.Equals(option.Code, code, StringComparison.Ordinal));
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
            await _completions.ResetAsync();
            _resetFailed = false;
            NotifyPersistenceState();
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception)
        {
            _resetFailed = true;
            NotifyPersistenceState();
            return;
        }

        await _navigation.ResetToAsync(Routes.Menu);
    }

    public bool HasPersistenceFailure => _loadFailed || _saveFailed || _resetFailed;
    public bool CanEditSettings => !_loadFailed;
    public string PersistenceFailureText => T("storageUnavailable");
    public string RetryPersistenceText => T("tryAgain");

    private void NotifyPersistenceState() => _uiThread.BeginInvokeOnMainThread(() =>
    {
        OnPropertyChanged(nameof(HasPersistenceFailure));
        OnPropertyChanged(nameof(CanEditSettings));
    });

    [RelayCommand]
    private async Task RetryPersistenceAsync()
    {
        if (_loadFailed) await OnAppearingAsync();
        if (_saveFailed && !_loadFailed) await SaveSettingsAsync(_settings);
        if (_resetFailed) IsResetConfirmOpen = true;
    }

    private void Persist()
    {
        if (_isLoading || _loadFailed)
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
                HintBudget = SelectedHintBudget(),
            },
        };

        // Submit every snapshot immediately. The shared OrderedSettingsRepository sequences
        // writes and reads across screens; a local queue would hide later changes from the
        // game's load when the player goes back before these writes finish.
        _ = SaveSettingsAsync(_settings);
    }

    private async Task SaveSettingsAsync(GameSettings settings)
    {
        try
        {
            await _settingsRepository.SaveAsync(settings);
            if (ReferenceEquals(settings, _settings))
            {
                _saveFailed = false;
                NotifyPersistenceState();
            }
        }
        catch (OperationCanceledException)
        {
            // Cancellation is not a storage failure; retain any earlier recovery state.
        }
        catch (Exception)
        {
            if (ReferenceEquals(settings, _settings))
            {
                _saveFailed = true;
                NotifyPersistenceState();
            }
        }
    }
}
