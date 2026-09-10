namespace Squarebuzz.App.Services;

/// <summary>
/// Whether the player has asked the operating system to cut animation down.
/// </summary>
/// <remarks>
/// <para>
/// Controls check this before starting animation; with it set, marks land instantly,
/// the mascot sits still and the win screen skips the confetti. For a player with a vestibular
/// disorder this is not a preference but a requirement, which is why it is the OS setting being
/// honoured rather than an in-app switch a parent would have to find twice.
/// </para>
/// <para>
/// Static rather than an injected service: like <c>MainThread</c>, it is read-only environment
/// state needed by controls that do not participate in dependency injection. Read fresh on every
/// query. Loaded controls also listen for changes to stop repeating effects, and window resume
/// refreshes the state in case a platform notification was missed while backgrounded.
/// </para>
/// </remarks>
public static partial class MotionPreferences
{
    private static bool _lastKnownReduceMotion;

    static MotionPreferences()
    {
        _lastKnownReduceMotion = GetReduceMotion();
        try
        {
            PlatformInitialize();
        }
        catch (Exception)
        {
            // Live reads and the resume refresh still work if notifications are unavailable.
        }
    }

    /// <summary>Raised on the UI thread when the OS preference changes.</summary>
    public static event EventHandler? Changed;

    /// <summary>True when animations should be skipped.</summary>
    public static bool ReduceMotion => GetReduceMotion();

    /// <summary>Re-reads the preference on the UI thread after a notification or window resume.</summary>
    public static void Refresh()
    {
        var reduced = GetReduceMotion();
        if (reduced == _lastKnownReduceMotion)
        {
            return;
        }

        _lastKnownReduceMotion = reduced;
        Changed?.Invoke(null, EventArgs.Empty);
    }

    static partial void PlatformInitialize();

    // Each shipped platform supplies its own answer in Platforms/. This fallback is only for a
    // head none of them cover - and until every head had a partial, it was silently the answer
    // on iOS, Mac Catalyst and Windows too, so the OS setting was honoured on Android alone.
#if !ANDROID && !IOS && !MACCATALYST && !WINDOWS
    private static bool GetReduceMotion() => false;
#endif
}
