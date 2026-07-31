using Android.Views.Accessibility;
using Application = Android.App.Application;

namespace Squarebuzz.App.Services;

/// <summary>
/// Android's answer: touch exploration, which is what TalkBack turns on.
/// </summary>
/// <remarks>
/// Touch exploration rather than "any accessibility service is enabled". Plenty of services -
/// switch access, voice access, screen dimmers - are enabled without the player navigating cell by
/// cell, and building four hundred views for them would be a waste. Touch exploration is precisely
/// the mode the overlay exists to serve.
/// </remarks>
public sealed partial class AccessibilityState
{
    // Rooted in a field: the manager holds listeners weakly on some API levels, and a collected
    // listener is a change notification that silently never comes.
    private TouchExplorationListener? _listener;

    private static bool GetIsScreenReaderActive()
    {
        try
        {
            var manager = Manager();

            return manager is { IsEnabled: true, IsTouchExplorationEnabled: true };
        }
        catch (Exception)
        {
            // A missing or refused system service must not stop the game starting.
            return false;
        }
    }

    /// <summary>
    /// Watches for TalkBack being switched on or off while the app runs, so a board that is
    /// already open can grow or shed its cell overlay instead of waiting for the next visit.
    /// </summary>
    partial void PlatformInitialize()
    {
        try
        {
            if (Manager() is not { } manager)
            {
                return;
            }

            // Touch exploration, matching GetIsScreenReaderActive: it flips exactly when the
            // answer to "is a screen reader driving?" flips.
            _listener = new TouchExplorationListener(this);
            manager.AddTouchExplorationStateChangeListener(_listener);
        }
        catch (Exception)
        {
            // No notifications then - the state is still read fresh on every board open.
        }
    }

    partial void DisposePlatform()
    {
        _listener?.Dispose();
        _listener = null;
    }

    private static AccessibilityManager? Manager() =>
        Application.Context.GetSystemService(Android.Content.Context.AccessibilityService)
            as AccessibilityManager;

    private sealed class TouchExplorationListener(AccessibilityState owner)
        : Java.Lang.Object, AccessibilityManager.ITouchExplorationStateChangeListener
    {
        public void OnTouchExplorationStateChanged(bool enabled)
        {
            // Listeners can fire on a system thread; everything downstream touches UI. TalkBack
            // also restarts itself while initialising, so this fires more than once per toggle -
            // harmless, since every consumer re-reads the live state rather than the argument.
            MainThread.BeginInvokeOnMainThread(owner.RaiseChanged);
        }
    }
}
