#if IOS || MACCATALYST
using Foundation;
using UIKit;

namespace Squarebuzz.App.Services;

/// <summary>
/// Apple's answer: the Reduce Motion switch under Settings → Accessibility → Motion, which is
/// the same setting on iPhone, iPad and a Mac Catalyst app.
/// </summary>
public static partial class MotionPreferences
{
    // Keep the notification token alive for the app's lifetime.
    private static NSObject? _observer;

    static partial void PlatformInitialize() =>
        _observer = UIApplication.Notifications.ObserveReduceMotionStatusDidChange((_, _) =>
            MainThread.BeginInvokeOnMainThread(Refresh));

    private static bool GetReduceMotion()
    {
        try
        {
            return UIAccessibility.IsReduceMotionEnabled;
        }
        catch (Exception)
        {
            // An unreadable setting must not stop the game; full motion is the default.
            return false;
        }
    }
}

#endif
