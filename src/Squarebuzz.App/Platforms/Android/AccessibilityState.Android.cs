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
    private static bool GetIsScreenReaderActive()
    {
        try
        {
            var manager = Application.Context.GetSystemService(
                Android.Content.Context.AccessibilityService) as AccessibilityManager;

            return manager is { IsEnabled: true, IsTouchExplorationEnabled: true };
        }
        catch (Exception)
        {
            // A missing or refused system service must not stop the game starting.
            return false;
        }
    }
}
