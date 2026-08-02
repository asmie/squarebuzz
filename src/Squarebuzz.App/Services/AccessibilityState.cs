namespace Squarebuzz.App.Services;

/// <inheritdoc />
/// <remarks>
/// The default: no platform answer, so no overlay. See the Android partial for the real one.
/// </remarks>
public sealed partial class AccessibilityState : IAccessibilityState, IDisposable
{
    private bool _isScreenReaderActive;

    public AccessibilityState()
    {
        // Lets a platform hook the system's change notifications. Registered once for the
        // app's lifetime - this service is a singleton, so the container disposes it only at
        // process exit.
        PlatformInitialize();

        _isScreenReaderActive = GetIsScreenReaderActive();
    }

    public void Dispose() => DisposePlatform();

    public event EventHandler? ScreenReaderStateChanged;

    /// <inheritdoc />
    public bool IsScreenReaderActive => _isScreenReaderActive;

    /// <inheritdoc />
    public void Refresh() => RaiseChanged();

    /// <summary>
    /// Re-reads the system state; raises <see cref="ScreenReaderStateChanged"/> only if it moved.
    /// </summary>
    /// <remarks>
    /// The platform listener is the main caller. TalkBack restarts itself while initialising, so
    /// it fires several times per toggle; comparing against the cached value turns that into one
    /// notification and spares consumers a redundant rebuild of four hundred overlay views.
    /// </remarks>
    private void RaiseChanged()
    {
        var active = GetIsScreenReaderActive();

        if (active == _isScreenReaderActive)
        {
            return;
        }

        _isScreenReaderActive = active;
        ScreenReaderStateChanged?.Invoke(this, EventArgs.Empty);
    }

    partial void PlatformInitialize();

    partial void DisposePlatform();

#if !ANDROID
    private static bool GetIsScreenReaderActive() => false;
#endif
}
