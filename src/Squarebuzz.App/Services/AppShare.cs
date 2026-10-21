using Squarebuzz.Presentation.Services;

namespace Squarebuzz.App.Services;

/// <summary>Uses the Android Sharesheet or Windows share window for a public store link.</summary>
public sealed class AppShare : IAppShare
{
    public async Task<bool> RequestAsync(Uri address, string title)
    {
        ArgumentNullException.ThrowIfNull(address);

        try
        {
            await MainThread.InvokeOnMainThreadAsync(() => Share.Default.RequestAsync(new ShareTextRequest
            {
                Uri = address.AbsoluteUri,
                Title = title,
            }));
            return true;
        }
        catch (OperationCanceledException)
        {
            // Dismissing the chooser is normal; do not report it as a failure.
            return true;
        }
        catch (Exception)
        {
            // A missing share handler must not interrupt Settings.
            return false;
        }
    }
}
