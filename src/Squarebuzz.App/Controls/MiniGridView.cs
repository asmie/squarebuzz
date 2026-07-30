using Squarebuzz.App.Drawing;

namespace Squarebuzz.App.Controls;

/// <summary>Renders a How to Play teaching diagram.</summary>
public sealed class MiniGridView : GraphicsView
{
    private readonly MiniGridDrawable _drawable = new();

    public MiniGridView()
    {
        Drawable = _drawable;
    }

    public static readonly BindableProperty RowsProperty = BindableProperty.Create(
        nameof(Rows),
        typeof(IReadOnlyList<string>),
        typeof(MiniGridView),
        propertyChanged: (bindable, _, value) =>
        {
            var view = (MiniGridView)bindable;
            view._drawable.Rows = (IReadOnlyList<string>?)value ?? [];
            view.Invalidate();
        });

    public IReadOnlyList<string>? Rows
    {
        get => (IReadOnlyList<string>?)GetValue(RowsProperty);
        set => SetValue(RowsProperty, value);
    }

    public static readonly BindableProperty RowCluesProperty = BindableProperty.Create(
        nameof(RowClues),
        typeof(IReadOnlyList<string>),
        typeof(MiniGridView),
        propertyChanged: (bindable, _, value) =>
        {
            var view = (MiniGridView)bindable;
            view._drawable.RowClues = (IReadOnlyList<string>?)value ?? [];
            view.Invalidate();
        });

    public IReadOnlyList<string>? RowClues
    {
        get => (IReadOnlyList<string>?)GetValue(RowCluesProperty);
        set => SetValue(RowCluesProperty, value);
    }
}
