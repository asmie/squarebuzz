using Windows.UI.ViewManagement;

namespace Squarebuzz.App.Services;

/// <summary>
/// Windows' answer: the "Animation effects" switch under Settings → Accessibility → Visual
/// effects, which the system exposes as <see cref="UISettings.AnimationsEnabled"/>.
/// </summary>
public static partial class MotionPreferences
{
    // One instance for the app's lifetime; the property reads the live setting each time.
    private static readonly Lazy<UISettings> Settings = new(() => new UISettings());

    static partial void PlatformInitialize()
    {
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041))
        {
            Settings.Value.AnimationsEnabledChanged += (_, _) =>
                MainThread.BeginInvokeOnMainThread(Refresh);
        }
    }

    private static bool GetReduceMotion()
    {
        try
        {
            return !Settings.Value.AnimationsEnabled;
        }
        catch (Exception)
        {
            // An unreadable setting must not stop the game; full motion is the default.
            return false;
        }
    }
}
