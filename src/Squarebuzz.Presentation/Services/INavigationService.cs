namespace Squarebuzz.Presentation.Services;

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
