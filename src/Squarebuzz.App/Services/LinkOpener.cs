using Squarebuzz.Presentation.Services;

namespace Squarebuzz.App.Services;

/// <inheritdoc />
public sealed class LinkOpener : ILinkOpener
{
    public Uri? StorePage =>
#if ANDROID
        new($"https://play.google.com/store/apps/details?id={Uri.EscapeDataString(AppInfo.Current.PackageName)}");
#elif WINDOWS
        ExternalLinks.MicrosoftStoreListing;
#else
        null;
#endif

    public async Task<bool> OpenStoreAsync()
    {
        var page = StorePage;
        if (page is null)
        {
            return false;
        }

        try
        {
#if ANDROID
            // Target Google Play explicitly; other stores can also handle generic store links.
            using var intent = new Android.Content.Intent(Android.Content.Intent.ActionView,
                Android.Net.Uri.Parse(page.AbsoluteUri));
            intent.SetPackage("com.android.vending");
            intent.AddFlags(Android.Content.ActivityFlags.NewTask);
            Android.App.Application.Context.StartActivity(intent);
            return true;
#elif WINDOWS
            if (await Launcher.Default.OpenAsync(ExternalLinks.MicrosoftStoreReview))
            {
                return true;
            }
#endif
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // A missing or unavailable store must not prevent opening the web listing.
        }

        return await OpenAsync(page);
    }

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
