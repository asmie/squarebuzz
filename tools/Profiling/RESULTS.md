# Profiling results — 2026-09-11

The four investigations have host measurements and physical Android phone evidence. The
production change resolves the registered clue font before drawing: Android no longer throws
and catches an asset-loading exception for the `BodyBold` alias. Other proposed optimizations
remain deferred for the reasons below. This is not a claim that every device meets a frame-rate
budget. Lower-end Android and tablet hardware are unavailable, as confirmed by the user;
native 25×25 coverage and Apple rendering of the font change remain unverified.

The user passed the physical TalkBack check on the final normal Release APK: empty and filled
cell descriptions matched their row, column, clues and state; double-tapping changed the selected
cell; moving focus away and back announced the updated state, with focus remaining usable.
This completes the available phone checks for this batch, subject to the hardware limits above.

After the check, the original Wispr-only accessibility service list was restored and verified;
accessibility remains enabled, touch exploration is back to 0, and USB keep-awake is back to 0.
The screen timeout remains 30,000 ms. The profiling router was stopped and the phone's reverse
rule list is empty. The normal Release APK and the 12-save test corpus remain installed.

## Decisions

| Investigation | Finding | Decision |
| --- | --- | --- |
| P1: shared board/minimap/magnifier snapshots | One refresh of all three at 25×25 allocates 2,072 bytes on the host. Native board snapshots total 17,280 bytes across a repeated-move capture, versus megabytes under clue drawing. | Keep the simple snapshots; a shared cache is not justified by these measurements. |
| P2: accessible board and descriptions | Native 20×20 overlay construction allocates about 17 MB under its application frame. Repeated full descriptions are a larger cost than snapshots. The user's physical TalkBack check passed. | Retain behavior for this batch; the data identifies a future optimization candidate, but does not establish a safe affected-cell update design. |
| P3: drawing, clue strikes, preferences | Passing a MAUI alias to a graphics canvas causes repeated caught exceptions. Native text-layout allocation dominates drawing; host clue calculations allocate zero bytes. | Resolve the registered platform font once per handler. Do not add palette/motion caches or change string overloads without evidence and invalidation tests. |
| P4: generation and Continue | Production retains at most 12 saves. Native Continue shows occasional long opening frames, with reconstruction and native card creation both contributing. | Keep the bounded implementation. A 50-save fake-repository stress result does not justify a production concurrency refactor. Reassess on slower hardware when available. |

## Environment and reproducibility

Production base: `1318c92` (`fix: address issues found during native validation`) plus the
working-tree font change. Host: Windows x64, .NET 10.0.11/CoreCLR, tiered compilation disabled
only in the profiling console tool. Exact runtime/OS/assembly hashes are in each JSON report.
The harness uses production managed code without native handlers and fake storage; its
elapsed times are not Android timings. See [README.md](README.md) for commands and limitations.

Phone: Samsung SM_S948B, SM8850/ARM64, Android 16/API 36, 1080×2340, density 450, adaptive
refresh, font scale 1.0, animator scale 1, power saving off, thermal status 0 at the recorded
checks. Language/theme: Polish, Light/Tangerine. This is a high-end phone. The app exposes
20×20 on phones; 25×25 requires a tablet. Existing Wispr Flow accessibility was preserved.
Samsung TalkBack version: 16.2.00.11. Scheduled OS sleep/DND mode activated during the evening;
that user preference was not changed. USB keep-awake was temporarily set from 0 to 2 with
permission; the 30-second screen timeout itself was not changed.

Raw JSON, traces, frame dumps, APKs and screenshots are ignored local evidence under
`artifacts/profiling` and `artifacts/native`. They are not part of this commit. Profiling tools
were `dotnet-trace` and `dotnet-dsrouter` 10.0.745401. A clean checkout can regenerate the host
reports and follow the recorded native workload protocol; it does not contain the user's
phone fixture or the session-specific trace-analysis helper.

## Host results

The initial two 30-workload runs were:

- `host-20260911T091603106-8d780d3898ba466e8eb97dbee2d5cb51.json`
- `host-20260911T091627389-349a938d01c344018c11fce083b9e12f.json`

