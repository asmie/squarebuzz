using System.Globalization;
using Squarebuzz.Core.Progression;

namespace Squarebuzz.App.Converters;

/// <summary>
/// Picks one of three values from a <see cref="PathNodeState"/>, so the trail's colours stay in the
/// markup rather than being computed in the ViewModel.
/// </summary>
/// <remarks>
/// The same shape as <see cref="BoolSelectConverter"/> and for the same reason: colours belong to
/// the theme dictionaries, and a ViewModel that returned a <c>Color</c> would bake in whichever
/// theme happened to be loaded when it ran. Declare instances in the page's own resources, not in
/// <c>App.xaml</c> - the theme dictionaries are merged at runtime, so a <c>StaticResource</c>
/// colour only resolves once a page is being built.
/// </remarks>
public sealed class PathStateSelectConverter : IValueConverter
{
    public object? DoneValue { get; set; }

    public object? CurrentValue { get; set; }

    public object? LockedValue { get; set; }

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is PathNodeState state
            ? state switch
            {
                PathNodeState.Done => DoneValue,
                PathNodeState.Current => CurrentValue,
                _ => LockedValue,
            }
            : LockedValue;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException("The trail's appearance is read-only.");
}
