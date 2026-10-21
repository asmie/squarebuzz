# Native acceptance checks

Run these cases on installed Release builds after the unit tests. A successful build, a UI
hierarchy dump, or an emulator screenshot does not establish audio or screen-reader usability.
Record each case as **passed**, **failed**, or **not run**, with the build and device used.
The latest execution record is [release-1.0.1.md](release-1.0.1.md).
Earlier platform evidence remains in [RESULTS.md](RESULTS.md).

## Sharing the app

Run on both Android and a packaged Windows build. These checks require a person to choose a
share target; a successful compile does not confirm delivery to another app.

1. Open Settings and select **Share squarebuzz** in the grown-ups section. Cancel the parent
   gate or enter a wrong answer: no share sheet should open.
2. Pass the gate. Confirm that the native Android Sharesheet or Windows share window opens.
3. Choose a disposable draft or copy-link target and inspect the payload without sending it:
   Android must share `https://play.google.com/store/apps/details?id=fun.asmie.squarebuzz`;
   Windows must share `https://apps.microsoft.com/detail/9P308NW6NSXM`. Neither payload should
   contain a review URI, private game data, or a local file path.
4. Open that link in a browser and confirm the intended public store listing is available.
5. Dismiss the share sheet, return to Settings, and share again. Cancellation must not show an
   error or a claim that the link was sent. Check that gameplay and settings remain intact.
6. Check a long-label language and an RTL language; the button and parent gate remain usable.
   If the system cannot open sharing, the app should show a localized notice with the link.

## Text and collection goals

Use the current language's labels when following these cases. Check English, Polish, an RTL
language and a language with long labels, then record remaining locales for native review.

1. Onboarding's first example says `2 1` and shows two filled squares, a gap and one filled
   square. The next card explains filling and X without requiring a particular input mode.
2. How to Play shows both placements of the eight-square group and the six shared squares.
   At large text sizes, scroll through all five lessons and confirm no sentence is clipped.
3. Change the hint allowance in Options, return to Quick game and verify the displayed number.
   Repeat with hints disabled, unlimited hints and Sharp. The description must follow the
   actual rules even when the selected mode did not change.
4. Inspect reset behind the parent gate without confirming it. It must explain that progress,
   saves, stars and trophies are removed, and settings remain. Cancel returns safely.
5. Inspect the three replacement trophy goals: 7 different pictures, 3 different packs and
   30 different pictures. Previously earned IDs 2, 7 and 12 remain earned after an update.
6. With a screen reader, distinguish an unmarked square from a square marked empty. Check
   complete coordinates and clues, as well as pronunciation of the revised tutorial.

## Build and evidence

Use the SDK/workload versions pinned in `global.json` and the platform prerequisites in the
root README. Use a dedicated test device or emulator with disposable game progress. The smoke
runner updates the installed app and force-stops it; it preserves existing application data.
It does not start an emulator, choose a device, clear logcat, or change OS preferences.

From the repository root in PowerShell:

```powershell
dotnet build src/Squarebuzz.App -c Release -p:SquarebuzzTargetFramework=net10.0-android -warnaserror
```

After the build succeeds, list devices and run the smoke check with an explicit serial:

```powershell
$adbPath = Join-Path $env:LOCALAPPDATA 'Android/Sdk/platform-tools/adb.exe'
& $adbPath devices -l
dotnet run tools/check-android.cs -- --serial emulator-5554 --adb $adbPath --apk src/Squarebuzz.App/bin/Release/net10.0-android/fun.asmie.squarebuzz-Signed.apk
```

The .NET 10 file-based runner uses a modern Android device supporting `cmd package
resolve-activity`. It verifies installation, matching input/installed APK hashes, launcher
resolution, cold startup, and Home/resume. It records screenshots, process logcat, device/OS
settings, command results, and APK SHA-256 under ignored `artifacts/native/`. A nonzero exit
means the smoke check failed. Read `result.json` and inspect screenshots and logs even on a pass.
This does not exercise gameplay, inspect database contents, or listen to audio.

