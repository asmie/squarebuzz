using Foundation;
using UIKit;

namespace Squarebuzz.App.Services;

/// <summary>
/// Apple's answer: VoiceOver, on iPhone, iPad and a Mac Catalyst app alike.
/// </summary>
/// <remarks>
/// VoiceOver specifically, not "any assistive technology": Switch Control, Voice Control and
/// Zoom are all enabled without the player navigating cell by cell, and building four hundred
/// overlay views for them would be a waste. VoiceOver is precisely the mode the overlay serves -
/// the same reasoning as the Android partial's choice of touch exploration.
/// </remarks>
public sealed partial class AccessibilityState
{
    // Rooted in a field so the observer token stays alive; dropping it unsubscribes.
    private NSObject? _observer;

    private static bool GetIsScreenReaderActive()
    {
        try
        {
            return UIAccessibility.IsVoiceOverRunning;
        }
        catch (Exception)
        {
            // An unreadable state must not stop the game starting.
            return false;
        }
    }

    /// <summary>
    /// Watches for VoiceOver being switched on or off while the app runs, so a board that is
    /// already open can grow or shed its cell overlay instead of waiting for the next visit.
    /// </summary>
    partial void PlatformInitialize()
    {
        try
        {
            _observer = UIAccessibility.Notifications.ObserveVoiceOverStatusDidChange((_, _) =>
            {
                // Delivered on the main thread already, but marshalled anyway: everything
                // downstream touches UI, and RaiseChanged drops repeats by re-reading the state.
                MainThread.BeginInvokeOnMainThread(RaiseChanged);
            });
        }
        catch (Exception)
        {
            // No notifications then - the state is still read fresh on every board open.
        }
    }

    partial void DisposePlatform()
    {
        _observer?.Dispose();
        _observer = null;
    }
}
