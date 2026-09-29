namespace Squarebuzz.Presentation.Services;

/// <summary>Opens a web page outside the app, in the device's browser.</summary>
/// <remarks>
/// Every caller puts the parent gate in front of this: a link out of the app is a grown-up
/// action in a game for children.
/// </remarks>
public interface ILinkOpener
{
    /// <summary>Opens <paramref name="address"/>. False when the device has nothing that can.</summary>
    Task<bool> OpenAsync(Uri address);
}

/// <summary>The app's few fixed destinations outside itself.</summary>
public static class ExternalLinks
{
    /// <summary>
    /// The privacy policy, <c>PRIVACY.md</c> at the root of the project repository.
    /// </summary>
    /// <remarks>
    /// Served by GitHub once the repository is public, which is the plan for release. Keep the
    /// file at that path on the default branch: every installed copy of the app links here.
    /// </remarks>
    public static Uri PrivacyPolicy { get; } = new("https://github.com/asmie/squarebuzz/blob/master/PRIVACY.md");
}
