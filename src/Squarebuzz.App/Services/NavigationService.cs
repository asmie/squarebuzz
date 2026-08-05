using Squarebuzz.Presentation.Services;

namespace Squarebuzz.App.Services;

/// <inheritdoc />
public sealed class ShellNavigationService : INavigationService
{
    public Task GoToAsync(string route) => Shell.Current.GoToAsync(route);

    public Task GoToAsync(string route, IDictionary<string, object> parameters) =>
        Shell.Current.GoToAsync(route, parameters);

    /// <summary>
    /// The leading "//" makes Shell replace the navigation stack rather than push onto it, so
    /// the back gesture cannot take a child back to the splash or onboarding screens.
    /// </summary>
    public Task ResetToAsync(string route) => Shell.Current.GoToAsync($"//{route}");

    public Task GoBackAsync() => Shell.Current.GoToAsync("..");
}
