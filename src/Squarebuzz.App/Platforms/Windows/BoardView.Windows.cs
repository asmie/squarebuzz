// Aliased rather than imported: MAUI's implicit global usings already bring in
// Microsoft.Maui.Controls.ScrollMode, and the WinUI namespace carries a ScrollMode of its own.
using DependencyObject = Microsoft.UI.Xaml.DependencyObject;
using ScrollMode = Microsoft.UI.Xaml.Controls.ScrollMode;
using ScrollViewer = Microsoft.UI.Xaml.Controls.ScrollViewer;
using VisualTreeHelper = Microsoft.UI.Xaml.Media.VisualTreeHelper;

namespace Squarebuzz.App.Controls;

/// <summary>
/// Windows' half of "this drag belongs to the board".
/// </summary>
/// <remarks>
/// <para>
/// The board sits inside a scrolling host so a 25x25 grid stays reachable on a small window. A
/// WinUI <see cref="ScrollViewer"/> hands touch and pen input to DirectManipulation, which pans
/// the content as soon as a contact travels past the system threshold and cancels the pointer
/// sequence the child was following. A board cell is far larger than that threshold, so the pan
/// always won inside the first or second square - the same silent two-cells-then-nothing the
/// Android and Apple partials describe.
/// </para>
/// <para>
/// The lever here is the scroll mode: while the board owns the gesture, every scrolling ancestor
/// has both axes set to <see cref="ScrollMode.Disabled"/>, and the modes it had before are put
/// back when the gesture ends. Saved rather than assumed, because the page decides which axes a
/// host scrolls on and this file should not have to know.
/// </para>
/// </remarks>
public sealed partial class BoardView
{
    /// <summary>The scroll modes each claimed ancestor had, so release restores exactly those.</summary>
    private readonly Dictionary<ScrollViewer, (ScrollMode Horizontal, ScrollMode Vertical)> _claimedScrollers = [];

    /// <summary>
    /// Claims the gesture from every scrolling ancestor, or hands it back.
    /// </summary>
    /// <remarks>
    /// Only ever claimed once a touch has actually landed on a square. A touch that starts in a
    /// clue gutter is not painting anything, so it is left to the scrollers - that is how a
    /// player pans a board too big for the window.
    /// </remarks>
    partial void ClaimGestureFromScrollers(bool claim)
    {
        if (claim)
        {
            Claim();
        }
        else
        {
            Release();
        }
    }

    private void Claim()
    {
        if (Handler?.PlatformView is not DependencyObject view)
        {
            return;
        }

        for (var ancestor = VisualTreeHelper.GetParent(view); ancestor is not null; ancestor = VisualTreeHelper.GetParent(ancestor))
        {
            if (ancestor is not ScrollViewer scroller || _claimedScrollers.ContainsKey(scroller))
            {
                continue;
            }

            _claimedScrollers[scroller] = (scroller.HorizontalScrollMode, scroller.VerticalScrollMode);

            // Stop a pan that DirectManipulation may already have begun on this very contact,
            // then keep it from starting another for the rest of the gesture.
            scroller.CancelDirectManipulations();
            scroller.HorizontalScrollMode = ScrollMode.Disabled;
            scroller.VerticalScrollMode = ScrollMode.Disabled;
        }
    }

    private void Release()
    {
        foreach (var (scroller, modes) in _claimedScrollers)
        {
            scroller.HorizontalScrollMode = modes.Horizontal;
            scroller.VerticalScrollMode = modes.Vertical;
        }

        _claimedScrollers.Clear();
    }
}
