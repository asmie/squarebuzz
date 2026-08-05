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

Every screen the prototype sketched now exists, including both Trials features that were
deliberately held back until the domain could support them.

Games autosave every 15 seconds while playing, plus on pause, on quit and on leaving the
screen. A finished puzzle deletes its own save, so Continue never offers a solved board.
Saves store the player's marks and a seed — never the picture — so a generated puzzle is
rebuilt rather than stored.

**That makes generated saves sensitive to generator changes**, so each one records the algorithm
that produced it. A seed only means a picture while the generator still turns it into that
picture; change the algorithm and the marks come back attached to a board the player never saw.
Nothing crashes, which is exactly what makes it worth guarding.

- `GeneratorVersion.Current` identifies the algorithm. **Bump it whenever a change alters what any
  seed produces** — including changes that look cosmetic, like a different radius or one extra
  draw from the random source, because the seed's meaning is the whole algorithm.
- `SavedGame.CanBeRebuilt` is the rule: authored puzzles always can, generated ones only at the
  current version. Not "older than" — a database written by a newer build is equally unrebuildable
  by this one.
- `PurgeUnrebuildableAsync` runs **once at startup**, before the menu can show a count or Continue
  can list anything, so those two can never disagree about how many games are waiting.

Authored saves are unaffected: they are shipped content looked up by id, and carry version 0.

The Gallery doubles as a picture picker (reached from New Game, as in the prototype). Nothing
in it leaks an unsolved answer: unfound cards draw a uniform grid of blank tiles rather than
the solution in a muted colour, and Continue thumbnails draw the player's own marks.

Pause and Complete are overlays on the board rather than separate routes, so the in-progress
session never has to be serialised across a navigation just to show a summary over it.

## Large screens

The design doc's scaling note is the rule: *"Above 900 px in landscape the play column becomes a
row … Cell size is computed from the free rectangle, not hard-coded."* `GamePage.ApplyLayout`
implements it — on a wide landscape screen the board takes the left column at full height and the
action buttons move into a 320-unit side column as a 2×2 block, on the side the Handedness setting
puts them. It is done in code, from the page's own measured size, because MAUI has no media
queries; and by rearranging **one** visual tree rather than switching between two, since the board
is a stateful control holding the live session.

That matters more than it sounds: with the controls beneath it, a 25×25 in landscape was squeezed
to roughly half the cell size it can now use.

**Deviation from the doc:** it puts a mini-map and clue helpers in the right-hand column and keeps
the toolbar at the bottom edge. Neither of those exists, and on a tablet the whole board is
visible at once so a mini-map would show nothing new — the controls go there instead.

25×25 "Giant" is unlocked by `IDeviceScreen.IsLargeScreen` (≥760 units, or a tablet/desktop
idiom), and shown locked with a "Tablet" caption on phones rather than hidden, as the doc asks.
Android also lets tablets rotate freely while phones stay portrait (`MainActivity`), and the
activity handles the configuration change itself so rotating never recreates it and loses the
board.

No tablet AVD is installed here, so this was exercised by reconfiguring the running emulator —
which is enough, because the app's own tablet test is `SmallestScreenWidthDp >= 600`:

```bash
adb shell wm size 1600x2560 && adb shell wm density 320   # then relaunch the app
adb shell settings put system accelerometer_rotation 0
adb shell settings put system user_rotation 1             # landscape
adb shell wm size reset && adb shell wm density reset
```

Verified that way: Giant unlocks, a 25×25 generates in about 4 seconds and renders whole, the
wide layout appears in landscape and reverts in portrait, the session survives both rotations,
and the phone layout is unchanged.

## Daily puzzle, timed trials, and trophies

Trials holds today's puzzle, the Timed Trial ladder and the trophy cabinet. The winding trail that
used to live here as a fourth tab grew into the Levels campaign behind the menu's Play button —
see the Levels section below.

### Timed Trial