During the manual cases, capture the current screen without restarting the app:

```powershell
dotnet run tools/check-android.cs -- --serial emulator-5554 --adb $adbPath --capture paused --expect Paused --expect Resume
```

`--expect` checks exact text or accessibility descriptions present in the app's UI hierarchy;
repeat it for additional expectations. Use the current language's text. UIAutomator requires
an idle screen: pause the game first. Continuous animation or timer updates can prevent a dump;
the runner fails instead of reusing a stale file. Keep screen-reader interaction testing separate
from hierarchy capture: ordinary UiAutomator sessions suppress accessibility services and can
temporarily remove the app's accessible-cell overlay. Use screenshots and physical interaction
for reader checks, and verify the reader is active before interpreting results.
An uncompressed hierarchy can retain covered nodes that the reader skips;
presence alone does not prove visibility, focusability, focus order or spoken output.

For manual results record source SHA plus any working-tree changes, APK/package hash, OS build,
device, display size/density, font scale, theme, language, reader version, input method, and case
steps. Keep recordings/logs with the test run; do not add private device data to the repository.
Restore any OS settings changed for a case to their recorded original values afterward.

## Windows board rendering regression

On Windows, run the unpackaged Win2D regression before testing Quick Game in the app:

```powershell
dotnet publish tests/Native/WindowsRendering -c Release -o artifacts/windows-rendering
& ./artifacts/windows-rendering/Squarebuzz.WindowsRendering.exe
```

The runner uses the production font resolver and board renderer with native Win2D, checks that
the bundled Quicksand face loads, and draws 40 combinations of board size, available width,
clue size and marks. It exits nonzero on failure. Run the executable from a different working
directory to check that font lookup follows the executable's location.

This covers the unpackaged `ms-appx` font crash when the first board is drawn. It does not
exercise Shell navigation, input, audio, saves or an installed MSIX package; those still need
the interactive cases below.

## Required platform coverage

| Platform | Required environments |
| --- | --- |
| Android | Installed Release APK on a touch phone and a tablet; TalkBack; a supported older OS and current OS. Emulator checks supplement physical touch/audio/haptic checks. |
| iOS | Current-commit simulator CI build plus installed app on an iPhone and iPad; VoiceOver and real audio. |
| Mac Catalyst | Current-commit CI build, then installed desktop smoke and window resizing. Signing/installation is separate from unsigned CI compilation. |
| Windows | Installed Release app; small and wide windows, keyboard/mouse, Narrator and touch where available. |

## Cases

### D1: Apple CI

Find the run for the commit being reviewed, then inspect both **iOS** and **Mac Catalyst** jobs
and their build steps. Record run URL, full `headSha`, job conclusions, selected Xcode and workload.
A successful older workflow or a skipped matrix entry does not pass this case.

```powershell
gh run list --repo asmie/squarebuzz --limit 10 --json databaseId,headSha,status,conclusion,url
gh run view RUN_ID --repo asmie/squarebuzz --json headSha,jobs,url
```

### D2: Lifecycle and persistence

1. Start a known puzzle. Fill and cross identifiable cells; record elapsed time and mistakes.
2. Home/background for at least 30 seconds, resume, pause, then wait again. Board and mode remain
   correct; only active play adds elapsed time. Repeat with the app switcher/minimized window.
3. Quit to menu, inspect Continue, force-stop/terminate, cold launch, and resume. Verify cells,
   crosses, hints, mistakes and elapsed time, not merely the presence of a saved row.
4. Restart from pause: same puzzle identity, empty board, reset per-game counters. For a daily
   game started before midnight, resume/restart after midnight and finish: credit its original date.
5. Complete a puzzle and immediately navigate away. Cold launch: stars/progress persist once,
   the completed save is gone, and the next campaign level is available. Replay must not duplicate
   rewards. Navigate into and out of Options/How to repeatedly; returning resumes the correct game.
6. On Windows close/pop the game page and start another; the discarded page must not keep ticking,
   saving or changing the new game. Exercise repeated background/foreground transitions on iOS.

