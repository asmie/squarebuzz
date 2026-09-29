using Squarebuzz.Presentation.Services;

namespace Squarebuzz.App.Services;

/// <inheritdoc />
public sealed class AppVersion : IAppVersion
{
    public string Version => AppInfo.Current.VersionString;

    public string Build => AppInfo.Current.BuildString;
}
