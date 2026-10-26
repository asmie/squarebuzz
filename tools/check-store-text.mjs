import fs from 'node:fs/promises';
import path from 'node:path';
import assert from 'node:assert/strict';

const root = path.resolve(import.meta.dirname, '..');
const read = async name => JSON.parse(await fs.readFile(path.join(root, name), 'utf8'));
const play = (await read('store/google-play/translations.json')).translations;
const microsoft = (await read('store/microsoft-store/listing-text.json')).translations;
const notes = (await read('store/google-play/release-notes-1.0.2-build-9.json')).translations;
const settings = await fs.readFile(path.join(root, 'src/Squarebuzz.Core/Model/GameSettings.cs'), 'utf8');
const supported = [...settings.matchAll(/new\(AppLanguage\.\w+, "([^"]+)"/g)].map(m => m[1]).sort();
const normalise = s => s.replaceAll('\r\n', '\n').trim();
for (const entries of [play, microsoft, notes]) {
  assert.deepEqual(entries.map(t => t.app_language).sort(), supported, 'Locale coverage or duplicates');
}
for (const t of play) {
  const ms = microsoft.find(x => x.app_language === t.app_language);
  const note = notes.find(x => x.app_language === t.app_language);
  assert.equal(ms.short_description, t.short_description);
  assert.equal(ms.release_notes, note.release_notes);
  assert.equal(ms.description.split('\n\n').slice(0, -1).join('\n\n'), t.full_description.split('\n\n').slice(0, -1).join('\n\n'));
  assert(ms.description.includes('Windows 10') && ms.description.includes('Windows 11'));
  assert.deepEqual(ms.features, t.full_description.split('\n').filter(s => s.startsWith('• ')).map(s => s.slice(2)));
  assert.equal(ms.features.length, 5);
  assert([...note.release_notes].length <= 500);
  for (const [filename, key, limit] of [['title', 'app_name', 30], ['short-description', 'short_description', 80], ['full-description', 'full_description', 4000]]) {
    assert([...t[key]].length <= limit, `${t.app_language}/${key} length`);
    const mirror = await fs.readFile(path.join(root, 'store/google-play/text', t.google_play_language || t.app_language, filename + '.txt'), 'utf8');
    assert.equal(normalise(mirror), normalise(t[key]), `${t.app_language}/${key} mirror`);
  }
  for (const value of [t.short_description, t.full_description, ms.description, note.release_notes]) {
    assert(!value.includes('\uFFFD'));
    // Store copy avoids em/en dashes and dash-like punctuation.
    assert(!/[\u2012-\u2015\u2E3A]| - /.test(value), `${t.app_language} dash punctuation`);
  }
  for (const quantity of ['600', '130', '39']) assert(t.full_description.includes(quantity));
}
console.log(`Store text: ${supported.length} languages; descriptions, features, release notes, mirrors and lengths pass.`);
