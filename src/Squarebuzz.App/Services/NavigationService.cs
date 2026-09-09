using Squarebuzz.Presentation.Services;

namespace Squarebuzz.App.Services;

/// <inheritdoc />
/// <remarks>
/// <see cref="Shell.Current"/> is null until the window has been built, and during teardown. A
/// navigation asked for in either gap has nowhere to go; it becomes a no-op rather than a null
/// dereference, matching <see cref="DispatcherGameTimer"/>'s stance when there is no dispatcher
/// yet. Every other service in this folder already guards its platform static - this was the one
/// that did not.
/// </remarks>
public sealed class ShellNavigationService : INavigationService
{
    public Task GoToAsync(string route) => Navigate(shell => shell.GoToAsync(route));

    public Task GoToAsync(string route, IDictionary<string, object> parameters) =>
        Navigate(shell => shell.GoToAsync(route, parameters));

    /// <summary>
    /// The leading "//" makes Shell replace the navigation stack rather than push onto it, so
    /// the back gesture cannot take a child back to the splash or onboarding screens.
    /// </summary>
    public Task ResetToAsync(string route) => Navigate(shell => shell.GoToAsync($"//{route}"));

    public Task GoBackAsync() => Navigate(shell => shell.GoToAsync(".."));

    private static Task Navigate(Func<Shell, Task> action) =>
        Shell.Current is { } shell ? action(shell) : Task.CompletedTask;
}
