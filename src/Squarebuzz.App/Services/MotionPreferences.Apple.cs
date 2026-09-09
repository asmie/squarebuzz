#if IOS || MACCATALYST
using UIKit;

namespace Squarebuzz.App.Services;

/// <summary>
/// Apple's answer: the Reduce Motion switch under Settings → Accessibility → Motion, which is
/// the same setting on iPhone, iPad and a Mac Catalyst app.
/// </summary>
public static partial class MotionPreferences
{
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
