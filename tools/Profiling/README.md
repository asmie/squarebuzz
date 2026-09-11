# Profiling P1–P4

[RESULTS.md](RESULTS.md) records host measurements, physical Android phone captures, the
measured font-resolution fix and remaining coverage limits. Lower-end phone and tablet hardware
were unavailable. The host tool changes no game data and does not connect to a device.

## Host baseline

Run from the repository root:

```powershell
dotnet run --project tools/Profiling -c Release
```

Use `-- --output DIRECTORY` to change the default ignored `artifacts/profiling` destination.
The tool writes a unique JSON report with nine samples per case, batch size, median elapsed time,
current-thread managed allocation, GC counts, runtime/OS details and assembly hashes. It exits
nonzero on a failed fixture or workload. Debug runs are rejected.

The harness links production App control/description/localization code and references Core and
Presentation. MAUI controls have **no native handlers**. English resources and Light/Tangerine
colors are loaded; test fakes supply storage, navigation, audio and timers. They make no device
calls. Continue work must finish synchronously or the harness rejects its current-thread
allocation measurement. No application database is opened.

Tiered compilation is disabled only in this console tool to stabilize optimized CoreCLR code.
Each case calibrates a batch to approximately 10 ms (capped at 1,048,576 operations), warms five
batches, and records nine more without forcing collections. Generation uses complete rotating
corpora of seeds 0–19 for every measured batch. Run sequentially in separate processes to assess
repeatability; avoid simultaneous builds/tests. Results are **batch averages**, not per-request
tail latencies, cold-start measurements or mobile frame times. This is a small diagnostic runner,
not a statistical performance regression gate.

| Investigation | Host coverage | Requires physical device |
| --- | --- | --- |
| P1 | Production board/minimap/magnifier refreshes at 5, 15 and 25 cells per side; snapshot allocations. | Real draws, animations, GC impact, scrolling and frame deadlines with minimap/magnifier visible. |
| P2 | 625 `DescribeCell` calls with real strings; page-equivalent full loop with unchanged managed semantic properties. | Creating/layout of the accessible board, changed native descriptions, TalkBack event/focus cost, repeated moves. |
| P3 | Full row/column strike calculations for empty/mixed marks; palette resolution from merged dictionaries. | Draw-string allocation, live strike animation, Android motion-setting reads, native drawing cost. |
| P4 | Unique generation at 15/20/25, difficulties 1/3/5; Continue rebuilds with 1/10/12 saves and an out-of-range 50-save stress case. | UI-thread stalls, storage and list layout for up to 12 saves, actual navigation and cancellation during load. |

The host refresh calls do not call `Draw`; the generic MotionPreferences fallback is never
profiled as if it were an Android query. The semantic-property loop measures unchanged values
after warmup, so it cannot establish the cost of native accessibility events on a move.

## Physical Android session

Connect the phone with USB debugging enabled, unlock it, accept the computer's debugging key,
and identify its serial with `adb devices -l`. Always select that serial explicitly; the existing
emulator may still be connected. Use test progress and record any pre-existing installed app.
Do not clear data or uninstall to prepare a measurement.

Record device model/SoC, Android version, APK hash/source revision, display refresh rate,
resolution/density, font scale, language/theme, animation settings, TalkBack version, power mode
and thermal conditions. Use the same conditions before/after a change and repeat on a tablet.

1. **P1:** warm a 20×20 generated game on a phone (25×25 requires a tablet), then repeatedly
   paint/undo a fixed group of valid cells.
   Capture separate runs with magnifier off/on and minimap visible; distinguish board changes
   from scrolling. Compare allocations and CPU stacks for the three refresh and draw methods.
