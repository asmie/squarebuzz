using Squarebuzz.App.Drawing;
using Squarebuzz.App.Services;

namespace Squarebuzz.App.Controls;

/// <summary>
/// Renders the mascot at whatever size it is given, gently bobbing and occasionally blinking.
/// </summary>
/// <remarks>
/// The idle motion is what makes the mascot a character rather than an icon - the design gives
/// it a slow bob and a blink every few seconds. Both loops run only while the view is loaded,
/// and not at all when the OS asks for reduced motion.
/// </remarks>
public sealed class MascotView : GraphicsView
{
    private static readonly TimeSpan BlinkEvery = TimeSpan.FromSeconds(4.2);
    private static readonly TimeSpan BlinkFor = TimeSpan.FromMilliseconds(140);

    private readonly MascotDrawable _drawable = new();

    private IDispatcherTimer? _blinkTimer;

    public MascotView()
    {
        Drawable = _drawable;

        Loaded += (_, _) => StartIdleMotion();
        Unloaded += (_, _) => StopIdleMotion();
    }

    private void StartIdleMotion()
    {
        if (MotionPreferences.ReduceMotion)
        {
            return;
        }

        // The bob: a slow sine on TranslationY, a couple of pixels either way over 2.6 s.
        this.AbortAnimation("mascotBob");
        new Animation(v => TranslationY = 3 * Math.Sin(v * 2 * Math.PI), 0, 1)
            .Commit(this, "mascotBob", length: 2600, repeat: () => true);

        _blinkTimer = Dispatcher.CreateTimer();
        _blinkTimer.Interval = BlinkEvery;
        _blinkTimer.IsRepeating = true;
        _blinkTimer.Tick += async (_, _) =>
        {
            _drawable.EyesClosed = true;
            Invalidate();

            await Task.Delay(BlinkFor);

            _drawable.EyesClosed = false;
            Invalidate();
        };
        _blinkTimer.Start();
    }

    private void StopIdleMotion()
    {
        this.AbortAnimation("mascotBob");
        TranslationY = 0;

        _blinkTimer?.Stop();
        _blinkTimer = null;
    }

    public static readonly BindableProperty PoseProperty = BindableProperty.Create(
        nameof(Pose),
        typeof(MascotPose),
        typeof(MascotView),
        MascotPose.Idle,
        propertyChanged: (bindable, _, value) =>
        {
            var view = (MascotView)bindable;
            view._drawable.Pose = (MascotPose)value;
            view.Invalidate();
        });

    public MascotPose Pose
    {
        get => (MascotPose)GetValue(PoseProperty);
        set => SetValue(PoseProperty, value);
    }
}
