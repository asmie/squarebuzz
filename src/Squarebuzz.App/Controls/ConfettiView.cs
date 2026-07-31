using Squarebuzz.App.Services;

namespace Squarebuzz.App.Controls;

/// <summary>
/// The win screen's falling confetti: 44 little rectangles drifting down on a loop, from the
/// prototype's celebration.
/// </summary>
/// <remarks>
/// One canvas and a frame timer rather than 44 animated views. Every piece's path is a pure
/// function of its index and the elapsed time, so there is no per-frame allocation and no state
/// to reset - stopping and starting again simply restarts the clock. Runs only while
/// <see cref="IsRunning"/> is true and the view is loaded, and never under reduced motion.
/// </remarks>
public sealed class ConfettiView : GraphicsView
{
    private readonly ConfettiDrawable _drawable = new();

    private IDispatcherTimer? _timer;

    public ConfettiView()
    {
        Drawable = _drawable;
        InputTransparent = true;

        Loaded += (_, _) => SyncTimer();
        Unloaded += (_, _) => StopTimer();
    }

    public static readonly BindableProperty IsRunningProperty = BindableProperty.Create(
        nameof(IsRunning),
        typeof(bool),
        typeof(ConfettiView),
        false,
        propertyChanged: (bindable, _, _) => ((ConfettiView)bindable).SyncTimer());

    /// <summary>Bound to the celebration's visibility; the loop runs only while true.</summary>
    public bool IsRunning
    {
        get => (bool)GetValue(IsRunningProperty);
        set => SetValue(IsRunningProperty, value);
    }

    private void SyncTimer()
    {
        if (!IsRunning || !IsLoaded || MotionPreferences.ReduceMotion)
        {
            StopTimer();
            return;
        }

        if (_timer is not null)
        {
            return;
        }

        _drawable.Restart();
        _drawable.IsActive = true;

        _timer = Dispatcher.CreateTimer();
        _timer.Interval = TimeSpan.FromMilliseconds(33);
        _timer.IsRepeating = true;
        _timer.Tick += (_, _) => Invalidate();
        _timer.Start();
    }

    private void StopTimer()
    {
        _timer?.Stop();
        _timer = null;

        // Without this, a run that never starts - reduced motion - still drew one motionless
        // frame of mid-air confetti, which is worse than none.
        _drawable.IsActive = false;
        Invalidate();
    }

    private sealed class ConfettiDrawable : IDrawable
    {
        private const int PieceCount = 44;

        /// <summary>The prototype's celebration palette - bright against both themes.</summary>
        private static readonly Color[] Colours =
        [
            Color.FromArgb("#FF8A3D"),
            Color.FromArgb("#FFC244"),
            Color.FromArgb("#2FC0A4"),
            Color.FromArgb("#4FA8F5"),
            Color.FromArgb("#FF6B8A"),
            Color.FromArgb("#B8A9E8"),
        ];

        private long _startedAt = Environment.TickCount64;

        /// <summary>Drawn only while the loop runs; a still frame of confetti is not a celebration.</summary>
        public bool IsActive { get; set; }

        public void Restart() => _startedAt = Environment.TickCount64;

        public void Draw(ICanvas canvas, RectF dirtyRect)
        {
            ArgumentNullException.ThrowIfNull(canvas);

            if (!IsActive || dirtyRect.Width <= 0 || dirtyRect.Height <= 0)
            {
                return;
            }

            var seconds = (Environment.TickCount64 - _startedAt) / 1000f;

            for (var i = 0; i < PieceCount; i++)
            {
                // Everything about a piece derives from its index: golden-ratio hashing spreads
                // the columns evenly without a random generator to seed or reset.
                var lane = (i * 0.6180339887f) % 1f;
                var fallSeconds = 3.2f + ((i % 7) * 0.35f);
                var delay = (i * 0.173f) % fallSeconds;
                var progress = ((seconds + delay) % fallSeconds) / fallSeconds;

                var x = dirtyRect.X + (lane * dirtyRect.Width);
                var y = dirtyRect.Y + (progress * (dirtyRect.Height + 40f)) - 20f;

                // A little sideways sway, so the fall reads as fluttering rather than rain.
                x += 12f * (float)Math.Sin((progress * 6f) + i);

                var width = 6f + (i % 3 * 2f);
                var height = 10f + (i % 4 * 2f);
                var rotation = (progress * 360f * (1 + (i % 3))) + (i * 29f);

                canvas.SaveState();
                canvas.Translate(x, y);
                canvas.Rotate(rotation);
                canvas.FillColor = Colours[i % Colours.Length];
                canvas.FillRoundedRectangle(-width / 2f, -height / 2f, width, height, 1.5f);
                canvas.RestoreState();
            }
        }
    }
}
