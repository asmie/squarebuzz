# Puzzle artwork review

Reviewed 2026-09-28: all 130 current pictures, at 5x5 and 10x10. The catalogue is in campaign
content order. Archived versions are excluded from visual-quality gates because old saves
must retain their exact boards.

## Findings and changes

Most subjects read clearly at their intended grid size. Fourteen larger boards needed more
variation; the previous stegosaurus also had an unclear silhouette. Their revisions use
recognizable changes in pose, contour or detail. IDs and campaign order are unchanged.

Among the 95 larger boards, 16 initially had at least 85% left/right reflection overlap.
Two retain it after review: the snowflake and basketball. Those subjects intentionally use
geometric symmetry. The small 5x5 boards also retain simple symmetric symbols for introductory
levels, including the heart, sun and target.

| Picture | L/R before | L/R after | Change |
| --- | --- | --- | --- |
| volcano | 100% | 63% | Offset eruption and lava channel |
| butterfly | 100% | 58% | Different wing poses |
| castle | 100% | 55% | Uneven towers and a flag |
| stego | 92% | 56% | Longer side profile with plates and a tail |
| cactus | 88% | 55% | Arms at different heights |
| rainbow | 100% | 61% | Cloud at one end |
| trophy | 100% | 84.8% | Asymmetric cup and handles |
| pizza | 100% | 55% | Angled slice and uneven toppings |
| submarine | 86% | 69% | Side profile with an offset periscope |
| umbrella | 92% | 73% | Larger curved handle |
| potion | 94% | 37% | Offset bottle, bubbles and a highlight |
| octopus | 100% | 79% | Different tentacle positions |
| cupcake | 86% | 74% | Offset frosting |
| rocket | 100% | 30% | Unequal fins and exhaust |

The planet and bone remain close to 180-degree symmetry. Their rotational outlines fit the
subjects, and neither is a near left/right or top/bottom copy. The report flags them for review
instead of changing cells solely to lower a score.

This is a visual and structural review, not a recorded playtest. During human review, show the
pictures without their labels and ask intended players to name them. Pay particular attention
to dinosaurs, instruments and the most abstract 5x5 symbols.

## Generate the catalogue

Run from the repository root with Node.js installed:

```powershell
node tools/review-puzzles.mjs
```

Open `artifacts/puzzle-review/index.html` in a browser. It includes all current boards, pack
filters, a symmetry filter and fill percentages. `metrics.json` records the measurements.
Both files are local generated output; rerun the tool after editing content.

Reflection overlap is filled-cell intersection divided by union after mirroring inside the
grid. Blank background does not raise the score. The report also measures 180-degree overlap
and counts connected filled regions. These are review aids: a disconnected sparkle or a
naturally symmetric object can still be good artwork.

## Authoring and save compatibility

1. Keep the subject recognizable at its actual grid size. Inspect both the solid silhouette
   and the separated-cell rendering; inspect it without the name as well.
2. Prefer a side view, a different pose or an uneven detail over an isolated cell added only
   to break symmetry. For 10x10 boards, target 45-65% fill and include some longer clue runs.
3. Keep at most two entirely empty rows or columns per axis. Avoid accidental duplicates,
   scattered one-cell noise and thin diagonals that require guessing.
4. Before changing a published picture, copy its full entry to `archivedPuzzles`, including
   its revision, and increment the current revision. Revision 1 is the default for old entries.
   Never change or reuse an archived (ID, revision) pair.
5. Keep the current array order stable, because it sets campaign milestone order.
6. Run `dotnet run tools/validate-puzzles.cs` and the Core content tests. The validator checks
   revision continuity and archive structure as well as current-board solvability. Original
   archives are exempt from the current solver and symmetry rules.

`ArtworkReviewTests` requires reflection overlap below 85% for 10x10 pictures except the
snowflake and basketball. A new exception needs a documented visual reason. The older 5x5
symbols and rotational similarity are reviewed manually.

## Evidence from this review

- All 130 current boards have distinct row data.
- Independent enumeration of line placements solved all 130 current boards without guessing.
- All 144 definitions present at the start of this audit are preserved. There are now 28
  archived revisions: the 14 from the save-compatibility fix and 14 from this artwork review.
- Current IDs, packs and campaign order are unchanged.
- The production .NET solver run is blocked by this machine's application-control policy.
  See [the audit guide](audit.md) for the checks still needed before sign-off.
