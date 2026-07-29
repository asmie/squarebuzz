namespace Squarebuzz.App.Drawing;

/// <summary>The mascot's expressions.</summary>
public enum MascotPose
{
    Idle,
    Cheer,
    Think,
}

/// <summary>
/// Draws the mascot as pixel art, from the same character grids the prototype used.
/// </summary>
/// <remarks>
/// Kept as a drawable rather than a bitmap so it recolours with the theme and accent for free -
/// the mascot is built from palette colours, not baked pixels, which is what lets Grape mode
/// produce a violet mascot without shipping a second asset.
/// </remarks>
public sealed class MascotDrawable : IDrawable
{
    // 'B' body, 'E' eye, 'M' mouth, 'A' raised arms, '^' happy eye, '-' closed eye.
    private static readonly string[] Idle =
    [
        "..BBB..",
        ".BBBBB.",
        "BBBBBBB",
        "BEBBBEB",
        "BBBBBBB",
        "BBMMMBB",
        ".BBBBB.",
        ".B...B.",
    ];

    private static readonly string[] Cheer =
    [
        "A.BBB.A",
        "ABBBBBA",
        "BBBBBBB",
        "B^BBB^B",
        "BBBBBBB",
        "BMMMMMB",
        ".BBBBB.",
        "B.....B",
    ];

    private static readonly string[] Think =
    [
        "..BBB..",
        ".BBBBB.",
        "BBBBBBB",
        "BEBB--B",
        "BBBBBBB",
        "BB.MM..",
        ".BBBBB.",
        ".B...B.",
    ];

    public MascotPose Pose { get; set; } = MascotPose.Idle;

    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        ArgumentNullException.ThrowIfNull(canvas);

        var pattern = Pose switch
        {
            MascotPose.Cheer => Cheer,
            MascotPose.Think => Think,
            _ => Idle,
        };

        var rows = pattern.Length;
        var columns = pattern[0].Length;

        // Square pixels, sized to whichever axis is tighter, then centred.
        var pixelGap = 0.12f;
        var pixel = Math.Min(
            dirtyRect.Width / (columns * (1 + pixelGap)),
            dirtyRect.Height / (rows * (1 + pixelGap)));

        if (pixel <= 0)
        {
            return;
        }

        var step = pixel * (1 + pixelGap);
        var offsetX = dirtyRect.X + ((dirtyRect.Width - (columns * step)) / 2);
        var offsetY = dirtyRect.Y + ((dirtyRect.Height - (rows * step)) / 2);
        var radius = Math.Max(1f, pixel * 0.18f);

        var body = Resolve("Primary", "#FF8A3D");
        var deep = Resolve("PrimaryDeep", "#D3641F");
        var ink = Resolve("Ink", "#332E42");
        var accent = Resolve("Accent", "#2FC0A4");

        for (var y = 0; y < rows; y++)
        {
            for (var x = 0; x < columns; x++)
            {
                var colour = pattern[y][x] switch
                {
                    'B' => body,
                    'M' => deep,
                    'A' => accent,
                    'E' or '^' or '-' => ink,
                    _ => (Color?)null,
                };

                if (colour is null)
                {
                    continue;
                }

                canvas.FillColor = colour;
                canvas.FillRoundedRectangle(offsetX + (x * step), offsetY + (y * step), pixel, pixel, radius);
            }
        }
    }

    private static Color Resolve(string key, string fallbackHex)
    {
        if (Application.Current?.Resources.TryGetValue(key, out var value) == true && value is Color color)
        {
            return color;
        }

        return Color.FromArgb(fallbackHex);
    }
}
