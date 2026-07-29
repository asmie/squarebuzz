namespace Squarebuzz.App.Drawing;

/// <summary>
/// The board's colours, resolved once from the active theme dictionary.
/// </summary>
/// <remarks>
/// A <see cref="IDrawable"/> has no access to the visual tree, so it cannot resolve
/// <c>DynamicResource</c> itself. Snapshotting the palette keeps the drawable a pure function of
/// its inputs, and means a theme change is one palette rebuild rather than a lookup per cell -
/// which matters when a 25x25 board draws 625 of them.
/// </remarks>
public sealed record BoardPalette
{
    public required Color CellEmpty { get; init; }

    public required Color CellLine { get; init; }

    public required Color CellFill { get; init; }

    public required Color Gutter { get; init; }

    public required Color GutterInk { get; init; }

    public required Color Sunk { get; init; }

    public required Color Ink2 { get; init; }

    public required Color Primary { get; init; }

    public required Color PrimarySoft { get; init; }

    public required Color Warn { get; init; }

    public required Color Gold { get; init; }

    /// <summary>
    /// Reads the current theme's colours. Falls back to the light palette's values for any key
    /// that cannot be resolved, so a missing resource degrades rather than throwing mid-draw.
    /// </summary>
    public static BoardPalette FromResources() => new()
    {
        CellEmpty = Resolve("CellEmpty", "#FFFDF7"),
        CellLine = Resolve("CellLine", "#E4D8C4"),
        CellFill = Resolve("CellFill", "#3B3550"),
        Gutter = Resolve("Gutter", "#FBEEDA"),
        GutterInk = Resolve("GutterInk", "#5B5268"),
        Sunk = Resolve("Sunk", "#F6E7D0"),
        Ink2 = Resolve("Ink2", "#5F5770"),
        Primary = Resolve("Primary", "#FF8A3D"),
        PrimarySoft = Resolve("PrimarySoft", "#FFE6D0"),
        Warn = Resolve("Warn", "#EF6C5A"),
        Gold = Resolve("Gold", "#FFC244"),
    };

    private static Color Resolve(string key, string fallbackHex)
    {
        if (Application.Current?.Resources.TryGetValue(key, out var value) == true && value is Color color)
        {
            return color;
        }

        return Color.FromArgb(fallbackHex);
    }
}
