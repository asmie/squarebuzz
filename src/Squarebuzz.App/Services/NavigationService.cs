namespace Squarebuzz.App.Services;

/// <summary>
/// Wraps Shell navigation so ViewModels do not reach for <c>Shell.Current</c> directly - which
/// is a static that cannot be faked in a test.
/// </summary>
public interface INavigationService
{
    Task GoToAsync(string route);

    Task GoToAsync(string route, IDictionary<string, object> parameters);

    /// <summary>Replaces the whole stack, for one-way transitions like splash to menu.</summary>
    Task ResetToAsync(string route);

    Task GoBackAsync();
}

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
