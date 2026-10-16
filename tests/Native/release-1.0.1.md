# Release 1.0.1 validation - 2026-10-16

Version: **1.0.1, build 7** (Windows package version 1.0.7.0). Source: working tree
based on `a750bc6ab4cca4b8b0b82f3890c508b0db126d42`, including the review fixes.
This record uses fresh tests; historical audit results are not counted as passes.

## Fixes and optimizations

- Wait for pending completion persistence before quitting or reading progress on return,
  including Android Back navigation. Completed saves disappear and rewards refresh together.
- Apply the selected culture even when English is already the localization service's default.
  Build the daily grid using the same calendar as its heading. Expired streaks display zero.
- Keep unavailable saved puzzles visible and deletable, with Resume disabled. Deletion also
  handles a list reload that replaces the card while its database write is in progress.
- Reuse timed-trial cards until the language changes. Schema 12 adds a partial index for
  pending completions, avoiding scans through accumulated completion receipts during recovery.
- The tested working tree also contains grouped drag undo/redo, immediate helper refresh,
  generator-version validation, Chinese speech-locale matching, accessibility cell refresh,
  and reduced repeated pack loading.

## Automated validation

| Check | Result |
| --- | --- |
| Core Release tests | Passed: 754 |
| Data Release tests | Passed: 146 |
| Presentation Release tests | Passed: 305 |
| Total | 1,205 passed; zero failed or skipped |
| Authored puzzle validation | Passed: 130 puzzles |
| Localization validation | Passed: 39 languages, 388 keys |
| Android Release build, warnings as errors | Passed: zero warnings/errors; AOT, trimming and R8 enabled |
| Windows Release build, win-x64, warnings as errors | Passed: zero warnings/errors |

Installed SDK 10.0.402 was invoked explicitly because the pinned SDK 10.0.401 is absent
on this machine. The repository's SDK/workload pin was preserved. Restore used the local
validation configuration with NuGet auditing disabled; this is not a fresh dependency-security audit.

## Android emulator execution

Device: `sqbuzz_phone`, `emulator-5554`, Google APIs x86_64, Android 16/API 36,
1080 x 2400 at 420 dpi, font scale 1.0. Fingerprint:
`google/sdk_gphone64_x86_64/emu64xa:16/BE2A.250530.026.F3/13894323:userdebug/dev-keys`.
Input was injected with ADB. Screenshots and UI hierarchy dumps were inspected.

| Scenario | Result and evidence |
| --- | --- |
| Release install, cold start, Home/resume | Passed using `tools/check-android.cs`; installed APK hash equals input hash |
| Existing data migration | Passed: schema 11 to 12, original progress and generated 20x20 save retained; pending index present |
| Resume existing save | Passed: existing filled cell, 1/180 progress, two remaining hints and zero mistakes retained |
| Expired streak | Passed: menu showed zero for the last-played date of October 9 on October 16 |
| Five-cell drag, Undo, Redo | Passed: whole row filled, one Undo cleared the row, one Redo restored all five cells |
| Background timer | Passed: 30 seconds in background did not accrue play time; elapsed changed from 0:28 to 0:29 including foreground input |
| Force-stop and resume | Passed: Heart save retained 5/16 filled cells, 0:29 elapsed, zero mistakes; original save also retained |
| Win then Android Back | Passed: Heart completed with three stars, 0:31, no hints/mistakes; completed save removed, menu stars increased from 3 to 6 and next level advanced from 2 to 3 |
| Persian language and calendar | Passed: live RTL layout and translated labels; heading and grid consistently showed October 2026 with 31 days and today 16 highlighted |
| Missing authored picture fixture | Passed: unavailable card visible, Resume disabled, Delete enabled; deleting it removed the warning and preserved the valid save |
| Final runtime logs | No fatal exception, unhandled exception, app ANR, SQLiteException or binding-diagnostics matches in the captured logs |
| Cleanup | Original settings, saves, progress, solved puzzles, trophies, daily history and completion receipts compared equal after restoration; schema 12 retained |

This Android runtime uses a Gregorian calendar for `fa`. Persian-calendar month lengths,
leap years, alignment, completion boundaries and view-model heading/grid agreement are
covered by automated tests on the desktop runtime.

The initially installed build 6 had a different signing certificate, so Android refused an
in-place update with `INSTALL_FAILED_UPDATE_INCOMPATIBLE`. Its APK and database were backed up,
the test app was reinstalled, and its original database was restored before migration checks.
Therefore this run verifies old-data migration, but does not prove an update signed with the
production key. A subsequent replacement installation with the matching test key passed.

Animation duration scale was temporarily set from 1 to 0 for stable UI dumps, then restored
to 1 before the final smoke test. English and the original game data/preferences were restored.

## Artifacts and release limits

Local evidence is ignored by Git:

- `artifacts/release-1.0.1/`: screenshots, UI hierarchies, database backups, restoration comparison
  and `logcat-final.txt`.
- `artifacts/native/20261016T110717791-smoke-f97a06907c4d4fd789398661a7e5e8bc/result.json`:
  successful final install/startup/resume report and matching installed hash.

Built packages are in `src/Squarebuzz.App/bin/Release/net10.0-android/`:

| Artifact | SHA-256 |
| --- | --- |
| `fun.asmie.squarebuzz-Signed.apk` | `B81101C0A90CCBCBE98C10A34B6BCF5E1FB33048904B688DCC5F9F9859383EF7` |
| `fun.asmie.squarebuzz-Signed.aab` | `E6813A698A3B099F547C9DBAE6A5F190C2F4E88EAE56D6D44882C417AF700A90` |

These are Release-configuration test packages signed with the local Android Debug certificate
(`CN=Android Debug, O=Android, C=US`), not store upload artifacts. Build/sign the upload with
the established release key before submission. Nothing was published or committed.

Not run: physical phone/tablet touch, sound/haptics, TalkBack/VoiceOver/Narrator usability,
older Android, Apple builds/runtime, installed Windows runtime, and the full native acceptance
matrix. The emulator checks and successful builds do not establish those results.
