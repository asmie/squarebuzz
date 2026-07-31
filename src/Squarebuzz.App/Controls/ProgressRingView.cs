namespace Squarebuzz.App.Controls;

/// <summary>
/// The status bar's progress ring: how much of the picture is filled in, as an arc.
/// </summary>
/// <remarks>
/// Deliberately wordless - the ring is a glanceable "how far along am I" for a player who may
/// not read yet. Screen readers get the same fact in words through the board's description, so
/// the ring itself stays out of the accessibility tree.
/// </remarks>
public sealed class ProgressRingView : GraphicsView
{
    private readonly RingDrawable _drawable = new();

    public ProgressRingView()
    {
        Drawable = _drawable;
        InputTransparent = true;
    }

    public static readonly BindableProperty ProgressProperty = BindableProperty.Create(
        nameof(Progress),
        typeof(double),
        typeof(ProgressRingView),
        0.0,
        propertyChanged: (bindable, _, value) =>
        {
            var view = (ProgressRingView)bindable;
            view._drawable.Progress = Math.Clamp((float)(double)value, 0f, 1f);
            view.Invalidate();
        });

    /// <summary>Fraction of the picture filled, 0..1.</summary>
    public double Progress
    {
        get => (double)GetValue(ProgressProperty);
        set => SetValue(ProgressProperty, value);
    }

    private sealed class RingDrawable : IDrawable
    {
        private const float Thickness = 5f;

        public float Progress { get; set; }

        public void Draw(ICanvas canvas, RectF dirtyRect)
        {
            ArgumentNullException.ThrowIfNull(canvas);

            var side = Math.Min(dirtyRect.Width, dirtyRect.Height);

            if (side <= Thickness * 2)
            {
                return;
            }

            var inset = Thickness / 2f;
            var x = dirtyRect.X + ((dirtyRect.Width - side) / 2f) + inset;
            var y = dirtyRect.Y + ((dirtyRect.Height - side) / 2f) + inset;
            var diameter = side - Thickness;

            canvas.StrokeSize = Thickness;
            canvas.StrokeLineCap = LineCap.Round;

            // The track, then the arc over it, clockwise from the top.
            canvas.StrokeColor = Resolve("Line", "#F0E2CE");
            canvas.DrawEllipse(x, y, diameter, diameter);

            if (Progress > 0f)
            {
                canvas.StrokeColor = Resolve("Primary", "#FF8A3D");
                canvas.DrawArc(x, y, diameter, diameter, 90, 90 - (Progress * 360f), clockwise: true, closed: false);
            }
        }

        private static Color Resolve(string key, string fallbackHex)
        {
            if (Application.Current?.Resources.TryGetValue(key, out var value) == true && value is Color color)
            {
                return color;
            }

            return Color.FromArgb(fallbackHex);
        }
    }
}
