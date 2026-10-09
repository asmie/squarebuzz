# Human audit guide

Prepared 2026-09-28. Review the working tree, including untracked source and tests. The native
and profiling reports describe earlier revisions; they are supporting evidence, not approval
of this revision.

## Start here

1. Read [README](../README.md) for setup and the layer map.
2. Open the artwork catalogue: run `node tools/review-puzzles.mjs`, then open
   `artifacts/puzzle-review/index.html`. Read [the artwork review](puzzle-art.md).
3. Review save compatibility before gameplay changes. Continue and Restart must resolve the
   same board on which the marks were made.
4. Run the tests and validators below. Then use [native acceptance](../tests/Native/README.md)
   for behavior that requires an installed app.
5. Record findings against a commit plus any working-tree diff. Include untracked files before
   producing a clean review checkout.

## Code paths and invariants

| Review area | Start with | Verify |
| --- | --- | --- |
| Board identity | `GameSessionFactory`, `SessionOrigin`, `SavedGame`, `EmbeddedPuzzleRepository` | ID + revision for authored boards; request + seed + generator version for generated boards. |
| Upgrade compatibility | `Migration0011AuthoredPuzzleRevision`, `AuthoredSaveCompatibilityTests`, `AuthoredPuzzleRevisionPersistenceTests` | Existing saves default to revision 1; later saves persist the exact revision; restart retains it. |
| Input, hints and scoring | `GameSession`, `HintProvider`, `ClueStrikeCalculator` | One mistake per drag, correct undo provenance, hint usage retained, clues struck only when supported. |
| Completion and persistence | `GameCompletionService`, `GameSaveService`, `SqliteGameCompletionRepository` | Retries do not duplicate rewards; delayed saves cannot resurrect completed games. |
| Time and lifecycle | `GameTimeTracker`, `GameViewModel`, `GamePage` | Pause/background accounting, trial deadlines, disposal and resuming after settings changes. |
| Content and progression | `puzzles.json`, `LevelCatalog`, `TrophyEvaluator` | Stable milestone order and IDs; all prior board revisions retained. |
| Accessibility | `BoardView`, `GamePage.BuildCellOverlay`, platform services | Native focus, gesture ownership, reduced motion, narration and contrast. |
| Localization | `AppLanguages`, `LocalizationService`, resource files | Placeholder parity, script coverage, RTL and speaker review. |

Comments have been shortened to contracts and implementation constraints. The cleanup does not
change gameplay logic. The behavioral changes in this audit are the artwork revisions and
additional content validation; package and CI versions also changed.

## Reproducible checks

Run from the repository root with the pinned SDK. For a machine without MAUI workloads:

```powershell
$env:MSBuildEnableWorkloadResolver = 'false'
dotnet test tests/Squarebuzz.Core.Tests -c Release -- RunConfiguration.TreatNoTestsAsError=true
dotnet test tests/Squarebuzz.Data.Tests -c Release -- RunConfiguration.TreatNoTestsAsError=true
dotnet test tests/Squarebuzz.Presentation.Tests -c Release -- RunConfiguration.TreatNoTestsAsError=true
dotnet run tools/validate-puzzles.cs
dotnet run tools/check-strings.cs
node tools/review-puzzles.mjs
git diff --check
```

Treat zero discovered tests as a failure. This matters when an adapter cannot load: the default
test command can otherwise return success without executing tests. CI requests this check and
runs each test project in a separate step so later commands cannot mask an earlier failure.

Unset `MSBuildEnableWorkloadResolver` for MAUI builds. Follow the README for a platform-specific
build, then [native acceptance](../tests/Native/README.md). Use disposable progress for destructive
test cases. Keep device evidence with the tested source revision and package hash.

## Validation status

Results below are from this working tree on 2026-09-28. Runtime checks blocked by the machine
must be rerun on CI or an approved development host before sign-off.

