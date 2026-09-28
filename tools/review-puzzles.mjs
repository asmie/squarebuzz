// Generates a local artwork contact sheet and symmetry report. No npm packages required.
// Run from the repository root: node tools/review-puzzles.mjs
import fs from 'node:fs';
import path from 'node:path';

const source = process.argv[2] ?? 'src/Squarebuzz.Core/Content/puzzles.json';
const destination = process.argv[3] ?? 'artifacts/puzzle-review';
const content = JSON.parse(fs.readFileSync(source, 'utf8'));
const escape = value => String(value).replaceAll('&', '&amp;').replaceAll('<', '&lt;')
  .replaceAll('>', '&gt;').replaceAll('"', '&quot;');

function analyse(puzzle) {
  const { width, height, rows } = puzzle;
  const cells = rows.join('').split('').map(cell => cell === '#');
  const filled = cells.filter(Boolean).length;
  function overlap(transform) {
    let intersection = 0;
    let union = 0;
    for (let y = 0; y < height; y++) {
      for (let x = 0; x < width; x++) {
        const [tx, ty] = transform(x, y);
        const a = cells[y * width + x];
        const b = cells[ty * width + tx];
        intersection += a && b ? 1 : 0;
        union += a || b ? 1 : 0;
      }
    }
    return union === 0 ? 1 : intersection / union;
  }
  const seen = new Set();
  const components = [];
  for (let index = 0; index < cells.length; index++) {
    if (!cells[index] || seen.has(index)) continue;
    const pending = [index];
    seen.add(index);
    let count = 0;
    while (pending.length) {
      const current = pending.pop();
      count++;
      const x = current % width;
      const y = Math.floor(current / width);
      for (const [nx, ny] of [[x - 1, y], [x + 1, y], [x, y - 1], [x, y + 1]]) {
        const next = ny * width + nx;
        if (nx >= 0 && nx < width && ny >= 0 && ny < height && cells[next] && !seen.has(next)) {
          seen.add(next);
          pending.push(next);
        }
      }
    }
    components.push(count);
  }
  return {
    id: puzzle.id, revision: puzzle.revision ?? 1, pack: puzzle.pack, width, height,
    fill: filled / cells.length,
    leftRight: overlap((x, y) => [width - 1 - x, y]),
    topBottom: overlap((x, y) => [x, height - 1 - y]),
    halfTurn: overlap((x, y) => [width - 1 - x, height - 1 - y]),
    components: components.sort((a, b) => b - a),
  };
}

const report = content.puzzles.map(analyse);
const pct = value => `${Number((value * 100).toFixed(1))}%`;
const cards = content.puzzles.map((puzzle, index) => {
  const result = report[index];
  const rectangles = puzzle.rows.flatMap((row, y) => [...row].flatMap((cell, x) =>
    cell === '#' ? [`<rect x="${x}" y="${y}" width="1" height="1"/>`] : []));
  const flagged = result.leftRight >= 0.85 || result.topBottom >= 0.85 || result.halfTurn >= 0.85;
  return `<article data-pack="${escape(puzzle.pack)}" data-flagged="${flagged}">
    <h2>${escape(puzzle.id)} <small>r${result.revision}</small></h2>
    <svg role="img" aria-label="${escape(puzzle.id)}" viewBox="0 0 ${puzzle.width} ${puzzle.height}" shape-rendering="crispEdges">
      <rect width="100%" height="100%" fill="#f4efe7"/><g fill="${escape(puzzle.color)}">${rectangles.join('')}</g>
    </svg>
    <p>${escape(puzzle.pack)} · ${puzzle.width}×${puzzle.height} · fill ${pct(result.fill)}</p>
    <p>Mirror L/R ${pct(result.leftRight)} · T/B ${pct(result.topBottom)} · 180° ${pct(result.halfTurn)}</p>
  </article>`;
});
const html = `<!doctype html>
<html lang="en"><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">
<title>squarebuzz artwork review</title>
<style>
body{font:15px/1.5 system-ui,sans-serif;margin:24px;background:#faf8f4;color:#262523}h1{margin-bottom:4px}
.controls{display:flex;gap:20px;margin:20px 0;flex-wrap:wrap}select{font:inherit}main{display:grid;grid-template-columns:repeat(auto-fill,minmax(220px,1fr));gap:18px}
article{padding:16px;background:white;border:1px solid #ddd;border-radius:8px}h2{font-size:17px;margin:0 0 12px}small{font-size:12px;color:#777}svg{display:block;width:180px;height:180px;margin:auto}p{font-size:12px;margin:10px 0 0}article[hidden]{display:none}@media print{.controls{display:none}article{break-inside:avoid}main{grid-template-columns:repeat(4,1fr)}}
</style>
<h1>squarebuzz artwork review</h1>
<p>${report.length} current pictures. Archived revisions are excluded. Regenerate after changing puzzle content.</p>
<p>Symmetry is the overlap of filled cells with the reflected or rotated picture (intersection / union). Blank background does not increase the score. A high score is a review prompt, not a quality verdict.</p>
<div class="controls"><label>Pack <select id="pack"><option value="">All</option>${[...new Set(report.map(p => p.pack))].sort().map(pack => `<option>${escape(pack)}</option>`).join('')}</select></label>
<label><input id="flagged" type="checkbox"> Only symmetry ≥85% on any axis</label></div>
<main>${cards.join('\n')}</main>
<script>
const pack = document.querySelector('#pack');
const flagged = document.querySelector('#flagged');
function filter() { document.querySelectorAll('article').forEach(card => {
  card.hidden = (pack.value && card.dataset.pack !== pack.value) || (flagged.checked && card.dataset.flagged !== 'true');
}); }
pack.addEventListener('change', filter); flagged.addEventListener('change', filter);
</script></html>`;
fs.mkdirSync(destination, { recursive: true });
fs.writeFileSync(path.join(destination, 'index.html'), html);
fs.writeFileSync(path.join(destination, 'metrics.json'), JSON.stringify(report, null, 2) + '\n');
for (const item of report.filter(p => Math.max(p.leftRight, p.topBottom, p.halfTurn) >= 0.85)) {
  console.log(`${item.id.padEnd(15)} ${item.width}x${item.height} L/R ${pct(item.leftRight).padStart(4)} T/B ${pct(item.topBottom).padStart(4)} 180° ${pct(item.halfTurn).padStart(4)}`);
}
console.log(`Wrote ${report.length} pictures to ${destination}/index.html`);
