namespace Squarebuzz.Presentation.Services;

/// <summary>Opens external web pages and the current platform's app store.</summary>
/// <remarks>
/// Every caller puts the parent gate in front of this: a link out of the app is a grown-up
/// action in a game for children.
/// </remarks>
public interface ILinkOpener
{
    /// <summary>Opens <paramref name="address"/>. False when the device has nothing that can.</summary>
    Task<bool> OpenAsync(Uri address);

    /// <summary>The current platform's store listing, or null when it is not configured.</summary>
    Uri? StorePage { get; }

    /// <summary>Opens the store to rate the app, falling back to its web listing.</summary>
    Task<bool> OpenStoreAsync();
}

/// <summary>The app's few fixed destinations outside itself.</summary>
public static class ExternalLinks
{
    public const string MicrosoftStoreId = "9P308NW6NSXM";

    public static Uri MicrosoftStoreListing { get; } = new($"https://apps.microsoft.com/detail/{MicrosoftStoreId}");

    public static Uri MicrosoftStoreReview { get; } = new($"ms-windows-store://review/?ProductId={MicrosoftStoreId}");

    /// <summary>
    /// The privacy policy, <c>PRIVACY.md</c> at the root of the project repository.
    /// </summary>
    /// <remarks>
    /// Served by GitHub once the repository is public, which is the plan for release. Keep the
    /// file at that path on the default branch: every installed copy of the app links here.
    /// </remarks>
    public static Uri PrivacyPolicy { get; } = new("https://github.com/asmie/squarebuzz/blob/master/PRIVACY.md");
}
