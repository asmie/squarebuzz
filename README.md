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

Every screen is real. There is no placeholder page and no dead end in the menu: Splash,
Onboarding, Menu, New Game, Board, Pause, Complete, Options, Continue, Gallery, About,
How to Play and Trials are all implemented.

Two things the prototype sketched are **deliberately absent** rather than present and hollow:
the timed modes and the "puzzle path" progression. Both need domain work that does not exist
yet — a countdown inside `GameSession`, and a progression model — and a tab that looks
playable but is not would be worse than one that is not there.

Games autosave every 15 seconds while playing, plus on pause, on quit and on leaving the
screen. A finished puzzle deletes its own save, so Continue never offers a solved board.
Saves store the player's marks and a seed — never the picture — so a generated puzzle is
rebuilt rather than stored.

The Gallery doubles as a picture picker (reached from New Game, as in the prototype). Nothing
in it leaks an unsolved answer: unfound cards draw a uniform grid of blank tiles rather than
the solution in a muted colour, and Continue thumbnails draw the player's own marks.

Pause and Complete are overlays on the board rather than separate routes, so the in-progress
session never has to be serialised across a navigation just to show a summary over it.

## Daily puzzle and trophies

Trials holds today's puzzle and the trophy cabinet.

The daily is **generated from the date**, not drawn from the twelve authored pictures — with
only twelve a rotation would repeat every twelve days. `DailyPuzzle.SeedFor` hashes the day
number, so every player gets the same picture on the same day and closing the app returns to
the identical board. It forces generation explicitly (`NewGameOptions.ForceGenerated`) because
10×10 falls inside the authored range and the factory would otherwise serve a shipped picture.
"Start over" on the daily re-serves *today's* puzzle rather than a fresh seed.

The nine trophies come from the prototype's artwork; **their thresholds are a proposal**, all
in `TrophyEvaluator` as named constants, and worth a look before release:

| Trophy | Rule |
|---|---|
| First Picture | Any puzzle finished |
| Week Streak | 7-day streak |
| No Hints | Finish with all hints unused |
| Speedy | A 5×5 in under 60 s |
| 100 Blocks | 100 filled squares in total |
| Dino Fan | Every puzzle in the `dinos` pack |
| Night Owl | Finish between 20:00 and 06:00 local |
| Perfect Ten | 3 stars *and* zero mistakes on a 10×10 or larger |
| Collector | Every picture in every pack, locked ones included |

## Settings

Every switch in Options changes something. That is worth stating because it was not true for
a while: seven settings were persisted and read back faithfully but never consulted by anything,
which is a failure a passing build and a green test suite both report as success.

| Setting | What it actually does |
|---|---|
| Theme: Light / Dark / **Auto** | Auto takes light/dark from the OS and keeps following it while the app runs — see below |
| Colour-blind | A third palette. Deliberately **not** overridden by Auto: it is an accessibility choice, not a brightness |
| Buttons on: Left / Right | Reorders the action row so Undo — the most-reached button — sits at the chosen end |
| Screen-time reminder | Off / 15 / 30 / 60 min of **play**, then a break overlay |
| Haptics | Gated in one place (`GamePage.Buzz`), so a new buzz cannot skip the switch |
| Sound effects | Six effects, gated in one place (`AudioService.Play`) — see below |
| Music | An eight-second loop, stopped when the app leaves the foreground |
| Voice narration | Reads the onboarding cards and the game's messages aloud — see below |

**Auto and `UserAppTheme`.** Following the OS is not simply "read `RequestedTheme`". Setting
`Application.UserAppTheme` overrides `RequestedTheme`, so a service that writes it and then
reads it back gets its own answer instead of the system's. Under Auto the app leaves
`UserAppTheme` at `Unspecified` and reads `PlatformAppTheme`, which always reports the OS.

**Screen time counts play, not uptime.** `IScreenTimeMonitor` is a singleton so the count is the
sum across every puzzle in an app run — a child who finishes five boards has been on the screen
for all five. It only counts ticks where the clock is actually running, and it reports the limit
being crossed exactly once, because a reminder that reopened every second would make the board
unusable. The break overlay stops the clock but is not a lockout: "A little longer" resumes.

## Sound

`Resources/Raw/*.wav` are **generated, not recorded**, by `tools/generate-sounds.cs`:

```bash
dotnet run tools/generate-sounds.cs
```

Each sound is a few numbers — pitch, length, envelope, peak level — so what it will sound like is
a property of that file, and changing one is reviewable in a diff rather than requiring an audio
editor. They are sine fundamentals with one quiet harmonic, raised-cosine envelopes so nothing
starts or ends on a discontinuity, all pitched from one C major scale so no two can clash, and
peaking at −14 dBFS. That makes them plain and safe; it does not make them good. **Treat them as
placeholders for a sound designer**, and note that nobody has yet listened to them on a real
device — their measured properties were verified, their musicality was not.