The final harness adds the production 12-save cap. Two sequential 31-workload runs passed:

- `host-20260911T202652043-bdba52e8b79445f5a913961dff10aa48.json`
- `host-20260911T202802165-10a19c8225e94828b0615572dcd58ea8.json`

Values are rounded ranges of the initial runs' batch medians, except the 12-save row, which
comes from the final runs. Each report contains its own full measurements and assembly hashes.

| Workload | Host median elapsed time per operation | Allocated bytes per operation |
| --- | --- | ---: |
| Board snapshot refresh, 25×25 | below 0.001 ms | 656 |
| Minimap snapshot refresh, 25×25 | below 0.001 ms | 656 |
| Magnifier refresh with palette, 25×25 | below 0.001 ms | 760 |
| All 625 English cell descriptions | 0.212–0.215 ms | 241,688 |
| Descriptions plus unchanged managed semantic setters | 0.277–0.295 ms | 241,688 |
| Full empty/mixed clue pass, 25×25 | 0.001–0.002 ms | 0 |
| Palette lookup | below 0.001 ms | 104 |
| Generation, 25×25 difficulty 3, seeds 0–19 | 0.765–0.816 ms | 600,554 |
| Continue, 1 generated 25×25 save | 0.293–0.331 ms | 313,240 |
| Continue, 10 generated 25×25 saves | 7.18–7.21 ms | 5,478,024 |
| Continue, 12 generated 25×25 saves | 11.31–11.91 ms | 8,032,384 |
| Continue, 50 saves: outside production retention limit | 31.6–32.9 ms | 23,567,312 |

The native drawings, semantic events and Android motion-setting queries are excluded from
these host loops. The unchanged semantic setters are not a model of native changes after a
move. Production `SqliteSaveGameRepository.MaxSavedGames` is 12; the 50-save case explicitly
bypasses this in a fake repository. It is a stress test, not normal application behavior.

## Font fix and matched evidence

The dependency's [Android graphics font resolver](https://github.com/dotnet/maui/blob/bf6156897c887d33dcd40592db3bcb6471916e03/src/Graphics/src/Graphics/Platforms/Android/FontExtensions.cs)
does not resolve MAUI aliases. It tries to load `BodyBold` as an asset, catches the exception
and falls back. Initial traces recorded 71 and 469 exceptions. `BoardView` now resolves the
font when its handler connects and supplies it to `BoardDrawable`, with explicit bold weight
and a default-bold fallback. Android/Apple use the registrar; Windows uses the font manager's
family source, including the family fragment needed by Windows.

A matched 20×20 fixture requested 20 Undo/fill pairs, with 200 ms after each action. Both runs
started and ended with the known cell filled. The after-run initialized Undo history before
the frame reset, within its 30-second diagnostic trace. Screenshots verified bold clues and
zero mistakes after the change.

| Diagnostic capture | Font exceptions | Frames / deadline misses | Histogram p50 / p95 / p99 |
| --- | ---: | --- | --- |
| `phone-font-before20` | 208 | 954 / 48 (5.03%) | 5 / 19 / 28 ms |
| `phone-font-after20` | 0 | 904 / 51 (5.64%) | 5 / 11 / 20 ms |

This proves the exception/font correction, not a general frame-rate improvement. The miss
percentages do not improve consistently. Sampled inclusive stack durations include callees
and possible waiting; they are not isolated CPU times. The actual MAUI Android `DrawString`
implementation creates a text layout per call; another overload also creates a layout, so
switching overloads alone would not remove that cost.

## Controlled native allocation measurements

