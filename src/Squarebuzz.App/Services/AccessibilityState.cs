namespace Squarebuzz.App.Services;

/// <inheritdoc />
/// <remarks>
/// The default: no platform answer, so no overlay. See the Android partial for the real one.
/// </remarks>
public sealed partial class AccessibilityState : IAccessibilityState
{
    public event EventHandler? ScreenReaderStateChanged;

    public bool IsScreenReaderActive => GetIsScreenReaderActive();

    /// <summary>Raised by a platform implementation when the system state changes.</summary>
    private void RaiseChanged() => ScreenReaderStateChanged?.Invoke(this, EventArgs.Empty);

#if !ANDROID
    private static bool GetIsScreenReaderActive() => false;
#endif
}