### D3: Touch input

On a known solution, test tap fill, drag across adjacent cells, reverse direction, lift outside
the board, cancel a gesture, and multi-touch interruption. Inspect every affected cell. Test both
mode-button crosses and hold-to-cross, then clear, undo, redo, and a new stroke after undo.
One Undo must reverse the entire drag, including its automatic crosses; one Redo restores it.
Repeat with an erase drag and a drag that starts on an incorrect fill. Undo keeps charged mistakes.
With a zoomed board inside its scrolling host, distinguish a scroll from a paint gesture: a
cancelled scroll must not leave unintended marks, stuck magnification, or stale drag ownership.
Repeat on tablet, left-handed layout and native touch hardware.

### D4: Accessibility

Enable the platform reader during a game and again before cold launch. Navigate sequentially
without relying on screen coordinates. Each cell announces row, column, state and clues in the
selected language. Fill, cross, clear and undo using accessible controls in both tap modes.
Open pause, break, time-up, win, parent-gate and reset dialogs: covered controls must leave
navigation; dialog actions must remain reachable. Dismiss and verify board/settings navigation
returns. Test focus order and restoration, not just exclusion attributes. Disable/re-enable the
reader and check that cell overlays/mode controls follow its state. Repeat with TalkBack,
VoiceOver, and Narrator. Listen to announcements; screenshots cannot pass that part.

### D5: Localisation and narration

Switch English to Arabic/Hebrew while Options is open, return to game, pause and open a parent
gate. Text and controls mirror appropriately; puzzle rows/columns retain their logical identity,
and the arithmetic question remains ordered correctly. Switch back without restarting. Repeat
Simplified and Traditional Chinese: native labels and narration use the requested script/voice.
Enable narration with a voice installed, then select a language with no installed matching voice:
show the unavailable-voice note and avoid speaking the wrong language. Restore the original choice.

### D6: Layout and text scaling

Exercise the smallest supported phone/window, a tablet in both orientations, a desktop window
below and above the wide-layout threshold, and font scales 1.0, 1.5 and 2.0. Check onboarding,
menu, Continue, Options and gameplay. Open every game dialog and both parent gates. Open the
numeric keyboard and enter a wrong answer: heading, retry message, entry, submit and cancel must
remain reachable by scrolling, with no text cut off. Test the reset confirmation, then cancel
without erasing progress. Check system bars/notches/gesture areas and keyboard dismissal.
Phones intentionally remain portrait; rotating a phone is not a tablet-layout test.

### D7: Live preferences

During play open Options, change light/dark/auto and accent, handedness, big numbers, zoom,
magnifier, helpers and tap mode. Check immediate selection styling and the returning board,
including clue/cell descriptions. With automatic crosses off, finish a line, then enable the helper
in Options. On return, its crosses must appear immediately on the board and minimap and be announced
in the cell descriptions.
In Auto, change OS light/dark while foreground and background;
explicit themes and colour-blind mode retain their intended policy. Check system-bar icon contrast.
Enable/disable OS reduced motion during play and in the background: mascot/confetti/cell/shake
animations follow the preference after returning, without changing game state. Verify persisted
preferences after cold launch and restore original test settings.

### D8: Release integration

Use the signed/trimmed Release package, not Debug or a prototype. Check first-run onboarding,
subsequent cold startup, Home/resume, SQLite save/continue/completion, and package upgrade with
existing progress. Listen to tap/reveal sounds and music; disable them, interrupt with backgrounding,
and resume. Verify narration availability and actual speech, and haptics on hardware. Inspect app
logs for native crashes, binding/resource failures, missing trimmed types and storage exceptions.
Repeat installation/startup on the other platform heads. Successful compilation alone does not
verify deployment, signing, native SQLite or audio.

Profiling P1–P4 is documented separately in the [profiling guide](../../tools/Profiling/README.md)
and [results](../../tools/Profiling/RESULTS.md). Do not infer performance on lower-end hardware
from emulator startup timing or unit-test duration.
