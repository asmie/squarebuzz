namespace Squarebuzz.Presentation.Services;

/// <summary>
/// Reports whether the player is exploring the screen with a screen reader.
/// </summary>
/// <remarks>
/// <para>
/// Needed because the board is a canvas. Making it reachable means overlaying one focusable
/// element per cell, and a 20x20 grid is four hundred of them - worth building for the player who
/// cannot otherwise play at all, and pure waste for everyone else. So it is built only when
/// something is actually listening.
/// </para>
/// <para>
/// MAUI has no cross-platform equivalent, so this is a platform partial. Anywhere without an
/// implementation reports false, which errs towards the cheap path rather than towards silently
/// building hundreds of views on a platform whose screen-reader state we cannot read.
/// </para>
/// </remarks>
public interface IAccessibilityState
{
    /// <summary>True when a screen reader is driving touch exploration.</summary>
    /// <remarks>
    /// A cached answer, not a live system query. Reading it is on the path of every painted cell,
    /// and on Android the live query is four JNI transitions; the state itself changes at most a
    /// handful of times in a session. <see cref="Refresh"/> re-reads it.
    /// </remarks>
    bool IsScreenReaderActive { get; }

    /// <summary>Raised when that changes, so a screen already open can adapt.</summary>
    event EventHandler? ScreenReaderStateChanged;

    /// <summary>
    /// Re-reads the system state and raises <see cref="ScreenReaderStateChanged"/> if it moved.
    /// </summary>
    /// <remarks>
    /// A safety net for the case where the platform's change notification never arrives - the
    /// listener failed to register, or the platform has none. Call it where a screen opens, not
    /// on a per-frame or per-move path.
    /// </remarks>
    void Refresh();
}
