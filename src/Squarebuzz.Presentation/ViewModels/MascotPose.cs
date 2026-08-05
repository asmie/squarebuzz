namespace Squarebuzz.Presentation.ViewModels;

/// <summary>The mascot's expressions.</summary>
/// <remarks>
/// Lives with the ViewModels rather than with the drawing code: the pose is state screens
/// decide ("cheer on a live streak"), and the mascot control merely renders it.
/// </remarks>
public enum MascotPose
{
    Idle,
    Cheer,
    Think,
}
