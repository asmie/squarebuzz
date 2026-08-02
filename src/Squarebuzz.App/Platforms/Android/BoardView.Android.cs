namespace Squarebuzz.App.Controls;

/// <summary>
/// Android's half of "this drag belongs to the board".
/// </summary>
/// <remarks>
/// <para>
/// The board sits inside a scrolling host so a 25x25 grid stays reachable on a phone. Android
/// scroll containers claim any gesture that travels further than the system touch slop - about
/// 21 device-independent units - and the child that was handling it gets a cancel. A board cell
/// is far larger than that, so the claim always landed inside the first or second square: a drag
/// meant to paint ten cells painted two and then died, silently, with no error anywhere.
/// </para>
/// <para>
/// <see cref="Android.Views.IViewParent.RequestDisallowInterceptTouchEvent"/> is the documented
/// answer. Each parent both honours it and passes it further up, so one call covers however many
/// scrolling ancestors the page happens to have - which matters here, because the board has more
/// than one and disabling the nearest changed nothing.
/// </para>
/// </remarks>
public sealed partial class BoardView
{
    /// <summary>
    /// Claims the gesture from every scrolling ancestor, or hands it back.
    /// </summary>
    /// <remarks>
    /// Only ever claimed once a touch has actually landed on a square. A touch that starts in a
    /// clue gutter is not painting anything, so it is left to the scrollers - that is how a
    /// player pans a board too big for the screen.
    /// </remarks>
    partial void ClaimGestureFromScrollers(bool claim)
    {
        if (Handler?.PlatformView is Android.Views.View view)
        {
            view.Parent?.RequestDisallowInterceptTouchEvent(claim);
        }
    }
}
