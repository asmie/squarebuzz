using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Squarebuzz.Presentation.Navigation;
using Squarebuzz.Presentation.Services;

namespace Squarebuzz.Presentation.ViewModels;

/// <summary>
/// About: what a nonogram is, and the grown-ups' links.
/// </summary>
public partial class AboutViewModel : LocalizedViewModel
{
    private readonly INavigationService _navigation;
    private readonly IUiThread _uiThread;
    private readonly IAppVersion _appVersion;

    // Which notice is showing. Each one gets a fresh token, so the timer of an earlier notice
    // cannot clear a later one - comparing the text did, when the same link was tapped twice.
    private int _noticeToken;

    public AboutViewModel(
        ILocalizationService strings,
        INavigationService navigation,
        IUiThread uiThread,
        IAppVersion appVersion)
        : base(strings)
    {
        ArgumentNullException.ThrowIfNull(navigation);
        ArgumentNullException.ThrowIfNull(uiThread);
        ArgumentNullException.ThrowIfNull(appVersion);

        _navigation = navigation;
        _uiThread = uiThread;
        _appVersion = appVersion;
        Gate = new ParentGate(strings);
    }

    /// <summary>The shared grown-ups' check in front of the outward-facing links.</summary>
    public ParentGate Gate { get; }

    /// <summary>Placeholder notice for external destinations that have not yet been configured.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNotice))]
    public partial string Notice { get; private set; } = string.Empty;

    public bool HasNotice => !string.IsNullOrEmpty(Notice);

    public string Heading => T("about");

    public string WhatIsTitle => T("whatIs");

    public string WhatIsBody => T("whatIsBody");

    public string AlsoCalled => T("alsoCalled");

    public string HowToLabel => T("aboutHowTo");


    public string PrivacyLabel => T("aboutPrivacy");

    public string RateLabel => T("aboutRate");


    /// <summary>The installed version and build, read from the package - see <see cref="IAppVersion"/>.</summary>
    public string VersionLabel => Strings.Format("version", _appVersion.Version, _appVersion.Build);

    public string NoAdsLabel => T("noAds");

    protected override void OnLanguageChangedCore()
    {
        Gate.RefreshLabels();
        Notice = string.Empty;
    }

    [RelayCommand]
    private async Task HowToAsync() => await _navigation.GoToAsync(Routes.HowTo);

    // Privacy and rating both lead outside the app, so each sits behind the gate. A child tapping
    // them should not reach a web page or a store page unaccompanied.
    [RelayCommand]
    private void Privacy() => OpenGated(PrivacyLabel);

    [RelayCommand]
    private void Rate() => OpenGated(RateLabel);

    private void OpenGated(string label) => Gate.Open(() =>
    {
        ShowNotice($"🔓 {label}");
        return Task.CompletedTask;
    });

    private void ShowNotice(string message)
    {
        var token = ++_noticeToken;
        Notice = message;

        _ = Task.Delay(2500).ContinueWith(
            _ => _uiThread.BeginInvokeOnMainThread(() =>
            {
                if (token == _noticeToken)
                {
                    Notice = string.Empty;
                }
            }),
            TaskScheduler.Default);
    }
}