| Check | September 28 result |
| --- | --- |
| Core, Data and Presentation Release test-project builds | Passed with no warnings or errors after dependency updates. |
| Automated .NET test execution | Blocked by Windows application control (`0x800711C7`), including the test adapter. No passing run claimed. |
| Production puzzle and string validators | Blocked by the same application-control policy. |
| Artwork | All 130 current boards inspected; independent line-pattern enumeration solved all 130. See artwork notes. |
| Saved artwork | All 144 definitions present at audit start remain available; 14 current boards received new revisions. |
| Dependency vulnerability checks | Restored Data, Presentation and Android app graphs report no known vulnerable packages, including transitive dependencies. |
| Outdated-package checks | Core test graph, including transitive packages, and direct Android app packages have no newer stable versions in the configured feeds. |
| MAUI app builds | Windows and Android Release builds passed with warnings treated as errors. Apple targets were not built on this host. |
| Host profiling tool | Release build passed with warnings treated as errors. Timings were not rerun. |
| Installed native behavior | Not rerun for this revision. Earlier device evidence is labelled historical. |

The independent artwork check is supporting evidence. It does not replace the production
solver tests or the native acceptance run.

Local test results are in `artifacts/audit-tests/`; dependency reports and the test-host
diagnostic log are in `artifacts/dependency-audit/`. All three test commands returned failure
with zero tests executed. These generated files are ignored by Git; preserve them separately
if they are needed as audit evidence.

## Dependency updates

Versions were checked against the official NuGet feed and project releases on 2026-09-28.
The app remains on .NET 10 and the xUnit v2 framework.

| Dependency | Before | After |
| --- | --- | --- |
| .NET SDK / workload set | 10.0.400 | 10.0.401 |
| CommunityToolkit.Maui | 15.0.0 | 15.0.1 |
| Microsoft.Maui.Controls | Transitive | 10.0.110, explicit app reference |
| Profiling Controls.Core | 10.0.60 | 10.0.110 |
| Logging.Debug | 10.0.10 | 10.0.12 |
| DependencyInjection | 10.0.0, tests only | 10.0.12, shared central pin |
| Microsoft.NET.Test.Sdk | 18.8.1 | 18.10.1 |
| xunit.runner.visualstudio | 3.1.5 | 4.0.0 |
| xunit.analyzers | 1.18.0, transitive | 2.1.0, central pin |
| actions/checkout | v4 | v7.0.1 |
| actions/setup-dotnet | v4 | v6.0.0 |
| actions/upload-artifact | v4 | v7.0.1 |

CommunityToolkit.Mvvm 8.4.2, Plugin.Maui.Audio 4.0.0, sqlite-net-pcl 1.11.285 and xunit 2.9.3
were already the current stable releases of those packages. The Apple workload uses
26.5.10318 and still recommends Xcode 26.6, as confirmed in its installed SDK metadata.

Sources: [.NET 10](https://dotnet.microsoft.com/en-us/download/dotnet/10.0),
[MAUI releases](https://github.com/dotnet/maui/releases),
[toolkit releases](https://github.com/CommunityToolkit/Maui/releases),
[xUnit adapter](https://xunit.net/releases/visualstudio/4.0.0),
[NuGet version feed](https://api.nuget.org/v3/index.json),
[checkout](https://github.com/actions/checkout/releases/tag/v7.0.1),
[setup-dotnet](https://github.com/actions/setup-dotnet/releases/tag/v6.0.0),
[upload-artifact](https://github.com/actions/upload-artifact/releases/tag/v7.0.1).

## Product review still needed

- Native-speaker review of translations; [status and priorities](translation-status.md).
- Recognition tests with intended players, especially small dinosaur and instrument pictures.
- Installed-app checks after the MAUI update, including screen readers and touch input.
- Privacy text and external destinations in Options/About.
- Licence metadata: the root uses BSD-3-Clause while `design/package.json` currently says MIT.
  Confirm the prototype's intended licence before distribution; this audit does not change it.
