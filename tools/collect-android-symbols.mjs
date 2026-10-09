import fs from 'node:fs';
import path from 'node:path';
import crypto from 'node:crypto';

// Called by export-android-symbols.ps1. ELF loadable sections must match the AAB;
// file names alone cannot distinguish symbols from a different build or ABI.
const [input, output, reportPath, ...roots] = process.argv.slice(2);
if (!input || !output || !reportPath || !roots.length) throw new Error('Input, output, report and search roots required');
const hash = data => crypto.createHash('sha256').update(data).digest('hex');
const walk = function* (folder) {
  if (!fs.existsSync(folder)) return;
  for (const entry of fs.readdirSync(folder, { withFileTypes: true })) {
    const file = path.join(folder, entry.name);
    if (entry.isDirectory()) yield* walk(file);
    else if (entry.isFile() && entry.name.endsWith('.so')) yield file;
  }
};
const elf = file => {
  const b = fs.readFileSync(file);
  if (b.length < 64 || b.subarray(0, 4).toString('hex') !== '7f454c46' || b[5] !== 1) return null;
  const is64 = b[4] === 2;
  if (!is64 && b[4] !== 1) return null;
  const word = offset => is64 ? Number(b.readBigUInt64LE(offset)) : b.readUInt32LE(offset);
  const sectionStart = word(is64 ? 40 : 32);
  const sectionSize = b.readUInt16LE(is64 ? 58 : 46);
  const sectionCount = b.readUInt16LE(is64 ? 60 : 48);
  const stringsIndex = b.readUInt16LE(is64 ? 62 : 50);
  if (!sectionCount || sectionStart + sectionSize * sectionCount > b.length) return null;
  const sections = Array.from({ length: sectionCount }, (_, i) => {
    const at = sectionStart + i * sectionSize;
    return { nameIndex: b.readUInt32LE(at), type: b.readUInt32LE(at + 4),
      flags: word(at + 8), address: word(at + (is64 ? 16 : 12)),
      offset: word(at + (is64 ? 24 : 16)), length: word(at + (is64 ? 32 : 20)),
      link: b.readUInt32LE(at + (is64 ? 40 : 24)), entrySize: word(at + (is64 ? 56 : 36)) };
  });
  const names = sections[stringsIndex];
  if (!names) return null;
  const stringAt = (table, index) => {
    const start = table.offset + index;
    let end = start;
    while (end < table.offset + table.length && b[end]) end++;
    return b.toString('utf8', start, end);
  };
  const loaded = crypto.createHash('sha256');
  loaded.update(String(b.readUInt16LE(18)));
  let functions = 0, symbols = 0;
  for (const s of sections) {
    s.name = stringAt(names, s.nameIndex);
    if ((s.flags & 2) && s.type !== 8) {
      loaded.update(`${s.name}:${s.type}:${s.address}:${s.length}:`);
      loaded.update(b.subarray(s.offset, s.offset + s.length));
    }
    if (s.type === 2 && s.entrySize) {
      for (let at = s.offset; at < s.offset + s.length; at += s.entrySize) {
        if (!b.readUInt32LE(at) || !b.readUInt16LE(at + (is64 ? 6 : 14))) continue;
        symbols++;
        if ((b[at + (is64 ? 4 : 12)] & 15) === 2) functions++;
      }
    }
  }
  return { signature: loaded.digest('hex'), functions, symbols, bytes: b.length, sha256: hash(b) };
};
const candidates = new Map();
for (const root of roots) {
  for (const source of walk(root)) {
    const info = elf(source);
    if (!info?.symbols) continue;
    const previous = candidates.get(info.signature);
    if (!previous || previous.symbols < info.symbols) candidates.set(info.signature, { ...info, source });
  }
}
const included = [], missing = [];
for (const file of walk(input)) {
  const relative = path.relative(input, file);
  const info = elf(file);
  const candidate = info && candidates.get(info.signature);
  if (!candidate) { missing.push(relative.replaceAll('\\', '/')); continue; }
  const target = path.join(output, relative);
  fs.mkdirSync(path.dirname(target), { recursive: true });
  fs.copyFileSync(candidate.source, target);
  included.push({ library: relative.replaceAll('\\', '/'), source: candidate.source,
    loadable_sections_sha256: info.signature, symbols_sha256: candidate.sha256,
    defined_symbols: candidate.symbols, function_symbols: candidate.functions });
}
if (!included.length) throw new Error('No matching native symbol tables found');
const report = { included_count: included.length, unavailable_count: missing.length, included, unavailable: missing };
fs.writeFileSync(reportPath, JSON.stringify(report, null, 2) + '\n');
console.log(JSON.stringify({ included: included.length, unavailable: missing.length }));
