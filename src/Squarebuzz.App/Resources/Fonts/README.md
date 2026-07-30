# Fonts

The design uses two families from Google Fonts, both under the SIL Open Font License 1.1
(`OFL-Fredoka.txt` and `OFL-Quicksand.txt` here):

| Family | Used for |
|---|---|
| **Fredoka** | display: headings and the `squarebuzz` wordmark |
| **Quicksand** | body text, buttons, and the clue numbers on the board |

They are bundled rather than fetched at runtime, because a children's game has to look like itself
offline.

## These are static instances cut from variable fonts

Upstream ships **only** variable fonts, and both default to `wght=300`:

| Upstream file | Axes | Default |
|---|---|---|
| `Fredoka[wdth,wght].ttf` | wght 300–700, wdth 75–125 | 300 / 100 |
| `Quicksand[wght].ttf` | wght 300–700 | 300 |

Shipping those as they come would render the whole app in **Light**, which is wrong for a game
whose entire character is chunky and rounded. MAUI cannot pick a weight along a variable axis
either, so each weight the design calls for has to exist as its own file.

| Bundled file | Cut at | Registered alias |
|---|---|---|
| `Fredoka-SemiBold.ttf` | wght 600, wdth 100 | `Display` |
| `Quicksand-Medium.ttf` | wght 500 | `Body` |
| `Quicksand-Bold.ttf` | wght 700 | `BodyBold` |

`BodyBold` exists because a rounded face smears when the platform fakes weight, so nothing in the
app uses `FontAttributes="Bold"` any more — bold text names the bold cut instead.

## Regenerating them

Sources are the canonical `google/fonts` repository, so the provenance is a URL rather than a
download button:

```bash
pip install fonttools
curl -L -o Fredoka-var.ttf   "https://github.com/google/fonts/raw/main/ofl/fredoka/Fredoka%5Bwdth%2Cwght%5D.ttf"
curl -L -o Quicksand-var.ttf "https://github.com/google/fonts/raw/main/ofl/quicksand/Quicksand%5Bwght%5D.ttf"

fonttools varLib.instancer Fredoka-var.ttf   wght=600 wdth=100 -o Fredoka-SemiBold.ttf --update-name-table
fonttools varLib.instancer Quicksand-var.ttf wght=500          -o Quicksand-Medium.ttf --update-name-table
fonttools varLib.instancer Quicksand-var.ttf wght=700          -o Quicksand-Bold.ttf   --update-name-table
```

Keep the `OFL-*.txt` files beside them: the licence requires the copyright notice and licence text
to travel with the fonts.

The `.csproj` glob is `Resources\Fonts\*.ttf` deliberately — a bare `*` would hand these licence
files and this README to the font compiler.

## Changing a weight

Cut a new instance, register it in `MauiProgram.ConfigureFonts` under an alias, and refer to the
alias from `Resources/Styles/Styles.xaml`. Nothing outside those two files should name a font file.

The board's clue numbers are the one exception to "styles decide the font": they are drawn onto a
canvas, which does not inherit XAML styles, so `BoardDrawable` names the alias itself.
