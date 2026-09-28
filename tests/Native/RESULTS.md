# Native validation record — updated 2026-09-12

Historical evidence for the revisions listed below. Re-run the relevant cases for the September 28
artwork and dependency changes; see [audit status](../../docs/audit.md).

**Status: partial execution; full platform acceptance remains open.** The eight packages are
defined in [README.md](README.md). The native fixes are committed in `1318c92`, and the separate
profiling batch is committed in `9ec3d9e`. The latest verified phone behavior is summarized below;
the later sections preserve the original emulator findings and retest history.

## Latest verified state

- The final normal Android Release APK includes the canvas-font correction from the profiling
  batch. SHA-256: `7E91AFD5463E81A397AB76162B6C9ABA20D31D4C0B4FE2BE76145AA816485A73`.
  Full Android and Windows Release builds passed with zero warnings/errors. Android runtime
  configuration was checked to exclude diagnostic ports and allocation instrumentation.
- Physical Samsung SM_S948B, Android 16/API 36: installation, matching input/installed hashes,
  cold startup and Home/resume passed in
  `artifacts/native/20260911T212131257-smoke-303a67beb7394bfdbe23b2cb56088596`.
  Twelve generated saves survived replacement installation; the original controlled puzzle
  resumed with its recorded 13/196 progress and zero mistakes.
- A physical TalkBack check passed on that APK: empty/filled cells announced their
  row, column, clues and state; double-tapping changed a selected filled cell; moving focus away
  and back announced the updated state, with focus remaining usable. This verifies that cell
  interaction scenario, not every modal, cross/clear/undo flow, or VoiceOver/Narrator behavior.
- Original accessibility and USB keep-awake settings were restored and verified. The normal
  Release APK and test saves remain on the phone. Detailed frame/allocation results and cleanup
  evidence are in [the profiling record](../../tools/Profiling/RESULTS.md).
