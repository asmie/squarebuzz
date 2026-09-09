using System.Globalization;

namespace Squarebuzz.App.Converters;

/// <summary>
/// Picks one of two values based on a boolean - used for selection styling, where a chip needs
/// a different colour, thickness or weight when chosen.
/// </summary>
/// <remarks>
/// <para>
/// Theme colours are named with <see cref="TrueKey"/>/<see cref="FalseKey"/> rather than passed
/// as values, because a <c>StaticResource</c> is resolved once when the page loads. That fixed a
/// chip's selected colour to whatever palette was current at the time: switching accent left
/// every selected chip on the Options screen in the old colour while the page background - a
/// <c>DynamicResource</c> - changed underneath it, so the one screen where the setting lives was
/// the one screen that half-ignored it.
/// </para>
/// <para>
/// A key is looked up in the merged application dictionaries on every conversion, so it always
/// yields the palette in force now. The literal <see cref="TrueValue"/>/<see cref="FalseValue"/>
/// pair remains for things that are not theme colours - a stroke thickness, an opacity - which
/// have no key to look up and never change with the palette.
/// </para>
/// <para>
/// A conversion only happens when the bound property is raised, so a view that must restyle on a
/// theme change still has to say so; <c>OptionsViewModel</c> raises everything after applying one.
/// </para>
/// </remarks>
public sealed class BoolSelectConverter : IValueConverter
{
    /// <summary>Resource key for the selected look. Takes precedence over <see cref="TrueValue"/>.</summary>
    public string? TrueKey { get; set; }

    /// <summary>Resource key for the unselected look. Takes precedence over <see cref="FalseValue"/>.</summary>
    public string? FalseKey { get; set; }

    public object? TrueValue { get; set; }

    public object? FalseValue { get; set; }

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true
            ? Resolve(TrueKey, TrueValue)
            : Resolve(FalseKey, FalseValue);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException("BoolSelectConverter is one-way.");

    /// <summary>
    /// The current value behind <paramref name="key"/>, or <paramref name="fallback"/> when no key
    /// was given or the dictionaries do not hold it.
    /// </summary>
    /// <remarks>
    /// A missing key falls back rather than throwing: a mistyped key should show a wrong colour in
    /// a chip, not take down the screen it is on.
    /// </remarks>
    private static object? Resolve(string? key, object? fallback)
    {
        if (key is null)
        {
            return fallback;
        }

        return Application.Current?.Resources.TryGetValue(key, out var resource) == true
            ? resource
            : fallback;
    }
}

/// <summary>Inverts a boolean, for the common "enabled when not busy" case.</summary>
public sealed class InvertBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is not true;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is not true;
}