`music.wav` is a chord pad rather than a tune, on purpose: a melody heard for an hour is what
makes a child turn the sound off, and unlike a 70 ms blip a tune is a composition rather than
arithmetic. It loops seamlessly by construction — every frequency, including the slow swells, is
rounded to a whole number of cycles across the eight seconds, so the step at the wrap point is
smaller than an ordinary sample-to-sample step within the file. It is written at 11 kHz because
its highest partial is 392 Hz; at 44.1 kHz this one file would outweigh everything else in the
app four times over.

**There is deliberately no mistake sound.** The design doc's motion spec says of the mistake
shake: "No sound sting by default." The shake and the warn tint already say it.

Two things worth knowing about the plugin (`Plugin.Maui.Audio` 4.0.0):

- Audio attributes must be passed to **each** `CreatePlayer` call. Configuring them once on the
  app builder is not enough — `adb shell dumpsys audio` showed every player registered as
  `USAGE_UNKNOWN` until `AudioService.OptionsFor` set them per player.
- The audio-focus and iOS session-category options in its documentation are **not in the released
  package**. So focus is unmanaged: the loop is stopped by hand on backgrounding
  (`App.CreateWindow`), but an incoming call will talk over it rather than pause it.

Verifying audio without being able to hear it: `adb shell dumpsys audio` lists every registered
player for a pid with its state, which is enough to prove that the assets loaded, that a given
action starts a given player, that the switches gate them, and that a mistake starts nothing.

## Voice narration

