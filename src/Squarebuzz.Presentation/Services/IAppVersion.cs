namespace Squarebuzz.Presentation.Services;

/// <summary>
/// The installed app's version, as the store and the OS know it.
/// </summary>
/// <remarks>
/// Read from the package rather than written into the strings. The About screen used to carry a
/// hand-typed "1.0 · build 128" in every language, which matched no build that ever shipped and
/// would have stayed wrong with every release. The numbers come from the project's
/// <c>ApplicationDisplayVersion</c> and <c>ApplicationVersion</c>, the same values each store
/// reads when a build is uploaded.
/// </remarks>
public interface IAppVersion
{
    /// <summary>The user-facing version, e.g. "1.0".</summary>
    string Version { get; }

    /// <summary>The build number the stores order uploads by, e.g. "1".</summary>
    string Build { get; }
}
