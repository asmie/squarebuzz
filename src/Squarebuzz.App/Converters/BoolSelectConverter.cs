using System.Globalization;

namespace Squarebuzz.App.Converters;

/// <summary>
/// Picks one of two values based on a boolean - used for selection styling, where a chip needs
/// a different colour, thickness or weight when chosen.
/// </summary>
/// <remarks>
/// The two values are supplied as <c>StaticResource</c> colours, so a chip's selected look is
/// fixed when the page loads rather than following a later theme change. Screens are rebuilt on
/// navigation, so in practice the only visible effect is that changing theme while a chooser is
/// open needs the screen re-entered.
/// </remarks>
public sealed class BoolSelectConverter : IValueConverter
{
    public object? TrueValue { get; set; }

    public object? FalseValue { get; set; }

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? TrueValue : FalseValue;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException("BoolSelectConverter is one-way.");
}

/// <summary>Inverts a boolean, for the common "enabled when not busy" case.</summary>
public sealed class InvertBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is not true;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is not true;
}
