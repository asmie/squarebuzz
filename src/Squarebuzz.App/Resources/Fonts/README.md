# Fonts

The bundled Fredoka and Quicksand files use the SIL Open Font License 1.1.
Keep `OFL-Fredoka.txt` and `OFL-Quicksand.txt` beside them.

| File | Variable-font settings | MAUI alias | Use |
| --- | --- | --- | --- |
| Fredoka-SemiBold.ttf | wght 600, wdth 100 | Display | Headings and wordmark |
| Quicksand-Medium.ttf | wght 500 | Body | Body text |
| Quicksand-Bold.ttf | wght 700 | BodyBold | Buttons and clue numbers |

These are static instances of the upstream variable fonts. Register weights explicitly in
`MauiProgram.ConfigureFonts` and select aliases in XAML. Use the bundled bold face instead of
synthetic `FontAttributes="Bold"`.

## Regenerate

Obtain the source fonts from the Google Fonts repository:

```bash
pip install fonttools
curl -L -o Fredoka-var.ttf "https://github.com/google/fonts/raw/main/ofl/fredoka/Fredoka%5Bwdth%2Cwght%5D.ttf"
curl -L -o Quicksand-var.ttf "https://github.com/google/fonts/raw/main/ofl/quicksand/Quicksand%5Bwght%5D.ttf"
fonttools varLib.instancer Fredoka-var.ttf wght=600 wdth=100 -o Fredoka-SemiBold.ttf --update-name-table
fonttools varLib.instancer Quicksand-var.ttf wght=500 -o Quicksand-Medium.ttf --update-name-table
fonttools varLib.instancer Quicksand-var.ttf wght=700 -o Quicksand-Bold.ttf --update-name-table
```

Record the upstream revision when replacing font files. Recheck glyph coverage and update
`AppLanguages.All.DisplayFontCovers` where needed. Languages without full display-face coverage
use the body face for headings; platform fallback may still be needed for body text.

The font build item includes only `*.ttf`; licence and Markdown files are not font inputs.
The board canvas does not inherit XAML styles. `BoardView` resolves the platform font and passes
it to `BoardDrawable`; a MAUI alias alone is not a valid canvas font on every platform.
