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
    /// <remarks>
    /// Called for every cell the finger crosses. The marks are copied into a reused buffer and
    /// the palette is resolved only by <see cref="RefreshPalette"/>, rather than allocating a
    /// board and re-reading the theme resources on every sample.
    /// </remarks>
    public void ShowCell(int index)
    {
        if (Session is not { } session)
        {
            return;
        }

        _drawable.Puzzle = session.Puzzle;

        // Snapshotted, so the magnifier never reads the board while a move is being applied.
        var cells = session.Cells;
        if (_drawable.Cells.Length != cells.Length)
        {
            _drawable.Cells = new CellState[cells.Length];
        }

        cells.CopyTo(_drawable.Cells);
        _drawable.CenterIndex = index;

        Invalidate();
    }

    /// <summary>Re-reads the theme colours. Called by the host when the palette changes.</summary>
    public void RefreshPalette()
    {
        _drawable.Palette = BoardPalette.FromResources();
        Invalidate();
    }
}
