using Squarebuzz.App.Drawing;
using Squarebuzz.App.Services;
using Squarebuzz.Core.Model;

namespace Squarebuzz.App.Controls;

/// <summary>Renders a finished picture, optionally masked or partially revealed.</summary>
public sealed class PuzzleArtView : GraphicsView
{
    private readonly PuzzleArtDrawable _drawable = new();
    private int _revealGeneration;

    public PuzzleArtView()
    {
        Drawable = _drawable;
        Unloaded += (_, _) => CompleteReveal();
    }

    public static readonly BindableProperty PuzzleProperty = BindableProperty.Create(
        nameof(Puzzle),
        typeof(Puzzle),
        typeof(PuzzleArtView),
        propertyChanged: (bindable, _, value) =>
        {
            var view = (PuzzleArtView)bindable;
            view._drawable.Puzzle = (Puzzle?)value;
            view.CompleteReveal();
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

    /// <summary>Reveals the picture over <paramref name="duration"/>, or immediately under reduced motion.</summary>
    public async Task RevealAsync(TimeSpan duration)
    {
        if (MotionPreferences.ReduceMotion || duration <= TimeSpan.Zero)
        {
            CompleteReveal();
            return;
        }

        var generation = ++_revealGeneration;
        const int frames = 24;
        var frameDelay = duration / frames;

        MotionPreferences.Changed += OnMotionPreferenceChanged;
        try
        {
            for (var frame = 1; frame <= frames; frame++)
            {
                if (generation != _revealGeneration)
                {
                    return;
                }

                if (MotionPreferences.ReduceMotion)
                {
                    CompleteReveal();
                    return;
                }

                _drawable.RevealProgress = frame / (double)frames;
                Invalidate();
                await Task.Delay(frameDelay);
            }
        }
        finally
        {
            MotionPreferences.Changed -= OnMotionPreferenceChanged;
        }
    }

    private void OnMotionPreferenceChanged(object? sender, EventArgs e)
    {
        if (MotionPreferences.ReduceMotion)
        {
            CompleteReveal();
        }
    }

    private void CompleteReveal()
    {
        _revealGeneration++;
        _drawable.RevealProgress = 1;
        Invalidate();
    }
}
