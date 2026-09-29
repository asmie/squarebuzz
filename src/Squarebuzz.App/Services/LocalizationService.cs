using System.ComponentModel;
using System.Globalization;
using System.Resources;
using Squarebuzz.Core.Model;
using Squarebuzz.Presentation.Services;

namespace Squarebuzz.App.Services;

/// <summary>Provides localised text and change notifications for live language switching.</summary>
/// <remarks>
/// An empty PropertyChanged name refreshes all indexer bindings. Missing resources return
/// the key so lookup failures do not interrupt rendering.
/// </remarks>
public sealed class LocalizationService : INotifyPropertyChanged, ILocalizationService
{
    private static readonly ResourceManager Resources = new(
        "Squarebuzz.App.Resources.Strings.AppStrings",
        typeof(LocalizationService).Assembly);

    private CultureInfo _culture = CultureFor(AppLanguage.English);

    /// <summary>
    /// Shared instance, needed because XAML bindings reach it through <c>x:Static</c>.
    /// The DI container resolves <see cref="ILocalizationService"/> to this same object, so
    /// code and markup never disagree about the current language.
    /// </summary>
    public static LocalizationService Instance { get; } = new();

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Raised after the language changes, so ViewModels holding composed strings can rebuild
    /// them. Separate from <see cref="PropertyChanged"/>, which exists for XAML bindings.
    /// </summary>
    public event EventHandler? LanguageChanged;

    public AppLanguage Language { get; private set; } = AppLanguage.English;

    /// <summary>Localised string for <paramref name="key"/>, or the key itself if absent.</summary>
    public string this[string key] => GetString(key);

    public string GetString(string key)
    {
        if (string.IsNullOrEmpty(key))
        {
            return string.Empty;
        }

        try
        {
            return Resources.GetString(key, _culture) ?? key;
        }
        catch (MissingManifestResourceException)
        {
            // Use the key when the resource assembly is missing.
            return key;
        }
    }

    /// <summary>Formats positional or single-value {n} placeholders.</summary>
    /// <remarks>
    /// Returns the template unchanged on a format error. tools/check-strings.cs checks resource
    /// placeholder parity before release.
    /// </remarks>
    public string Format(string key, params object[] arguments)
    {
        var template = GetString(key);

        // The prototype's placeholder is "{n}", not a positional "{0}", so the common
        // single-argument case is substituted directly.
        if (arguments.Length == 1 && template.Contains("{n}", StringComparison.Ordinal))
        {
            return template.Replace("{n}", arguments[0]?.ToString() ?? string.Empty, StringComparison.Ordinal);
        }

        try
        {
            return string.Format(_culture, template, arguments);
        }
        catch (FormatException)
        {
            return template;
        }
    }

    public void SetLanguage(AppLanguage language)
    {
        if (language == Language)
        {
            return;
        }

        Language = language;
        _culture = CultureFor(language);

        // Update both this thread and thread defaults so dates, numbers and labels use the selected culture.
        CultureInfo.DefaultThreadCurrentCulture = _culture;
        CultureInfo.DefaultThreadCurrentUICulture = _culture;
        CultureInfo.CurrentUICulture = _culture;
        CultureInfo.CurrentCulture = _culture;

        // An empty property name means "everything changed" - every indexer binding re-reads.
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
        LanguageChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Gets the chosen culture with English, then invariant fallback.</summary>
    /// <remarks>
    /// The persisted language remains unchanged if the device lacks the requested culture.
    /// </remarks>
    private static CultureInfo CultureFor(AppLanguage language)
    {
        try
        {
            return CultureInfo.GetCultureInfo(language.ToCultureCode());
        }
        catch (CultureNotFoundException)
        {
            // Fall through to the defaults below.
        }

        try
        {
            return CultureInfo.GetCultureInfo("en");
        }
        catch (CultureNotFoundException)
        {
            return CultureInfo.InvariantCulture;
        }
    }
}

