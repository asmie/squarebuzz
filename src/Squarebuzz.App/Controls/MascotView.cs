using Squarebuzz.App.Drawing;

namespace Squarebuzz.App.Controls;

/// <summary>Renders the mascot at whatever size it is given.</summary>
public sealed class MascotView : GraphicsView
{
    private readonly MascotDrawable _drawable = new();

    public MascotView()
    {
        Drawable = _drawable;
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
