using Squarebuzz.Core.Model;

namespace Squarebuzz.Presentation.Services;

/// <summary>
/// Injectable view of the app's localisation: localised text lookup, formatting, and the
/// current language. ViewModels depend on this so their composed strings are testable; the
/// MAUI head supplies the resource-backed implementation.
/// </summary>
public interface ILocalizationService
{
    /// <summary>
    /// Raised after the language changes, so ViewModels holding composed strings can rebuild
    /// them.
    /// </summary>
    event EventHandler? LanguageChanged;

    AppLanguage Language { get; }

    /// <summary>Localised string for <paramref name="key"/>, or the key itself if absent.</summary>
    string GetString(string key);

    /// <summary>Formats a localised string, e.g. "{n} in progress".</summary>
    string Format(string key, params object[] arguments);

    void SetLanguage(AppLanguage language);
}