`NarrationService` speaks through the platform's text-to-speech engine — no assets, no package.
It reads the three onboarding cards, the transient game messages ("line done", "oops", "hint
used"), the win, and the break reminder. Narration **interrupts** rather than queues: these are
captions for what is on screen now, and a backlog describing screens the player has already left
would be worse than silence.

It is **not a screen reader.** It speaks what the game chooses to speak, because the youngest
players in the target range can read the clue *numbers* long before they can read "Auto-cross
finished lines". Screen-reader support is separate, and is covered below.

**A voice is per-language and per-device.** Nothing is spoken in a language the device has no
voice for, and the service deliberately does **not** fall back to another language's voice: an
English engine reading Polish is noise, not degraded narration, and a child who cannot read the
words cannot tell that the voice is wrong either. When no voice is found, the switch carries the
note "No voice for this language on this device" rather than failing silently.

To exercise that path on an emulator:

```bash
adb shell pm disable-user --user 0 com.google.android.tts   # then relaunch the app
adb shell pm enable --user 0 com.google.android.tts
```

Verifying speech without hearing it: the TTS engine registers its own audio player under *its*
uid, so `dumpsys audio` shows a player appear from `com.google.android.tts` for the duration of
each utterance — enough to prove a given action speaks, that the switch gates it, and that
switching language still produces speech.

## Screen readers

Every control that a screen reader could not otherwise name now carries
`SemanticProperties.Description`: the ⏸ button, the timer, the ▶ and ✕ on each saved game (two
rows of unlabelled buttons where one is destructive), the −/+ cell-size steppers, each gallery
card, each trophy, and the star row (`★★☆` is not something that can be spoken). Page titles
carry `HeadingLevel`, and the decorative drawables are marked
`AutomationProperties.ExcludedWithChildren`.

Descriptions are composed in the ViewModels so they are localised and stay in step with state.
Gallery cards deliberately say **nothing** about an unfound picture beyond its size: naming it, or
even hinting at its shape, would spoil the discovery for exactly the player who has to rely on
this text instead of the artwork.

**The board is the honest gap.** A `GraphicsView` contributes nothing to the accessibility tree,
so before this the puzzle was simply *absent* — a screen-reader user could not perceive it at all.
It now carries a description ("Puzzle board, 10 by 10. 24 of 34 squares filled.") that updates on
every move, and that description says outright that the squares cannot be reached by touch
exploration yet. **The game is therefore still not playable with a screen reader.** Making it so
means an accessible overlay — one focusable element per cell, each naming its row and column, its
state, and its two clues — which is a real feature and not a label pass. It is also the reason
the board description tells the truth rather than implying more than it delivers.

Announcements go through `SemanticScreenReader.Announce` at the same funnel narration uses, so
toasts, the win and the break reminder are spoken even though nothing takes focus when they
appear. That is deliberately **not** tied to the Voice narration switch: the player's screen
reader is their choice, not ours to switch off. If both are on, both speak.

Verifying it: `adb shell uiautomator dump` writes the whole accessibility tree, so
`content-desc` can be inspected per node without TalkBack running at all.

```bash
adb shell uiautomator dump /sdcard/t.xml && adb exec-out cat /sdcard/t.xml
```

That is how two silent bugs turned up — a timer description frozen at `0:00` while the visible
clock ran, and a board description that never refreshed after a move. Neither is visible on
screen, and neither would fail a test that did not know to look.

## Continuous integration

`.github/workflows/ci.yml` runs four jobs:

| Job | Runner | Why |
|---|---|---|
| `domain` | ubuntu + windows | Builds and tests Core and Data. No MAUI workload, so it is fast — and running on both OSes proves the "Core runs anywhere" goal and exercises the platform-specific SQLite native. |
| `android` | windows | Builds the APK and uploads it as an artifact. |
| `ios` | **macos** | iOS cannot be built on a Windows dev machine, so without this an iOS-only break would go unnoticed until release. Builds the simulator target, which links the real thing without needing a signing identity. |
| `app-warnings` | windows | The MAUI head relaxes warnings-as-errors for generated code; this re-builds it with `-warnaserror` so app-layer warnings still fail the build. |

**It has never actually run.** Nothing has been pushed, so every statement below comes from
auditing the workflow against the repository and running each job's commands locally — not from a
green tick. Three failures were found and fixed that way, all of which would have hit on the
first push:

- `dotnet restore a.csproj b.csproj` — MSBuild takes **one** project and rejects the second as an
  unknown switch (MSB1008), so the `domain` job died at its first real step on both runners.
- **Restore covers every framework a project declares**, not just the one being built. The three
  platform jobs each install a single workload on purpose, so restore alone would have demanded
  the other three and failed with NETSDK1147. Fixed with a `SquarebuzzTargetFramework` property
  that only `Squarebuzz.App` reads — overriding `TargetFrameworks` on the command line does not
  work, because a command-line property is global and redefines `Squarebuzz.Core` and
  `Squarebuzz.Data` too, which target plain `net10.0`.
- `dotnet-quality: preview` pinned the SDK to a pre-release build of a framework that has since
  shipped. Removed; `10.0.x` resolves to the latest release.

What is verified locally, on Windows: the `domain` job's full sequence including the trx
artifacts, the `android` job's build and that `*-Signed.apk` matches what it produces, and the
`app-warnings` build. What is **not** verified: anything ubuntu-specific in `domain`, the whole
`ios` job, and every `dotnet workload install` step — none of which can run here.

There is no `global.json`, so CI takes the latest 10.0.x while this machine builds on
`10.0.400-preview`. Pinning would make them agree but constrains the local toolchain, so it is
left as a decision rather than assumed.

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

# One platform only, without needing the other three workloads installed (what CI does):
dotnet build src/Squarebuzz.App -c Release -p:SquarebuzzTargetFramework=net10.0-android
```

Use `-t:Run` for Android rather than `adb install`. Debug builds use Fast Deployment, which
pushes assemblies separately from the APK; installing the `.apk` by hand produces a
`No assemblies found … Exiting` crash on launch.

The Windows target needs a **2.x Windows App Runtime**. If launching fails with
`REGDB_E_CLASSNOTREG`, that runtime is missing — Visual Studio installs it as part of
deployment.

Useful for checking the Auto theme on an emulator, since it needs the OS setting to change
underneath a running app:

```bash
adb shell cmd uimode night yes    # OS to dark; the app should follow with no restart
adb shell cmd uimode night no
```

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
- **Adding a setting is three steps, and the compiler checks none of them.** Put it in
  `GameSettings`, read *and* write it in `SqliteSettingsRepository`, and add a
  `partial void On<Name>Changed` in `OptionsViewModel` so the change persists — the MVVM
  generator only emits that hook for properties that declare one, so a missing hook is a
  setting that silently never saves. Then make something actually consume it. Four settings
  had already gone the whole way to the database and back without step four.
- **Known MAUI quirk:** `FlowDirection` set on a layout does reverse its columns, but only
  before that layout has measured. Binding it to a value that changes later leaves the children
  where they were. Bind `Grid.Column` per child instead when the order has to change at runtime.
- **Known MAUI quirk:** a `Label` inside a `DataTemplate` can reserve height for one line
  fewer than it needs and silently clip the last line. It cost real text on How to Play. If
  you edit or translate a multi-line string in a templated list, check it still renders in
  full on a device — the build will not tell you.
- Colours are never hard-coded in pages. They come from the swappable theme dictionaries in
  `Resources/Themes` via `DynamicResource`, which is what makes 3 themes × 3 accents work at
  runtime.

## Licence

MIT — see [LICENSE](LICENSE).
