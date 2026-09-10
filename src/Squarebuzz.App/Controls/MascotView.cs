using System.ComponentModel;
using Squarebuzz.App.Drawing;
using Squarebuzz.App.Services;
using Squarebuzz.Presentation.ViewModels;

namespace Squarebuzz.App.Controls;

/// <summary>
/// Renders the mascot at whatever size it is given, gently bobbing and occasionally blinking.
/// </summary>
/// <remarks>
/// <para>
/// The idle motion is what makes the mascot a character rather than an icon - the design gives
/// it a slow bob and a blink every few seconds. Neither runs when the OS asks for reduced
/// motion.
/// </para>
/// <para>
/// Motion follows whether the mascot can actually be <em>seen</em>, not merely whether it is
/// loaded. Being loaded is not the same thing: the game screen carries four mascots, one in each
/// of its pause, break, time-up and win overlays, and an overlay hidden with <c>IsVisible</c>
/// keeps its children in the tree and loaded. All four therefore bobbed and blinked for the
/// whole of every game - four repeating animations and four timers redrawing canvases nobody
/// could see, which also left the window permanently non-idle and so never able to settle.
/// </para>
/// </remarks>
public sealed class MascotView : GraphicsView
{
    private static readonly TimeSpan BlinkEvery = TimeSpan.FromSeconds(4.2);
    private static readonly TimeSpan BlinkFor = TimeSpan.FromMilliseconds(140);

    private const string BobAnimation = "mascotBob";

    private readonly MascotDrawable _drawable = new();

    /// <summary>
    /// Every ancestor being watched for a visibility change, so the chain can be released again.
    /// </summary>
    private readonly List<VisualElement> _watchedAncestors = [];

    private IDispatcherTimer? _blinkTimer;
    private bool _isMoving;

    public MascotView()
    {
        Drawable = _drawable;

        Loaded += (_, _) =>
        {
            MotionPreferences.Changed += OnMotionPreferenceChanged;
            WatchAncestors();
            SyncIdleMotion();
        };

        Unloaded += (_, _) =>
        {
            MotionPreferences.Changed -= OnMotionPreferenceChanged;
            ReleaseAncestors();
            SyncIdleMotion();
        };
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

    /// <summary>
    /// True only when this mascot and every element above it is visible.
    /// </summary>
    /// <remarks>
    /// MAUI has no "effective visibility": hiding a container leaves each child's own
    /// <see cref="VisualElement.IsVisible"/> untouched, so the answer has to come from the chain.
    /// </remarks>
    private bool IsActuallyOnScreen()
    {
        for (Element? element = this; element is not null; element = element.Parent)
        {
            if (element is VisualElement { IsVisible: false })
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Listens for a visibility change anywhere above this mascot, because that is what decides
    /// whether the idle motion should be running.
    /// </summary>
    private void WatchAncestors()
    {
        ReleaseAncestors();

        for (Element? element = this; element is not null; element = element.Parent)
        {
            if (element is VisualElement visual)
            {
                visual.PropertyChanged += OnAncestorPropertyChanged;
                _watchedAncestors.Add(visual);
            }
        }
    }

    private void ReleaseAncestors()
    {
        foreach (var ancestor in _watchedAncestors)
        {
            ancestor.PropertyChanged -= OnAncestorPropertyChanged;
        }

        _watchedAncestors.Clear();
    }

    private void OnAncestorPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(IsVisible))
        {
            SyncIdleMotion();
        }
    }

    private void OnMotionPreferenceChanged(object? sender, EventArgs e) => SyncIdleMotion();

    /// <summary>Starts or stops the bob and the blink to match the mascot's current state.</summary>
    private void SyncIdleMotion()
    {
        var shouldMove = IsLoaded && IsActuallyOnScreen() && !MotionPreferences.ReduceMotion;

        if (shouldMove == _isMoving)
        {
            return;
        }

        _isMoving = shouldMove;

        if (shouldMove)
        {
            StartIdleMotion();
        }
        else
        {
            StopIdleMotion();
        }
    }

    private void StartIdleMotion()
    {
        // The bob: a slow sine on TranslationY, a couple of pixels either way over 2.6 s.
        this.AbortAnimation(BobAnimation);
        new Animation(v => TranslationY = 3 * Math.Sin(v * 2 * Math.PI), 0, 1)
            .Commit(this, BobAnimation, length: 2600, repeat: () => true);

        _blinkTimer = Dispatcher.CreateTimer();
        _blinkTimer.Interval = BlinkEvery;
        _blinkTimer.IsRepeating = true;
        _blinkTimer.Tick += OnBlinkTick;
        _blinkTimer.Start();
    }

    private void StopIdleMotion()
    {
        this.AbortAnimation(BobAnimation);
        TranslationY = 0;

        if (_blinkTimer is not null)
        {
            _blinkTimer.Tick -= OnBlinkTick;
            _blinkTimer.Stop();
            _blinkTimer = null;
        }

        // A blink caught mid-wink would otherwise be the pose the mascot is wearing when the
        // overlay it lives in is next shown.
        _drawable.EyesClosed = false;
        Invalidate();
    }

    private async void OnBlinkTick(object? sender, EventArgs e)
    {
        if (!_isMoving || MotionPreferences.ReduceMotion)
        {
            SyncIdleMotion();
            return;
        }

        _drawable.EyesClosed = true;
        Invalidate();

        await Task.Delay(BlinkFor);

        // The blink outlasts its own timer by 140 ms, so it can land after the mascot has been
        // hidden - reopening the overlay on a mascot with its eyes shut.
        _drawable.EyesClosed = false;
        Invalidate();
    }
}
