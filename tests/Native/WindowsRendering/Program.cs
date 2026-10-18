using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Text;
using Microsoft.Maui.Graphics.Platform;
using Squarebuzz.App.Drawing;
using Squarebuzz.Core.Layout;
using Squarebuzz.Core.Model;

namespace Squarebuzz.WindowsRendering;

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        var result = 1;
        WinRT.ComWrappersSupport.InitializeComWrappers();
        Microsoft.UI.Xaml.Application.Start(_ =>
        {
            var application = new Microsoft.UI.Xaml.Application();
            try
            {
                Run();
                result = 0;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine(exception);
            }
            finally
            {
                application.Exit();
            }
        });
        return result;
    }

    private static void Run()
    {
        if (AppInfo.Current.PackagingModel != AppPackagingModel.Unpackaged)
        {
            throw new InvalidOperationException("This regression must run without package identity.");
        }

        var registrar = new FontRegistrar(new EmbeddedFontLoader());
        registrar.Register("Quicksand-Bold.ttf", "BodyBold");
        var manager = new FontManager(registrar);
        var source = WindowsCanvasFont.Resolve(manager);
        Console.WriteLine($"Resolved clue font: {source}");
        var fontUri = new Uri(source);
        if (!fontUri.IsFile)
        {
            throw new InvalidOperationException("Win2D needs a file URI for the unpackaged font.");
        }

        // Text layout can silently substitute a system font, so also check the bundled face.
        using var fontSet = new CanvasFontSet(new Uri(fontUri.GetLeftPart(UriPartial.Path)));
        if (fontSet.Fonts.Count == 0 || !fontSet.Fonts.Any(face => face.FamilyNames.Values.Contains("Quicksand")))
        {
            throw new InvalidOperationException("The bundled Quicksand face was not loaded.");
        }
        Console.WriteLine("Bundled Quicksand face loaded.");

        using var device = CanvasDevice.GetSharedDevice();
        var cases = 0;
        foreach (var size in new[] { 5, 10, 15, 20, 25 })
        foreach (var bigNumbers in new[] { false, true })
        foreach (var width in new[] { 360, 1000 })
        {
            var solution = Enumerable.Range(0, size * size).Select(index => (index % size + index / size) % 3 == 0).ToArray();
            var puzzle = Puzzle.FromSolution("render-test", "animals", "#FF8A3D", size, size, solution);
            var layout = BoardLayout.Calculate(puzzle, width, 700, bigNumbers: bigNumbers);
            var drawable = new BoardDrawable
            {
                Puzzle = puzzle,
                Layout = layout,
                ClueFont = new Microsoft.Maui.Graphics.Font(source, FontWeights.Bold),
            };
            using var target = new CanvasRenderTarget(device, (float)layout.TotalWidth, (float)layout.TotalHeight, 96);
            foreach (var marked in new[] { false, true })
            {
                drawable.SetCells(solution.Select(filled => marked ? (filled ? CellState.Filled : CellState.Crossed) : CellState.Empty).ToArray());
                using (var session = target.CreateDrawingSession())
                {
                    var canvas = new PlatformCanvas { Session = session };
                    drawable.Draw(canvas, new RectF(0, 0, (float)layout.TotalWidth, (float)layout.TotalHeight));
                }
                if (!target.GetPixelBytes().Any(value => value != 0))
                {
                    throw new InvalidOperationException("Rendering produced a blank board.");
                }
                cases++;
                Console.WriteLine($"PASS: {size}x{size}, width={width}, bigNumbers={bigNumbers}, marked={marked}");
            }
        }
        Console.WriteLine($"PASS: {cases} native board renders (5x5 through 25x25, narrow/wide, normal/big clues, empty/completed).");
    }
}
