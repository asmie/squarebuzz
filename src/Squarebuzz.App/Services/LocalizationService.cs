using System.ComponentModel;
using System.Globalization;
using System.Resources;
using Squarebuzz.Core.Model;

namespace Squarebuzz.App.Services;

/// <summary>
/// Supplies localised text and lets the language change without restarting the app.
/// </summary>
/// <remarks>
/// <para>
/// XAML binds through the indexer - <c>{Binding [menu_newGame], Source={x:Static
/// svc:Localization.Instance}}</c> - because a plain generated resource accessor is a static
/// property and cannot raise change notifications. Raising <see cref="PropertyChanged"/> with
/// an empty name on language change re-evaluates every one of those bindings at once, which is
/// what makes the Options screen update live the way the prototype did.
/// </para>
/// <para>
/// Missing keys return the key itself rather than throwing or showing blank: a forgotten
/// translation should be obvious in the UI but must never crash a child's game.
/// </para>
/// </remarks>
public sealed class LocalizationService : INotifyPropertyChanged
{
    private static readonly ResourceManager Resources = new(
        "Squarebuzz.App.Resources.Strings.AppStrings",
        typeof(LocalizationService).Assembly);

    private CultureInfo _culture = CultureInfo.GetCultureInfo("en");

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
            // The resource assembly is missing entirely - almost certainly a build problem.
            // Showing keys is far better than taking the app down.
            return key;
        }
    }

    /// <summary>Formats a localised string, e.g. "{n} in progress".</summary>
    public string Format(string key, params object[] arguments)
    {
        var template = GetString(key);

        // The prototype's placeholder is "{n}", not a positional "{0}", so the common
        // single-argument case is substituted directly.
        if (arguments.Length == 1 && template.Contains("{n}", StringComparison.Ordinal))
        {
            return template.Replace("{n}", arguments[0]?.ToString() ?? string.Empty, StringComparison.Ordinal);
        }

        return string.Format(_culture, template, arguments);
    }

    public void SetLanguage(AppLanguage language)
    {
        if (language == Language)
        {
            return;
        }

        Language = language;
        _culture = CultureInfo.GetCultureInfo(language.ToCultureCode());

        // Affects date and number formatting too, not just our own strings.
        CultureInfo.CurrentUICulture = _culture;
        CultureInfo.CurrentCulture = _culture;

        // An empty property name means "everything changed" - every indexer binding re-reads.
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
        LanguageChanged?.Invoke(this, EventArgs.Empty);
    }
}

/// <summary>Injectable view of <see cref="LocalizationService"/>.</summary>
public interface ILocalizationService
{
    event EventHandler? LanguageChanged;

    AppLanguage Language { get; }

    string GetString(string key);

    string Format(string key, params object[] arguments);

    void SetLanguage(AppLanguage language);
}

/// <summary>
/// Adapts the singleton to the interface, so ViewModels take a dependency they can fake while
/// XAML still reaches the one shared instance.
/// </summary>
public sealed class LocalizationServiceAdapter : ILocalizationService
{
    public event EventHandler? LanguageChanged
    {
        add => LocalizationService.Instance.LanguageChanged += value;
        remove => LocalizationService.Instance.LanguageChanged -= value;
    }

    public AppLanguage Language => LocalizationService.Instance.Language;

    public string GetString(string key) => LocalizationService.Instance.GetString(key);

    public string Format(string key, params object[] arguments) => LocalizationService.Instance.Format(key, arguments);

    public void SetLanguage(AppLanguage language) => LocalizationService.Instance.SetLanguage(language);
}
