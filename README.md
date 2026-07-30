# squarebuzz

A nonogram (Picross) game for 6–12 year olds, built with .NET MAUI for **iPhone, iPad,
Android phone and tablet, and Windows**.

Fill squares from the numeric clues on each row and column and a hidden picture appears.
Every puzzle is solvable by pure logic — never by guessing.

## Repository layout

```
src/Squarebuzz.Core     Pure domain: game rules, clue maths, constraint solver, puzzle
                        generation. References no MAUI and no platform APIs, so the whole
                        game is unit-testable without an emulator.
src/Squarebuzz.Data     SQLite persistence. Implements the repository interfaces declared
                        in Core.Abstractions, so the domain never sees a database.
src/Squarebuzz.App      MAUI application: ViewModels, XAML pages, board rendering,
                        theming, platform heads.
tests/…Core.Tests       xunit suite over Core. Runs on any OS.
design/                 The original Claude Design prototype, kept as the visual source of
                        truth. Not compiled.
```

Dependencies flow strictly `App → Data → Core`. Core depends on nothing.

## Screen status

| Built | Scaffolded (shared `ComingSoonPage`) |
|---|---|
| Splash, Onboarding, Menu, New Game, Board, Pause, Complete, Options, Continue | Trials, About, Gallery, How to Play |

Games autosave every 15 seconds while playing, plus on pause, on quit and on leaving the
screen. A finished puzzle deletes its own save, so Continue never offers a solved board.
Saves store the player's marks and a seed — never the picture — so a generated puzzle is
rebuilt rather than stored.

Pause and Complete are overlays on the board rather than separate routes, so the in-progress
session never has to be serialised across a navigation just to show a summary over it.

Every scaffolded route is registered and navigable today — they share one page and differ only
by a `titleKey` route parameter, so the menu is fully explorable with no dead ends.

## Continuous integration

`.github/workflows/ci.yml` runs four jobs:

| Job | Runner | Why |
|---|---|---|
| `domain` | ubuntu + windows | Builds and tests Core and Data. No MAUI workload, so it is fast — and running on both OSes proves the "Core runs anywhere" goal and exercises the platform-specific SQLite native. |
| `android` | windows | Builds the APK and uploads it as an artifact. |
| `ios` | **macos** | iOS cannot be built on a Windows dev machine, so without this an iOS-only break would go unnoticed until release. Builds the simulator target, which links the real thing without needing a signing identity. |
| `app-warnings` | windows | The MAUI head relaxes warnings-as-errors for generated code; this re-builds it with `-warnaserror` so app-layer warnings still fail the build. |

## Requirements

- .NET SDK 10.0 with the `maui-windows`, `android`, `ios` and `maccatalyst` workloads
- Visual Studio 2026 (the solution uses the `.slnx` format)
- Android SDK + an emulator or device for Android
- **iOS/macOS:** a network-paired Mac with Xcode. The managed code compiles on Windows,
  but deploying to a simulator or device does not.

## Build and run

```bash
dotnet build squarebuzz.slnx                       # everything, all target frameworks
dotnet test tests/Squarebuzz.Core.Tests            # domain tests, no device needed

# Android (emulator running):
dotnet build src/Squarebuzz.App -f net10.0-android -t:Run

# Windows:
dotnet build src/Squarebuzz.App -f net10.0-windows10.0.19041.0 -t:Run
```

Use `-t:Run` for Android rather than `adb install`. Debug builds use Fast Deployment, which
pushes assemblies separately from the APK; installing the `.apk` by hand produces a
`No assemblies found … Exiting` crash on launch.

The Windows target needs a **2.x Windows App Runtime**. If launching fails with
`REGDB_E_CLASSNOTREG`, that runtime is missing — Visual Studio installs it as part of
deployment.

## Viewing the original design

The prototype is a self-contained web app that compiles itself in the browser, so it only
needs to be served over HTTP:

```bash
cd design && npm start        # → http://localhost:5173
```

It has no npm dependencies; `tools/serve.mjs` is a zero-dependency static server.

## Conventions

- **MVVM** throughout, via `CommunityToolkit.Mvvm` source generators.
- **Central Package Management** — package versions live only in `Directory.Packages.props`.
- Warnings are errors in Core and Data. The MAUI head relaxes this because generated
  partials trip a few analyser rules.
- Colours are never hard-coded in pages. They come from the swappable theme dictionaries in
  `Resources/Themes` via `DynamicResource`, which is what makes 3 themes × 3 accents work at
  runtime.

## Licence

MIT — see [LICENSE](LICENSE).
