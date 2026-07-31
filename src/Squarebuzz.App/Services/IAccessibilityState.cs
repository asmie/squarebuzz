namespace Squarebuzz.App.Services;

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
    bool IsScreenReaderActive { get; }

    /// <summary>Raised when that changes, so a screen already open can adapt.</summary>
    event EventHandler? ScreenReaderStateChanged;
}