`gc-verbose` alone yielded no GC/allocation events on this Mono runtime; that does not mean
zero allocation. Captures below additionally use startup
`MONO_DIAGNOSTICS=--diagnostic-mono-profiler=alloc` and provider
`Microsoft-DotNETRuntimeMonoProfiler:0x200001:5`, enabling allocation and GC phases without a
forced heap dump. The [runtime source](https://github.com/dotnet/runtime/blob/v10.0.0/src/mono/mono/eventpipe/ep-rt-mono-profiler-provider.c)
and [event manifest](https://github.com/dotnet/runtime/blob/v10.0.0/src/coreclr/vm/ClrEtwAll.man)
define the startup requirement and event payloads.

APK: `diagnostic-alloc-rebuilt-21C8F55F.apk`, SHA-256
`21C8F55FE06C4C43F9C4A600C995537CD051099545B03AC048D667584BB5D27F`.
Each trace is 30 seconds unless noted. Whole-process totals include idle/framework work.
These are Mono managed allocation events, not retained memory or native Java/GPU heap totals.
Allocation-stack attribution selects the nearest actual method starting with `Squarebuzz.`;
framework generic arguments containing the namespace are excluded. These rows partition
allocation rather than duplicating inclusive stack totals. The local analyzer decodes Mono
allocation ID 39 and GC phase ID 38 against the manifest and rejects unexpected payload sizes.

After the user confirmed the phone would remain untouched, a fresh generated 20×20 difficulty-3
puzzle was verified. Its first row has clue 14 and column 10 at (658,724) is a valid fill.
Move workloads request 20 Undo/fill pairs, 200 ms after each input; injected events may be
coalesced. Default zoom is 100%, magnifier off. The magnifier was visibly verified during held
contact. Minimap uses 110% zoom, magnifier off. The strike case starts with row 1 columns 4–16
filled and completes column 17 at (993,724), then Undoes, ten pairs with 500 ms after each
input. Screenshots verify the struck clue, automatic crosses, toast and zero mistakes.

| Capture (`phone-control-`) | Allocated bytes | Allocation events | Relevant nearest application frames |
| --- | ---: | ---: | --- |
| `idle20` | 2,441,600 | 43,945 | No input; timer/animations running. |
| `moves20` | 16,677,784 | 286,700 | Clue drawing 9,281,432 bytes; board snapshots 17,280. |
| `talkback-build20` | 21,596,624 | 315,320 | Accessible-board construction 17,066,584 bytes. |
| `talkback-moves20` | 22,714,792 | 441,146 | Line-clue description 3,021,760; localization formatting 2,796,448; DescribeCell 1,824,000; clue enumeration 972,800 bytes. |
| `magnifier20-valid` | 14,789,416 | 248,537 | ShowCell 17,280; magnifier draw 1,408 bytes. |
| `minimap20` | 16,149,176 | 234,126 | UpdateCells 17,280; minimap draw 8,320 bytes. |
| `strikes20` | 9,098,224 | 164,406 | Clue drawing 4,471,256 bytes. |

Per-allocation stack instrumentation materially changes performance. Move frame p99 buckets
of 65–93 ms in these captures must not be treated as normal-release regressions. GC intervals
are pre-stop-world to post-start-world envelopes, including stop/resume overhead, not precise
isolated stop-the-world pause times. Shell-injected native button actions do not establish
spoken announcements or actual touch exploration. UiAutomator dumps were avoided during
valid accessibility captures because their default session suppresses accessibility services.

### Generation and realistic Continue corpus

Twelve generated 20×20 difficulty-3 saves were created through normal UI use, with no database
injection or retention bypass. The first has 13/196 progress; later saves have one test input.
Each Continue trace contains three open/back cycles with two seconds on the page per open.
Screenshots verify menu counts and visible cards. These are navigation/rendering workloads,
not isolated `ReloadAsync` timings.

| Saves | Whole-process allocated bytes | Allocation events | Nearest line-solver frame | Nearest card-template frame |
| --- | ---: | ---: | ---: | ---: |
| 1 | 4,698,088 | 77,505 | 466,848 | 489,824 |
| 10 | 16,954,920 | 254,493 | 4,623,744 | 2,926,672 |
| 12 | 18,277,504 | 271,473 | 5,658,432 | 2,994,272 |

The card-template cost changes little from 10 to 12 saves while reconstruction continues to
grow, consistent with visible-card virtualization. The heavily instrumented 12-save trace has
about 1,003 ms inclusive sampled `ReloadAsync` duration across three opens. It does not
establish normal UI latency. Five Mono GC envelopes span 38.75–50.24 ms.

`phone-control-generation20.nettrace` records three generated 20×20 difficulty-3 games in
40 seconds, including options, navigation, drawing and saving. Total allocation: 26,071,032
bytes; inclusive sampled generator duration: about 86.7 ms across all three games. UI
construction is more prominent in this instrumented workload. Host coverage spans additional
sizes/difficulties; these three native games do not establish mobile generation tail latency.

## Final normal Release verification

Normal Android Release full Rebuild passed with zero warnings/errors in 2:48.75. The generated
runtime environment contains only `mono.enable_assembly_preload=0`, with no diagnostic ports
or allocation instrumentation. Full Windows Release build also passed with zero warnings/errors
in 40.48 seconds. Host profiling builds/runs, help and invalid-argument behavior passed.
The initial host restore used cached packages with online audit disabled for that invocation;
no repository audit configuration was changed. The final Android restore/build succeeded.

Final normal APK: `normal-final-7E91AFD5.apk`, SHA-256
`7E91AFD5463E81A397AB76162B6C9ABA20D31D4C0B4FE2BE76145AA816485A73` (33,554,091 bytes).
The native smoke passed in
`artifacts/native/20260911T212131257-smoke-303a67beb7394bfdbe23b2cb56088596`, verifying matching
input/installed hashes, cold startup and Home/resume. Twelve saves survived replacement.

Final normal frame captures use the same phone, 100% zoom and magnifier off. The original
controlled puzzle resumed with 13/196 progress, zero mistakes. Undo history was initialized
before the reset; move cases then requested 20 Undo/fill pairs at (920,1897)/(658,724), with
200 ms after each input. Screenshots verify the same board and zero mistakes afterward.

| Capture (`squarebuzz-normal-`) | Frames | Deadline misses | Histogram p50 / p95 / p99 |
| --- | ---: | ---: | --- |
| `continue12-1` | 24 | 1 | 5 / 7 / 61 ms |
| `continue12-2` | 21 | 2 | 5 / 9 / 57 ms |
| `continue12-3` | 20 | 1 | 5 / 9 / 9 ms |
| `moves20` | 480 | 123 (25.62%) | 5 / 16 / 20 ms |
| `talkback-moves20` | 545 | 78 (14.31%) | 6 / 17 / 21 ms |

Continue windows include navigation and two seconds on the list. They show occasional long
opening frames, not a sustained stall. Board moves still miss adaptive deadlines. The bounded
raw frame history includes 8.33 ms deadlines; a universal 16.67 ms threshold would misclassify
those frames. Do not infer a before/after regression or speedup from different fixtures and
adaptive refresh histories. These results do not certify smoothness on all target devices.

## Exclusions and session limitations

- Interrupted transfers are not application failures. The final transfer stalled until only
  the selected phone's ADB transport was reconnected; installation then succeeded in 2.94 seconds.
- `diagnostic-alloc-final-33D9A1DF.apk` failed startup with missing embedded XAML after a direct
  Compile target had been used. Full Rebuild resolved the packaging failure. That APK is excluded;
  compile-only success is not validation of a deployable MAUI package.
- `phone-control-magnifier20` is excluded because the pause panel remained open and the toggle
  had not changed. Use `magnifier20-valid` instead.
- `phone-control-talkback-build20` has a valid allocation trace, but its early frame dump has
  zero frames. Its 4,950 ms empty-histogram sentinel is not a latency measurement.
- Final `squarebuzz-normal-talkback-build20` includes TalkBack's unrelated phone-permission
  pre-prompt and is excluded as isolated board-construction latency. The prompt was cancelled
  and visually verified absent before `talkback-moves20`.
- An earlier UiAutomator inspection suppressed accessibility services; that overlay change is
  a measurement side effect, not evidence of an application bug.
- The menu reported no saves after an earlier interrupted session despite no agent uninstall
  or data-clear command. The cause is unknown. Controlled results use the subsequently verified
  new fixture; different historical puzzle coordinates must not be reused interchangeably.
- Lower-end phone, native 25×25 tablet and Apple font rendering remain unavailable/unverified
  coverage. They must be completed before making corresponding device-level claims.
