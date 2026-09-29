using Squarebuzz.Presentation.Services;

namespace Squarebuzz.App.Services;

/// <inheritdoc />
public sealed class LinkOpener : ILinkOpener
{
    /// <remarks>
    /// SystemPreferred: an in-app browser sheet where the platform offers one (Custom Tabs on
    /// Android, Safari View Controller on Apple), the default browser elsewhere. The app itself
    /// needs no internet access either way - the browser does the fetching.
    /// </remarks>
    public async Task<bool> OpenAsync(Uri address)
    {
        ArgumentNullException.ThrowIfNull(address);

        try
        {
            return await Browser.Default.OpenAsync(address, BrowserLaunchMode.SystemPreferred);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // No browser at all, or the platform refused. The caller shows the address instead.
            return false;
        }
    }
}