Three rungs, from the design: **Warm-up** (5×5 in 3:00), **Steady** (10×10 in 5:00) and
**Lightning** (the same 10×10 in 2:00). The jump that matters is the third — the same grid as the
second with well under half the time, which is what makes it read as a dare rather than as more of
the same.

This is **the only way to lose the game**, and that is most of the work. `GameSession` gained a
`TimeLimit`, a `Remaining` that never goes negative, an `IsTimeUp`, and an `IsOver` that means
"won or lost". Every play guard moved from `IsSolved` to `IsOver`, so a board whose clock has
stopped refuses marks, hints and undo while the view catches up on the next tick. Solving on the
final tick counts as a **win**: `IsSolved` is checked first, and there is a test for exactly that
ordering.

Some deliberate choices:

- A trial is **generated and Sharp**. Racing a clock with the helpers on measures tapping speed
  rather than reading clues, and both trial sizes fall inside the authored range — so without
  `ForceGenerated` a trial would hand out one of the twelve shipped pictures, which a player may
  already know by heart.
- **Trials are never saved.** A race you can put down and pick up tomorrow is not a race, and a
  countdown frozen in the Continue list would mean nothing. `AutosaveAsync` skips timed sessions,
  which also means the feature needed no schema change at all.
- Losing costs the attempt, not the afternoon: nothing is recorded, and the overlay leads with
  another go. **Try again** draws a fresh picture and a full clock rather than handing back the
  same grid, which would let a player beat the trial from memory.
- Elapsed time is clamped to the limit, so a loss does not get recorded as taking longer the
  coarser the caller's tick happens to be.

### Levels (the Play campaign)

The menu's primary button is **Play**: a 600-level campaign that replaced the old twelve-stop
Puzzle Path. The trail keeps the Path's visual language — finished levels show a star in the
accent colour, the current one is drawn larger in the primary colour, the rest are numbered and
dimmed, and nodes lean sideways on the prototype's eight-step cycle (0, 26, 46, 26, 0, −26, −46,
−26 — a sampled sine) — but it is a grouped, virtualizing `CollectionView`, because six hundred
nodes cannot be built up front the way twelve could.

