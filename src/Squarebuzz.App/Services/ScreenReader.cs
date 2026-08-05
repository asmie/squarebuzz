using Squarebuzz.Presentation.Services;

namespace Squarebuzz.App.Services;

/// <summary>The platform screen reader, via MAUI's <see cref="SemanticScreenReader"/>.</summary>
public sealed class MauiScreenReader : IScreenReader
{
    public void Announce(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        try
        {
            SemanticScreenReader.Default.Announce(text);
        }
        catch (Exception)
        {
            // Not every platform implements it, and an announcement is never worth a crash.
        }
    }
}