2. **P2:** repeat with TalkBack active. Record initial accessible-board construction separately
   from 30 individual moves. Inspect `DescribeCell`, `RefreshCellDescriptions`, native semantic
   property updates and focus behavior. Do not trade away correct announcements for speed.
   Avoid the shell `uiautomator dump` during this measurement: ordinary UiAutomation sessions
   [suppress accessibility services by default](https://developer.android.com/reference/android/app/UiAutomation#FLAG_DONT_SUPPRESS_ACCESSIBILITY_SERVICES).
   Use a harness that explicitly preserves services, or screenshots and gfxinfo for rendering.
   Shell-injected taps exercise native buttons but do not establish real touch-exploration or
   spoken-focus behavior; those require a separate interaction check.
3. **P3:** complete a line to trigger clue strikes, including animation frames after a move.
   Inspect clue/number formatting, `BoardPalette.FromResources` and Android
   `MotionPreferences.GetReduceMotion`. Repeat after theme and OS animation changes if caching
   is proposed; those changes define the invalidation requirements.
4. **P4:** capture new generated games across sizes/difficulties and Continue loads with a
   recorded save corpus. Separate time in generation, SQLite and native list creation. A long
   frame on the UI thread is the evidence for considering cancellable background reconstruction.

Start with the normal Release APK. Android's
[dumpsys guidance](https://developer.android.com/tools/dumpsys) documents `gfxinfo` frame data:

```powershell
& $adbPath -s $serial shell dumpsys gfxinfo fun.asmie.squarebuzz reset
# Perform one recorded scenario, then capture immediately.
& $adbPath -s $serial shell dumpsys gfxinfo fun.asmie.squarebuzz framestats
```

Retain raw output and frame deadlines; exclude non-rendered/flagged rows when calculating
durations. Frame history is bounded, so a capture is not automatically the entire session.
Do not treat an `am start -W` launch time as time until the game is interactive.

For CPU/GC attribution, follow the official
[MAUI profiling guide](https://learn.microsoft.com/en-us/dotnet/maui/fundamentals/profiling?view=net-maui-10.0).
It requires current `dotnet-trace`/`dotnet-dsrouter` tools and a diagnostics-enabled Release build.
The local session uses tool version `10.0.745401`. Select the device before configuring the
router. For this repository the build properties are:

```powershell
dotnet build src/Squarebuzz.App -c Release -p:SquarebuzzTargetFramework=net10.0-android -p:DiagnosticAddress=127.0.0.1 -p:DiagnosticPort=9000 -p:DiagnosticSuspend=false -p:DiagnosticListenMode=connect -warnaserror
```

Install that APK explicitly on the chosen serial and record its hash separately. A diagnostics
build is for measurement, not distribution; collect an uninstrumented frame baseline too.
Verify router/tool options against the installed versions before starting capture. Restore
changed device preferences and remove only the forwarding rules created for this session.

When the diagnostic tool can launch but its ADB child cannot, the
[router's server/server mode](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/dotnet-dsrouter)
allows the USB forwarding step to be performed separately. First preserve the installed normal
APK and verify its hash, install the diagnostic APK without clearing data, and create the
device-specific rule with `adb -s SERIAL reverse --no-rebind tcp:9000 tcp:9000`. Check existing
rules before proceeding if that reports a conflict. Then run the router on the host:

```powershell
artifacts/profiling/tools/dotnet-dsrouter.exe server-server --ipc-server squarebuzz-phone --tcp-server 127.0.0.1:9000 --runtime-timeout 120
```

Keep that process running while the diagnostic app is open. In another terminal, capture one
bounded scenario (the duration starts when the runtime connects):

```powershell
artifacts/profiling/tools/dotnet-trace.exe collect --diagnostic-port squarebuzz-phone,connect --profile dotnet-sampled-thread-time,gc-verbose --duration 00:00:00:30 --format Speedscope --output artifacts/profiling/phone-scenario.nettrace
```

Use a new output filename for each run. Verify that the trace contains the expected managed
stacks and GC/allocation events; selecting a profile alone does not establish runtime support
or successful data collection. After the session, stop the router, remove only the created
`tcp:9000` reverse rule, and restore the preserved normal APK without clearing application data.

In this .NET 10 Mono session, `gc-verbose` did not produce allocation events. Native allocation
capture additionally required an `AndroidEnvironment` file containing
`MONO_DIAGNOSTICS=--diagnostic-mono-profiler=alloc` and the trace provider
`--providers Microsoft-DotNETRuntimeMonoProfiler:0x200001:5`. This enables allocation and GC
phase events without requesting a forced heap dump. Keep this environment file/import local
to diagnostic builds; it adds instrumentation overhead. Use a full build/rebuild, not a direct
`Compile` target, when packaging MAUI XAML. Event IDs and payloads follow the official
[Mono event manifest](https://github.com/dotnet/runtime/blob/v10.0.0/src/coreclr/vm/ClrEtwAll.man).
Separate idle, navigation, and repeated-move traces, record their complete durations, and do
not divide a whole-process allocation total by move count as if background work were absent.

## Decisions

Keep the existing implementation when its measured cost is acceptable on target hardware.
Consider shared snapshots for P1, affected-cell updates for P2, explicit invalidation caches for
P3, or cancellable background generation for P4 only when traces justify the complexity. Any
change must retain session identity, original daily date, deterministic seeds, correct clue
strikes, live preferences and accessibility focus. Record before/after results on the same
hardware and workload before preparing the user's single profiling commit.
