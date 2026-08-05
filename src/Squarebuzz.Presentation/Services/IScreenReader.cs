namespace Squarebuzz.Presentation.Services;

/// <summary>
/// Sends an announcement to whatever screen reader the player is running.
/// </summary>
/// <remarks>
/// The seam that keeps <c>SemanticScreenReader</c> - a MAUI static - out of the ViewModels.
/// Implementations must be safe to call unconditionally: a no-op when no screen reader is
/// running, and never a crash - an announcement is never worth one.
/// </remarks>
public interface IScreenReader
{
    void Announce(string text);
}