Levels climb through the board sizes in bands — 1–40 are 5×5, 41–200 are 10×10, 201–400 are
15×15, 401–600 are 20×20 — with generator difficulty ramping 1→5 inside each band. The campaign
deliberately stops at 20×20: 25×25 is tablet-only, and a campaign whose last stretch is walled
off on a phone would punish the players who got there. `LevelCatalog` is a pure function of the
level number and the shipped content: every generated level's seed comes from the level number
(the daily puzzle's mixing scheme with a different salt), and the 70 authored pictures are woven
into their size's band as evenly spaced milestone levels, in content-file order, locked packs
included — meeting a fairy-tale picture at its level is how that pack is earned.

The only stored campaign state is one integer, `highest_level` on the progress row: unlocking is
strictly linear, completions record their level with max semantics (replaying an old level never
winds the campaign back), and a parent's progress reset wipes it. Level games autosave and resume
like any other, carrying their level number through the save table. Finished levels are
replayable; locked ones are shown, numbered and inert rather than hidden — the same call the
design makes for the locked 25×25 size card: *visible, explained, not hidden*. Quick game (the
old New Game screen) keeps the free-choice flow, and its win screen's "Next" never serves the
picture just solved twice in a row.

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

## Puzzle generation

`BlobPuzzleGenerator` scatters overlapping circular blobs over the left half of the grid and
mirrors it, which is what makes generated pictures read as symmetrical creatures.
`UniqueSolutionGenerator` wraps it and rejects anything that cannot be solved by pure logic, so
the game's promise holds for generated puzzles as well as authored ones.

**Coverage is driven by a target fill, not by a blob count.** The ported version placed
`width × height / 26` blobs of radius up to `width / 5`, and neither figure scaled: at 10×10 that
is four small blobs, so most rows came out *empty* and the empty-line repair filled them with a
pair of cells on the mirror axis. The result was a bar down the middle of most generated 10×10
pictures — the daily puzzle's size. Everything passed: the grids were symmetrical, had no empty
line, and were uniquely solvable. They were just poor pictures.

Three changes, each measured over 200 seeds per size:

| | before | after |
|---|---|---|
| Fill at 10×10 | 34.5% | 54.2% |
| Fill spread across sizes | 34.5%–57.9%, non-monotonic | 53%–62% at every size |
| Mirror-axis artefact rows, 10×10 | **4.35 of 10** (worst 8) | **0.64** |
| Mirror-axis artefact rows, 15×15 | 3.53 of 15 (worst 11) | 0.35 |
| Clue runs per row, 25×25 | 3.58 | 2.97 |
| Uniquely solvable, 10×10 | 53.3% | **89.5%** |
| Uniquely solvable, 25×25 | 58.7% | **83.5%** |

- Blob centres come from a **shuffled permutation** of the half-grid's cells rather than being
  drawn independently, so no band of rows can be left uncovered by chance.
- Blobs are added **until the half reaches the difficulty's target density**, so coverage scales
  with the grid instead of being a constant tuned for 5×5.
- The empty-line repair attaches its cell **under a filled cell of the nearest occupied row**
  instead of to the mirror axis. Axis repairs all lined up with each other, which is what produced
  the bar; attaching them to their neighbour reads as the shape tapering off.

Blobs are also nearly solid (85%) rather than thinned by a third: every isolated cell is a clue
run of 1, so a speckled picture is also a tedious set of clues.

`BlobPuzzleGeneratorTests` asserts these as invariants — density band per size, a ceiling on
axis-artefact rows and sparse columns, a ceiling on clue runs per line, and that difficulty still
thins the picture. They are statistical tests over 200 seeds, which is what it takes to catch a
defect that is about *distribution* rather than about any single puzzle.

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

### The board

A `GraphicsView` contributes nothing to the accessibility tree, so the puzzle used to be *absent* —
a screen-reader user could not perceive it, let alone play it. Two things now cover it.

The board carries a summary that updates on every move: "Puzzle board, 10 by 10. 24 of 34 squares
filled."

And when a screen reader is running, `GamePage.BuildCellOverlay` puts **one focusable button per
square** over the grid, each announcing where it is, what is in it, and the two clues that govern
it — "Row 3, column 3, empty. Row clue 6 6. Column clue 2 2 2 4 4." Activating one plays the move,
and every square's description is refreshed immediately afterwards, because a screen reader reads
whatever an element says at the moment it takes focus.

Three decisions worth knowing:

- **Only when something is listening.** A 20×20 grid is four hundred buttons: worth it for a player
  who cannot otherwise play, pure waste for everyone else. `IAccessibilityState` asks Android
  whether *touch exploration* is on — not merely whether some accessibility service is enabled,
  since switch access and screen dimmers do not navigate square by square. Platforms without an
  implementation report false, erring towards the cheap path. Verified both ways: 100 cell nodes for
  a 10×10 with a reader on, **zero** with it off.
- **Alignment comes from the board's own `BoardLayout`**, not a second calculation — the overlay is
  inset by the clue gutters and given one row and column per cell at exactly the drawn cell size.
  Two independent computations of the same geometry would drift the first time either changed.
- **The clues are repeated on every square.** Verbose, and deliberate: a player who cannot see the
  gutters has no other way to know them, and asking a child to hold twenty numbers in their head is
  not an alternative. The tidier design — focusable row and column headers that announce clues only
  when focus crosses into a new line — needs control over focus order, which MAUI does not offer.

The board summary drops its "cannot be reached" caveat when the overlay exists, so the description
never contradicts the screen it describes.

**Not verified:** TalkBack's own double-tap gesture. It activates the accessibility-focused node,
which for an `android.widget.Button` is the same click the overlay handles — and the nodes report
`clickable=true focusable=true class=android.widget.Button` — but injected `adb` taps cannot drive
TalkBack's focus, so the click path was exercised directly instead. Nor has the 400-button case been
timed on real hardware.

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

### Reproducing CI locally

**CI builds a clean checkout; a local build is incremental.** That difference hides analyser
failures completely: analysers only run when a file is actually compiled, so a rule that would fail
the build stays silent for as long as the outputs are up to date. The first real CI run failed on
`CA1707` in both `domain` jobs while every local build had been green for weeks.

The faithful reproduction is to build from what git actually has, not from the working directory:

```bash
git archive HEAD -o /tmp/head.zip && unzip -q /tmp/head.zip -d /tmp/head && cd /tmp/head
```

That catches both halves of the problem at once — analysers running from scratch, and **files that
exist on disk but were never committed**. The `CA1707` failure was exactly the latter: the
suppression lived only in `.editorconfig`, which had never been `git add`ed. Build behaviour must
come from files the build owns, so it now lives in the test `.csproj` files instead, and
`.editorconfig` is editor style only.

`-t:Rebuild` forces the analysers to run but will not tell you about an uncommitted file.

### The first run

Three failures were found before it ever ran, by auditing the workflow and running each job's
commands locally, and all three would have hit on the first push:

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

Then it ran for real, and failed in two independent ways.

**`domain`, both runners:** `CA1707` — see above. `android` and `app-warnings` were unaffected.

**`ios`:** not a code break at all, and it took two rounds because there are *two* environment
constraints that pull in opposite directions.

Round one:

```
error : This version of .NET for iOS (26.5.10301) requires Xcode 26.6.
        The current version of Xcode is 26.5.
```

A .NET for iOS pack refuses an Xcode older than the one it was built for, and 26.5 is the image
**default** — with 26.6 installed alongside it. So: select the newest Xcode.

Round two got past that and failed further in:

```
xcodebuild: error: SDK ".../Xcode_26.6.0.app/.../MacOSX.sdk" cannot be located.
xcrun: error: unable to find utility "actool"
```

A non-default Xcode has not had its first-launch component install run, so `xcodebuild` cannot
resolve its SDKs — the newest Xcode is *selectable* without being *usable*.

So the job no longer encodes a version or an assumption. It tries each Xcode newest-first, runs
`xcodebuild -runFirstLaunch`, and keeps the first one whose toolchain actually resolves the macOS
SDK and `actool` — the very thing the build needs later. If none can, it fails with the list it
tried, so the log answers the question instead of prompting another round.

Two things worth keeping in mind here:

- `sort -t. -k1,1n -k2,2n -k3,3n **-r**` does **not** reverse a keyed sort — a trailing `-r` is
  silently ignored and you get oldest-first. It has to be per-key: `-k1,1nr -k2,2nr -k3,3nr`. That
  bug would have made the job pick Xcode 26.0.1.
- What the image contains is checkable without a Mac:
  `actions/runner-images/images/macos/macos-26-arm64-Readme.md` lists every Xcode, which SDKs each
  one carries, and which is default. That is where the "26.6 exists but is not default" fact came
  from, rather than from guessing.

**If it still fails**, the remaining lever is the other direction: pin `dotnet-version` to an
earlier feature band (the image ships 10.0.103, 10.0.203 and 10.0.302) so the iOS pack matches the
image's default Xcode, instead of moving Xcode to match the pack.

What is verified locally, on Windows: the `domain` job's full sequence including the trx
artifacts, the `android` job's build and that `*-Signed.apk` matches what it produces, and the
`app-warnings` build — all from a clean export of what git holds. What is **not** verified:
anything ubuntu-specific in `domain`, the whole `ios` job, and every `dotnet workload install`
step — none of which can run here.

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
- **Fonts are named by alias, never by file.** `Display`, `Body` and `BodyBold` are registered in
  `MauiProgram` and referred to from `Styles.xaml` — see `Resources/Fonts/README.md`, which also
  records where the files came from and how to cut a different weight. Nothing uses
  `FontAttributes="Bold"`: a rounded face smears when the platform fakes weight, so bold text names
  the bold cut. The board's clue numbers are the one exception to "styles decide the font", because
  a canvas does not inherit them.

## Licence

MIT — see [LICENSE](LICENSE).
