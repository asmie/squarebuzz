namespace Squarebuzz.App.Services;

/// <summary>
/// Whether the player has asked the operating system to cut animation down.
/// </summary>
/// <remarks>
/// <para>
/// Every animation in the game checks this before it runs; with it set, marks land instantly,
/// the mascot sits still and the win screen skips the confetti. For a player with a vestibular
/// disorder this is not a preference but a requirement, which is why it is the OS setting being
/// honoured rather than an in-app switch a parent would have to find twice.
/// </para>
/// <para>
/// Static rather than an injected service: like <c>MainThread</c>, it is read-only environment
/// state needed by controls that do not participate in dependency injection. Read fresh on every
/// query, so flipping the OS setting mid-session is honoured by the next animation.
/// </para>
/// </remarks>
public static partial class MotionPreferences
{
    /// <summary>True when animations should be skipped.</summary>
    public static bool ReduceMotion => GetReduceMotion();

    // Each shipped platform supplies its own answer in Platforms/. This fallback is only for a
    // head none of them cover - and until every head had a partial, it was silently the answer
    // on iOS, Mac Catalyst and Windows too, so the OS setting was honoured on Android alone.
#if !ANDROID && !IOS && !MACCATALYST && !WINDOWS
    private static bool GetReduceMotion() => false;
#endif
}