- GitHub CLI was rechecked on 2026-09-12. The latest run remains
  [34524486106](https://github.com/asmie/squarebuzz/actions/runs/34524486106), for
  `4ed40c82e7291d2e9a0c123f6ec313a371fdd769`; no run for current revision
  `9ec3d9e9b65d616d04d8bf4d4f497528f5a6ab19` was returned. Current iOS/Mac Catalyst CI evidence
  remains missing. A successful local Windows build is not installed Windows acceptance.
- No lower-end Android phone or tablet was available for this run. Native 25×25,
  tablet landscape, older Android, installed Apple/Windows behavior, and the outstanding
  lifecycle, gesture, modal, language/voice and physical audio/haptic cases remain unverified.
  Later cell checks narrow D4's original gap; they do not pass the full eight-package matrix.

## Original emulator build and environment — 2026-09-11

- Installed baseline: source `02dcef9` (`refactor: separate session coordination and persistence recovery`).
- Release APK: `fun.asmie.squarebuzz-Signed.apk`, SHA-256
  `642F61E4C64A790DEF510B94A7C72E185C4F204DEA0DAF4389E8C83390872F7F`.
- Updated working-tree APK: user completed the full Release build with warnings as errors
  successfully in 88.4 seconds. Local artifact SHA-256:
  `4F61791361E53C8728735C76A1EF3E6A014887C54B967FE3390C3A8594764358`.
  Installation, hash verification, cold startup and Home/resume passed in
  `artifacts/native/20260911T081342419-smoke-054b2126038a44e5b47b4ff250b76c76`.
  The second run, `20260911T082111542-smoke-ab5e145940cc4161a9a7c408b4f7eba3`, also passed
  but installed the same APK. Neither run includes the subsequent root-layout/text-measurement
  corrections described below.
- Root-layout/text-measurement APK: source and installed SHA-256 matched
  `0605390B9F69C90C4522C1116D7775781498FDE6A4BB3A64F5103B38023AC5DD`.
  Installation, cold startup and Home/resume passed in
  `artifacts/native/20260911T082518292-smoke-acdcb2e4224b4cf2a9878def4dd4bdf1`.
  This APK predates the Android keyboard resize correction below.
- Keyboard resize APK: source and installed SHA-256 matched
  `9E1E8EC266B9371498485AC2DA83EAA3F7510141BE7F6A30DA2A7ACB0F47DD54`.
  Installation, cold startup and Home/resume passed in
  `artifacts/native/20260911T085535225-smoke-f569e3a116944e1ba3dd2b71266eae01`.
- Package: `fun.asmie.squarebuzz`, launcher `crc64315effd481912370.MainActivity`.
- Android emulator: `sqbuzz_phone`, `emulator-5554`, Google APIs x86_64, Android 16/API 36,
  1080×2400, 420 dpi. This is an emulator, not physical phone/tablet coverage.
- Initial language English, light theme, font scale 1.0. Cases also used Arabic, dark theme,
  font scales 1.5/2.0, and a temporary 1920×1080 display override. The phone activity remained
  portrait as designed; the override did not establish tablet landscape coverage.
- TalkBack was enabled during a paused game. Full spoken navigation/cell operation acceptance
  was not completed. Audio output and haptics were not evaluated by listening/physical interaction.
- Raw screenshots, UI hierarchies and build/test logs are local, ignored files in
  `artifacts/review/`; they are not bundled with a clean checkout of this report.
- Test cleanup restored font scale 1.0, animator duration scale 1, original rotation settings,
  display size/density, and disabled TalkBack/touch exploration. App choices returned to English
  and Light. The dedicated emulator retains the test progress for the next verification run.
  The last retest also saved an unfilled Level 2 (Heart) session after using Quit to menu.

## Original emulator cases and limits

| Package | Observed result on the baseline | Remaining at that stage; see latest state above |
| --- | --- | --- |
| D1 | User supplied successful run [34524486106](https://github.com/asmie/squarebuzz/actions/runs/34524486106), SHA `4ed40c82e7291d2e9a0c123f6ec313a371fdd769`. Local history shows that revision predates the refactor and its workflow has iOS only. | Current-commit iOS and Mac Catalyst job/step results. The overall older run conclusion does not establish either current build. |
| D2 | Save/quit/cold launch/Continue retained Level 1, 6/18 filled and 10:42. Resumed board retained a cross; elapsed time resumed from the saved value. Pause held elapsed time stable. Completion showed 18/18, zero mistakes, three stars; after cold launch menu showed three stars, Level 2, and no saved game. | Daily midnight attribution on installed app, restart cases, immediate completion/navigation races, repeated lifecycle transitions, iOS and Windows page lifetime. |
| D3 | Tap plus horizontal drag produced 6/18 fills with zero mistakes. Undo reduced to 5/18; redo restored the saved count. Mode-button cross survived save/continue. A sequence of taps/drags completed the known Level 1 solution. | Hold-to-cross, scrolling cancellation, off-board release, interruption/multi-touch, tablet and physical touch coverage. |
| D4 | Paused hierarchy retained covered gameplay controls; actual TalkBack focus highlighted the background Level 1 heading. Parent-gate hierarchy also retained covered settings. | Complete modal exclusion/focus checks beyond the partial retests below, spoken cell descriptions, fill/cross/clear, focus order/restoration, VoiceOver and Narrator cases. |
| D5 | English → Arabic applied without process restart. Menu and game HUD/control order mirrored; the puzzle grid retained its row/column orientation. Arabic level descriptions and saved completion state were present; switching back to English restored the paused game labels. | Full RTL interaction/dialog acceptance, both Chinese scripts/voices, installed/missing TTS voice behavior. |
| D6 | Win result displayed on baseline phone at default and 1.5 font scales. At 2.0, opening the parent-gate numeric keyboard covered the bottom of Cancel. Initial menu artwork intruded into the status-bar area. | Remaining dialogs at small sizes/large fonts beyond the gate/pause retests below, actual tablet landscape and Windows window resizing. |
| D7 | Dark selection immediately recoloured Options and persisted when returning to menu/game. Its status-bar icons initially stayed dark until a later configuration change. Setting animator duration scale to zero stopped persistent menu animation sufficiently for idle hierarchy capture. | OS Auto transitions, handedness/zoom/helpers on return, complete reduced-motion transitions and persistence. Idle hierarchy capture is not full animation acceptance. |
| D8 | Signed Release APK installed; explicit launcher cold-started; Home/return and SQLite-backed save/completion state survived process termination. No startup crash was observed. | Physical audio/haptics and interruptions, narration, upgrade cases, installed Apple/Windows packages. |

The TalkBack activation sequence included raw coordinate input, so it is not evidence that
accessible cell activation works correctly. The saved count was checked before that sequence;
subsequent coordinate input changed a cell before the puzzle was completed.

## Changes prompted by native findings

These changes are in the native validation batch; installed retest results follow:

1. Apply `SafeAreaEdges=All` to ContentPage and root layouts, and configure Android's
   `WindowSoftInputModeAdjust=Resize` so keyboard appearance can reduce the dialog viewport.
2. Exclude gameplay/settings covered by modal dialogs from accessibility navigation. Apply the
   same parent-gate behavior to About. Add regression coverage for game overlays and the
   gate → reset confirmation → cancellation transitions.
3. Make game/parent/reset cards scroll within the available viewport, constrain their width,
   and use minimum button heights so larger fonts can expand the controls.
4. Update Android status/navigation bar icon contrast when the resolved app theme changes,
   and restore it on activity resume. Unsubscribe on activity destruction.

## Retest of APK `4F617913…`

- Install/startup/Home-resume smoke: passed; the runner verified identical source and installed
  SHA-256 values. Process log review found an emulator graphics-format warning and English
  satellite-resource fallback messages, with no startup crash or binding error in that capture.
- Parent-gate accessibility: after enabling TalkBack and dismissing its permission dialog,
  forward navigation focused the gate heading rather than the covered settings. The uncompressed
  UIAutomator hierarchy still includes background nodes; its presence alone does not establish
  reader focusability. Full spoken navigation/activation remains open.
- Dark theme: the gate showed light status icons against the dark background without recreating
  the activity. Full Auto/OS transitions remain open.
- **Safe-area retest failed:** cold menu content still overlapped the status bar, and the
  parent-gate keyboard still obscured Cancel at font scale 2.0.
- **Text-measurement retest failed:** the gate heading clipped its second line at font scale 2.0.
- Corrections now apply `SafeAreaEdges=All` to the owning root layouts, including a new Grid
  around the menu ScrollView. Dialog content uses Grid rows for definite-width text measurement;
  outer spacing belongs to the scrolling viewport rather than reducing the card's width.
  These corrections were installed in APK `0605390B…`, with results below.
  The interrupted game-focus check was discarded because a user smoke
  run restarted the app while it was being exercised.

Local retest evidence includes `native-updated-menu-png.png`,
`native-updated-gate-nextfocus-png.png`, `native-updated-gate-large-png.png`, and
`native-updated-gate-scrolled-png.png`.

## Retest of APK `0605390B…`

- Install/hash/cold-start/Home-resume smoke: passed.
- Cold-start menu: content now clears the status bar.
- Live theme changes: Light selects dark status icons; Dark selects light icons without
  recreating the activity.
- Parent gate at font scale 2.0: the complete heading is visible before opening the keyboard.
- **Keyboard retest still failed:** Cancel remained partly covered, and swiping did not make
  it fully reachable. Native window diagnostics reported `sim={adjust=pan}`. The app now
  explicitly requests Android `Resize`, following the
  [MAUI keyboard input-mode guidance](https://learn.microsoft.com/en-us/dotnet/maui/android/platform-specifics/soft-keyboard-input-mode?view=net-maui-10.0).
  This additional correction compiles with warnings as errors; its installed retest is below.

Local evidence: `native-final-menu-png.png`, `native-final-light-png.png`,
`native-final-dark-png.png`, `native-final-gate-large-png.png`, and
`native-final-gate-keyboard-png.png`.

## Retest of APK `9E1E8EC2…`

- Install/hash/cold-start/Home-resume smoke: passed. Native window diagnostics now report
  `sim={adjust=resize forwardNavigation}`.
- Parent gate, dark theme, font scale 2.0: the numeric keyboard reduces the available viewport.
  The full heading, answer field, OK and Cancel remain visible above it. Tapping Cancel closes
  the gate and returns to settings. This resolves the reproduced keyboard obstruction.
- Paused game with TalkBack bound and touch exploration enabled: the visible accessibility
  focus rectangle surrounds the Paused heading. Full spoken navigation, activation and focus
  restoration remain unverified. The first gesture during reader startup was discarded because
  the service had not yet been confirmed bound and the unfilled board restarted. It does not establish reader navigation.
- Pause dialog, font scale 2.0: heading and all actions fit at 1080×2400. With a temporary
  1080×1400 viewport, scrolling reveals the complete Quit to menu button; tapping it returns
  to the menu. This verifies overflow scrolling on the emulator, not tablet landscape coverage.
- Restored physical display dimensions, font scale 1.0, animator duration scale 1, Light/English,
  and disabled TalkBack/touch exploration. Original rotation settings remain unchanged.

Local evidence: `native-resize-keyboard-png.png`, `native-resize-cancel-xml.xml`,
`native-reader-state-png.png`, `native-pause-large-png.png`, `native-pause-short-png.png`,
and `native-pause-short-scrolled-png.png`.

The safe-area setting follows the [MAUI ContentPage safe-area documentation](https://learn.microsoft.com/en-us/dotnet/maui/ios/platform-specifics/page-safe-area-layout?view=net-maui-10.0).
System-bar icon updates use [AndroidX WindowInsetsControllerCompat](https://developer.android.com/reference/androidx/core/view/WindowInsetsControllerCompat).
Scroll containers fill their available space, following the [MAUI ScrollView guidance](https://learn.microsoft.com/en-us/dotnet/maui/user-interface/controls/scrollview?view=net-maui-10.0).

Useful local evidence: `native-menu.png`, `native-talkback-ready-png.png`,
`native-gate-xml.xml`, `native-gate-large-keyboard-png.png`, `native-options-dark-png.png`,
`native-complete-png.png`, `native-postwin-menu-xml.xml`, `native-saves-xml.xml`,
and `native-arabic-board-png.png`.

## Original automated verification and execution blockers

These blockers describe the earlier emulator session. Later full builds, phone smoke and ADB
access succeeded as recorded above; current Apple CI evidence is still missing.

- Release unit tests: Core **521**, Data **128**, Presentation **227** — **876 passed**, none skipped.
- Updated Android C#/XAML compilation with `-t:Compile`, explicit `TargetFramework=net10.0-android`
  and warnings as errors: **passed, zero warnings/errors**. This is compilation, not APK packaging.
- `tools/check-android.cs`: compiled, help passed, and missing `--adb` was rejected before device
  access. End-to-end install/hash/startup/resume checks passed in the four runs above.
- Full updated Release build/package: **passed locally by the user**, using
  `dotnet build src/Squarebuzz.App -c Release -p:SquarebuzzTargetFramework=net10.0-android -warnaserror --no-restore`.
  This build output covered APK `4F617913…`; subsequent layout APK `0605390B…` and keyboard
  APK `9E1E8EC2…` were installed with matching hashes and their changed behavior verified.
  Separate full-build console output was not supplied for those later APKs.
  Packaging from this session hit the
  NuGet-cache access restriction, and the elevated runner still fails with Windows **Access denied**
  before process creation. The user completed the installer/smoke checks locally.
- The existing approved interactive device shell allowed baseline checks to continue. New host
  ADB/GitHub CLI processes could not be run with the required access in this session.
- No current Apple CI evidence or installed iOS/Mac Catalyst/Windows runtime evidence was available.

P1–P4 profiling subsequently completed on the available host and physical phone, with the
coverage limits recorded in [the profiling results](../../tools/Profiling/RESULTS.md).
Emulator timings are not a representative hardware baseline.
