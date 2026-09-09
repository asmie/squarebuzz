using System.ComponentModel;
using System.Globalization;
using System.Resources;
using Squarebuzz.Core.Model;
using Squarebuzz.Presentation.Services;

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
            // The resource assembly is missing entirely - almost certainly a build problem.
            // Showing keys is far better than taking the app down.
            return key;
        }
    }

    /// <summary>Formats a localised string, e.g. "{n} in progress".</summary>
    /// <remarks>
    /// Never throws, for the same reason <see cref="GetString"/> does not: the templates are
    /// hand-translated in thirty-nine languages, and a stray brace or a <c>{1}</c> where the code
    /// passes one argument is a translation slip, not a reason to take the board down at render
    /// time. The template comes back as-is, which is ugly and obvious - the right outcome for a
    /// mistake somebody has to go and fix. <c>tools/check-strings.cs</c> catches these before
    /// they ship; this is the net under it.
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

        // Affects date and number formatting too, not just our own strings. The Default* pair
        // matters as much as the Current* pair: Current only changes *this* thread, and the
        // saved language is applied during startup on whichever thread the settings load
        // happened to finish on - without the defaults, every other thread keeps formatting
        // dates in the device language while the labels around them speak the chosen one.
        CultureInfo.DefaultThreadCurrentCulture = _culture;
        CultureInfo.DefaultThreadCurrentUICulture = _culture;
        CultureInfo.CurrentUICulture = _culture;
        CultureInfo.CurrentCulture = _culture;

        // An empty property name means "everything changed" - every indexer binding re-reads.
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
        LanguageChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// The culture for a shipped language, or the nearest one this device can actually supply.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="CultureInfo.GetCultureInfo(string)"/> throws when the platform's ICU data does
    /// not know the code. Directory.Build.props keeps invariant globalisation off for exactly this
    /// reason, but a trimmed or unusual OS image can still lack a culture - and the one place that
    /// would have surfaced it was the language picker, taking the app down on the tap.
    /// </para>
    /// <para>
    /// The fallback is English, then invariant. <see cref="Language"/> still records what the
    /// player chose: the choice is theirs and is persisted; the culture is what this device can
    /// do with it, and a later device may do better.
    /// </para>
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
