using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Squarebuzz.App.Services;

namespace Squarebuzz.App.ViewModels;

/// <summary>
/// Placeholder for the screens that are scaffolded but not yet built - Continue, Trials,
/// Options, About, Gallery and How to Play.
/// </summary>
/// <remarks>
/// One page serves all six, taking its heading from a route parameter. That keeps every route
/// real and navigable end to end, so the menu is fully explorable and nothing dead-ends, without
/// six near-identical placeholder files to delete later.
/// </remarks>
[QueryProperty(nameof(TitleKey), "titleKey")]
public partial class ComingSoonViewModel : LocalizedViewModel
{
    private readonly INavigationService _navigation;

    public ComingSoonViewModel(ILocalizationService strings, INavigationService navigation)
        : base(strings)
    {
        _navigation = navigation;
    }

    /// <summary>Resource key for the heading, supplied by whichever route opened this page.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Heading))]
    public partial string TitleKey { get; set; } = string.Empty;

    public string Heading => string.IsNullOrEmpty(TitleKey) ? string.Empty : T(TitleKey);

    public string BackLabel => T("menu");

    [RelayCommand]
    private async Task BackAsync() => await _navigation.GoBackAsync();
}
