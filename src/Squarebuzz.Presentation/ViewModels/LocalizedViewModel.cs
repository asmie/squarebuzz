using Squarebuzz.Presentation.Services;

namespace Squarebuzz.Presentation.ViewModels;

/// <summary>
/// Base for screens whose labels come from resources.
/// </summary>
/// <remarks>
/// Localised labels are exposed as computed properties, and a language change simply raises
/// "everything changed" so all of them re-read at once. That keeps XAML free of indexer
/// bindings against a foreign source, which proved fragile inside nested elements.
/// </remarks>
public abstract class LocalizedViewModel : ViewModelBase, IDisposable
{
    private bool _disposed;

    protected LocalizedViewModel(ILocalizationService strings)
    {
        ArgumentNullException.ThrowIfNull(strings);

        Strings = strings;
        Strings.LanguageChanged += OnLanguageChanged;
    }

    protected ILocalizationService Strings { get; }

    /// <summary>Shorthand for a resource lookup, used by derived screens' text properties.</summary>
    protected string T(string key) => Strings.GetString(key);

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        // An empty name means every property changed - each localised label re-reads.
        OnPropertyChanged(string.Empty);
        OnLanguageChangedCore();
    }

    /// <summary>Override for anything beyond re-reading properties, e.g. rebuilding a list.</summary>
    protected virtual void OnLanguageChangedCore()
    {
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed)
        {
            return;
        }

        if (disposing)
        {
            Strings.LanguageChanged -= OnLanguageChanged;
        }

        _disposed = true;
    }
}
