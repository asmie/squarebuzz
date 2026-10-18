namespace Squarebuzz.App.Drawing;

/// <summary>Resolves the board's font for Win2D rather than the XAML text renderer.</summary>
internal static class WindowsCanvasFont
{
    public static string Resolve(IFontManager fontManager)
    {
        var source = fontManager.GetFontFamily(Microsoft.Maui.Font.OfSize("BodyBold", 12)).Source;
        if (AppInfo.Current.PackagingModel != AppPackagingModel.Unpackaged ||
            !Uri.TryCreate(source, UriKind.Absolute, out var uri) ||
            !string.Equals(uri.Scheme, "ms-appx", StringComparison.OrdinalIgnoreCase))
        {
            return source;
        }

        // XAML resolves ms-appx in an unpackaged app, but Win2D's text layout requires
        // package identity for that scheme and throws on the first clue. Win2D supports
        // file URIs; resolve beside the executable, independent of the working directory.
        var fontPath = Path.Combine(AppContext.BaseDirectory, Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/')));
        return new Uri(fontPath).AbsoluteUri + uri.Fragment;
    }
}
