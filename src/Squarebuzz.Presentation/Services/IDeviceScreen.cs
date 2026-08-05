namespace Squarebuzz.Presentation.Services;

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
