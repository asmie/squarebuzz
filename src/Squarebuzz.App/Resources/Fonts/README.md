# Fonts

The prototype (`design/Squarebuzz.dc.html`) loads two families from Google Fonts:

| Family | Used for | Weights referenced |
|---|---|---|
| **Fredoka** | display / headings, the `squarebuzz` wordmark | 400, 500, 600, 700 |
| **Quicksand** | body text, buttons, clue numbers | 500, 600, 700 |

A children's game must work offline, so these are bundled rather than fetched at runtime.

## Adding them

1. Download both families (both are under the SIL Open Font License 1.1):
   - <https://fonts.google.com/specimen/Fredoka>
   - <https://fonts.google.com/specimen/Quicksand>
2. Copy the `.ttf` files into this folder, e.g. `Fredoka-SemiBold.ttf`, `Quicksand-SemiBold.ttf`.
   The `.csproj` picks up everything here via `<MauiFont Include="Resources\Fonts\*" />`.
3. Register them in `MauiProgram.ConfigureFonts`:

   ```csharp
   fonts.AddFont("Fredoka-SemiBold.ttf", "FredokaSemiBold");
   fonts.AddFont("Quicksand-SemiBold.ttf", "QuicksandSemiBold");
   fonts.AddFont("Quicksand-Bold.ttf", "QuicksandBold");
   ```

4. Point the styles at them in `Resources/Styles/Styles.xaml` by adding
   `<Setter Property="FontFamily" Value="QuicksandSemiBold" />` to the base `Label` style and
   `FredokaSemiBold` to `H1`/`H2`.

Until step 3 is done the app runs on the platform default font. Nothing breaks — only the
typography differs from the design doc.
