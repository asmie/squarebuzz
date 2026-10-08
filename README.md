# squarebuzz

A nonogram game for children aged 6 and up, with no upper age limit. Adults can enjoy it just as much.
Built with .NET MAUI for Android, iOS, Mac Catalyst and Windows.
Players fill a grid using row and column clues to reveal a picture.

The game has a 600-level campaign, Quick Game, daily puzzles and six timed trials. It includes
130 authored pictures and generates larger boards. Current authored content and generated
candidates must be solvable by the line solver without guessing.

**Reviewing the project? Start with [the audit guide](docs/audit.md).** It lists the main code
paths, checks to run and validation still needed. [Artwork notes](docs/puzzle-art.md) explain
the picture review and how to generate a visual catalogue.

## Build and test

Install the SDK pinned in [global.json](global.json). Package versions are in
[Directory.Packages.props](Directory.Packages.props). The SDK and workload set are pinned
separately; both currently use `10.0.401`.

Core, Data and Presentation target plain `net10.0` and can be tested without MAUI workloads:

```powershell
$env:MSBuildEnableWorkloadResolver = 'false'
dotnet test tests/Squarebuzz.Core.Tests -c Release -- RunConfiguration.TreatNoTestsAsError=true
dotnet test tests/Squarebuzz.Data.Tests -c Release -- RunConfiguration.TreatNoTestsAsError=true
dotnet test tests/Squarebuzz.Presentation.Tests -c Release -- RunConfiguration.TreatNoTestsAsError=true
dotnet run tools/validate-puzzles.cs
dotnet run tools/check-strings.cs
```

On Linux or macOS, use `export MSBuildEnableWorkloadResolver=false` before the same `dotnet`
commands. Unset that variable before building the MAUI app.

For an app build, install the platform workload from the repository root:

```powershell
Remove-Item Env:MSBuildEnableWorkloadResolver -ErrorAction SilentlyContinue
dotnet workload install maui-android
dotnet workload --version
dotnet build src/Squarebuzz.App -c Release -p:SquarebuzzTargetFramework=net10.0-android
```

Use `maui-windows`, `maui-ios` or `maui-maccatalyst` for other platforms. Their targets are
`net10.0-windows10.0.19041.0`, `net10.0-ios` and `net10.0-maccatalyst`.
`SquarebuzzTargetFramework` limits restore to one platform without changing the framework of
referenced projects. Building the whole solution requires all selected platform workloads.

Apple builds require a Mac and the Xcode version selected in [CI](.github/workflows/ci.yml).
For Android Debug deployment, add `-t:Run` with an emulator or device connected; this also
deploys the assemblies required by Fast Deployment.

## Code map

| Directory | Responsibility |
| --- | --- |
| `src/Squarebuzz.Core` | Rules, clues, solver, generation, content and progression. No package dependencies. |
| `src/Squarebuzz.Data` | SQLite repositories and ordered schema migrations. References Core. |
| `src/Squarebuzz.Presentation` | ViewModels and application services. References Core and CommunityToolkit.Mvvm. |
| `src/Squarebuzz.App` | MAUI pages, rendering, platform services and dependency registration. |
| `tests` | Core, Data and Presentation tests, plus native acceptance instructions. |
| `tools` | Content validators, artwork report, sound generation and profiling tools. |
| `design` | Original visual specification and browser prototype; some behavior predates the app. |

`MauiProgram` connects the layers. Start a gameplay review with `GameSessionFactory`,
`GameSession` and `SessionOrigin`, then follow the calls from `GameViewModel`.

## Behavior to preserve

- **Authored saves:** ID and revision identify the exact board. Before changing a published
  board, preserve its entry in `archivedPuzzles` and increment its revision. Archives are
  available to Continue and Restart, and excluded from new-game selection.
- **Generated saves:** the request, seed and `GeneratorVersion` reproduce the board. Increment
  the version whenever an algorithm change alters any seed's output. Incompatible generated
  saves are removed at startup.
- **Replay:** Restart keeps the resolved board, seed, mode, hint budget and daily date.
  Next selects a new game. Timed trials are never saved.
- **Completion:** `GameCompletionService` uses a persistent journal so retries do not award
  progress twice. `GameSaveService` orders saves across session changes.
- **Input and scoring:** one drag is one undoable stroke and charges at most one mistake.
  Hint usage remains charged after undo. A win requires filled cells to match the solution;
  crossing every blank is optional.
- **Time:** `GameTimeTracker` uses a monotonic clock. Background time and modal pauses do not
  count as active play.

The generator builds a mirrored base, adds asymmetric details and passes candidates through
`UniqueSolutionGenerator`. The wrapper rejects boards the line solver cannot finish.
Density and clue-complexity checks live in `BlobPuzzleGeneratorTests`.

## Content and accessibility

The campaign uses 35 pictures at 5x5 and 95 at 10x10 as milestones. The order of the current
`puzzles` array affects those milestones; keep it stable during artwork revisions. Larger
generated sizes are 15x15, 20x20 and 25x25; the last requires a tablet or a sufficiently large display.

The app has 39 languages and 388 resource keys per language. Structural checks do not establish
translation quality; see [translation status](docs/translation-status.md).
[Font notes](src/Squarebuzz.App/Resources/Fonts/README.md) cover script support and licences.

When a screen reader is active, the board canvas gains a per-cell control overlay. Descriptions
include position, state and both clues. Native review must also cover focus, gestures, reduced
motion, narration, audio and haptics; see [the acceptance cases](tests/Native/README.md).

Settings apply immediately. Adding a setting requires a domain property, repository read/write
mapping, ViewModel change hook and consumer. Confirm all four.

The privacy policy is [PRIVACY.md](PRIVACY.md). About and Options open it at its GitHub address
(`ExternalLinks.PrivacyPolicy`) behind the parent gate, so the repository must be public and the
file must stay at that path on `master`. Update the policy before shipping anything that changes
what the app stores or sends. "Rate squarebuzz" is still a placeholder until the store IDs exist.
The About version line is read from `ApplicationDisplayVersion` and `ApplicationVersion` in the
app project; raise both for every store upload.

## Tools and CI

```powershell
node tools/review-puzzles.mjs          # writes artifacts/puzzle-review/index.html
dotnet run tools/validate-puzzles.cs   # grids, revision history and production solver
dotnet run tools/check-strings.cs      # resource keys and placeholders
dotnet run tools/generate-sounds.cs    # regenerates bundled sound effects
```

The artwork report requires Node.js and no npm packages. The prototype runs with `npm start`
from `design`. [Profiling instructions](tools/Profiling/README.md) cover the host runner and
device measurements.

[CI](.github/workflows/ci.yml) runs domain tests on Windows and Linux, content checks, Android
and Windows builds, and separate iOS/Mac Catalyst compile jobs. Native acceptance is separate.
Historical device reports identify their tested revisions; they do not certify the current tree.

## Licence

Code and generated sounds use the [BSD 3-Clause License](LICENSE). Fredoka and Quicksand use
the SIL Open Font License 1.1; retain the licence files beside the fonts.
