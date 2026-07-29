namespace Squarebuzz.App.Services;

/// <summary>
/// Reports whether there is enough screen for the largest grid.
/// </summary>
/// <remarks>
/// Behind an interface so screen-dependent rules can be tested, and so the check lives in one
/// place rather than being re-derived from <c>DeviceInfo</c> at each call site.
/// </remarks>
public interface IDeviceScreen
{
    /// <summary>True on tablets and desktop, where the 25x25 grid becomes playable.</summary>
    bool IsLargeScreen { get; }

    /// <summary>Width in device-independent units.</summary>
    double Width { get; }
}

/// <inheritdoc />
public sealed class DeviceScreen : IDeviceScreen
{
    /// <summary>The prototype's breakpoint for unlocking the giant grid.</summary>
    private const double LargeScreenWidth = 760;

    public double Width
    {
        get
        {
            var display = DeviceDisplay.MainDisplayInfo;

            // Density converts physical pixels to the units layout actually works in.
            return display.Density > 0 ? display.Width / display.Density : display.Width;
        }
    }

    public bool IsLargeScreen => Width >= LargeScreenWidth || DeviceInfo.Idiom == DeviceIdiom.Tablet
                                 || DeviceInfo.Idiom == DeviceIdiom.Desktop;
}
