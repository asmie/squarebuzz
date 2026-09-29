using Squarebuzz.Presentation.Services;

namespace Squarebuzz.App.Services;

/// <inheritdoc />
public sealed class DeviceScreen : IDeviceScreen
{
    /// <summary>The prototype's breakpoint for unlocking the giant grid.</summary>
    private const double LargeScreenWidth = 760;

    /// <summary>Width in device-independent units.</summary>
    private static double Width
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
