using Android.Database;
using Android.OS;
using Android.Provider;
using Application = Android.App.Application;

namespace Squarebuzz.App.Services;

/// <summary>
/// Android's answer: the animator duration scale, which the system's "Remove animations"
/// accessibility switch sets to zero.
/// </summary>
public static partial class MotionPreferences
{
    // One observer for the app's lifetime; controls release their Changed subscriptions on unload.
    private static MotionObserver? _observer;

    static partial void PlatformInitialize()
    {
        var resolver = Application.Context.ContentResolver;
        var uri = Settings.Global.GetUriFor(Settings.Global.AnimatorDurationScale);
        if (resolver is null || uri is null)
        {
            return;
        }

        _observer = new MotionObserver();
        resolver.RegisterContentObserver(uri, notifyForDescendants: false, _observer);
    }

    private sealed class MotionObserver() : ContentObserver(new Handler(Looper.MainLooper!))
    {
        public override void OnChange(bool selfChange) =>
            MainThread.BeginInvokeOnMainThread(Refresh);

        public override void OnChange(bool selfChange, Android.Net.Uri? uri) => OnChange(selfChange);
    }

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
