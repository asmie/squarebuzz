namespace Squarebuzz.Presentation.Services;

/// <summary>
/// Marshals work onto the UI thread.
/// </summary>
/// <remarks>
/// The seam that keeps <c>MainThread</c> - a MAUI static - out of the ViewModels. Used by the
/// toast and notice auto-dismissals, whose delayed continuations land on a pool thread but must
/// touch bound properties. A test fake simply runs the action inline.
/// </remarks>
public interface IUiThread
{
    void BeginInvokeOnMainThread(Action action);
}
