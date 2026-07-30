using Squarebuzz.App.Drawing;
using Squarebuzz.Core.Model;

namespace Squarebuzz.App.Controls;

/// <summary>Shows the 3x3 neighbourhood of the cell under the finger.</summary>
public sealed class MagnifierView : GraphicsView
{
    private readonly MagnifierDrawable _drawable = new();

    public MagnifierView()
    {
        Drawable = _drawable;
    }

    public static readonly BindableProperty SessionProperty = BindableProperty.Create(
        nameof(Session),
        typeof(GameSession),
        typeof(MagnifierView));

    public GameSession? Session
    {
        get => (GameSession?)GetValue(SessionProperty);
        set => SetValue(SessionProperty, value);
    }

    /// <summary>Row-major index to centre on, or -1 to show nothing.</summary>
    public void ShowCell(int index)
    {
        if (Session is not { } session)
        {
            return;
        }

        _drawable.Puzzle = session.Puzzle;

        // Snapshotted, so the magnifier never reads the board while a move is being applied.
        _drawable.Cells = session.Cells.ToArray();
        _drawable.CenterIndex = index;
        _drawable.Palette = BoardPalette.FromResources();

        Invalidate();
    }
}
