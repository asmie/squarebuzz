#if IOS || MACCATALYST
using UIKit;

namespace Squarebuzz.App.Controls;

/// <summary>
/// Apple's half of "this drag belongs to the board".
/// </summary>
/// <remarks>
/// <para>
/// The board sits inside a scrolling host so a 25x25 grid stays reachable on a phone. A
/// <see cref="UIScrollView"/> watches every touch inside it and, once a finger has travelled
/// far enough to read as a pan, takes the gesture for itself and cancels the child's. A board
/// cell is far larger than that threshold, so the theft always landed inside the first or second
/// square: a drag meant to paint ten cells painted two and then died, silently. The Android
/// partial documents the identical failure; until this file existed the fix was Android-only,
/// on a game whose README lists iPhone and iPad first.
/// </para>
/// <para>
/// <see cref="UIScrollView.ScrollEnabled"/> is the lever. Switching it off does not disturb the
/// touch already in progress on the child - it only stops the scroll view competing for it - and
/// it is applied to every scrolling ancestor, because the board has more than one and disabling
/// the nearest alone changed nothing on Android either.
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
        if (Handler?.PlatformView is not UIView view)
        {
            return;
        }

        for (var ancestor = view.Superview; ancestor is not null; ancestor = ancestor.Superview)
        {
            if (ancestor is UIScrollView scroller)
            {
                scroller.ScrollEnabled = !claim;
            }
        }
    }
}

#endif
