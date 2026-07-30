using Squarebuzz.App.Drawing;
using Squarebuzz.Core.Model;

namespace Squarebuzz.App.Controls;

/// <summary>Renders a finished picture, optionally masked or partially revealed.</summary>
public sealed class PuzzleArtView : GraphicsView
{
    private readonly PuzzleArtDrawable _drawable = new();

    public PuzzleArtView()
    {
        Drawable = _drawable;
    }

    public static readonly BindableProperty PuzzleProperty = BindableProperty.Create(
        nameof(Puzzle),
        typeof(Puzzle),
        typeof(PuzzleArtView),
        propertyChanged: (bindable, _, value) =>
        {
            var view = (PuzzleArtView)bindable;
            view._drawable.Puzzle = (Puzzle?)value;
            view.Invalidate();
        });

    public Puzzle? Puzzle
    {
        get => (Puzzle?)GetValue(PuzzleProperty);
        set => SetValue(PuzzleProperty, value);
    }

    public static readonly BindableProperty MarksProperty = BindableProperty.Create(
        nameof(Marks),
        typeof(IReadOnlyList<CellState>),
        typeof(PuzzleArtView),
        propertyChanged: (bindable, _, value) =>
        {
            var view = (PuzzleArtView)bindable;
            view._drawable.Marks = (IReadOnlyList<CellState>?)value;
            view.Invalidate();
        });

    /// <summary>Draw the player's marks rather than the finished picture. See PuzzleArtDrawable.</summary>
    public IReadOnlyList<CellState>? Marks
    {
        get => (IReadOnlyList<CellState>?)GetValue(MarksProperty);
        set => SetValue(MarksProperty, value);
    }

    public static readonly BindableProperty IsMaskedProperty = BindableProperty.Create(
        nameof(IsMasked),
        typeof(bool),
        typeof(PuzzleArtView),
        false,
        propertyChanged: (bindable, _, value) =>
        {
            var view = (PuzzleArtView)bindable;
            view._drawable.IsMasked = (bool)value;
            view.Invalidate();
        });

    public bool IsMasked
    {
        get => (bool)GetValue(IsMaskedProperty);
        set => SetValue(IsMaskedProperty, value);
    }

    /// <summary>Animates the picture in, cell by cell, over <paramref name="duration"/>.</summary>
    public async Task RevealAsync(TimeSpan duration)
    {
        const int frames = 24;
        var frameDelay = (int)(duration.TotalMilliseconds / frames);

        for (var frame = 1; frame <= frames; frame++)
        {
            _drawable.RevealProgress = frame / (double)frames;
            Invalidate();

            await Task.Delay(frameDelay);
        }
    }
}
