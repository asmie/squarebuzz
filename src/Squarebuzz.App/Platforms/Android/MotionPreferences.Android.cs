using Android.Provider;
using Application = Android.App.Application;

namespace Squarebuzz.App.Services;

/// <summary>
/// Android's answer: the animator duration scale, which the system's "Remove animations"
/// accessibility switch sets to zero.
/// </summary>
public static partial class MotionPreferences
{
    private static bool GetReduceMotion()
    {
        try
        {
            var scale = Settings.Global.GetFloat(
                Application.Context.ContentResolver,
                Settings.Global.AnimatorDurationScale,
                1f);

            return scale == 0f;
        }
        catch (Exception)
        {
            // An unreadable setting must not stop the game; full motion is the default.
            return false;
        }
    }
}
