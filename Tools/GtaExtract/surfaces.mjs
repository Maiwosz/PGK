import fs from 'node:fs';
/** Nazwy typów powierzchni (indeks = id materiału w kolizji), z data/surfaud.dat. */
export function loadSurfaceNames(gameDir) {
  const names = [];
  for (const raw of fs.readFileSync(gameDir + '/data/surfaud.dat', 'latin1').split(/\r?\n/)) {
    const t = raw.trim();
    if (!t || t.startsWith(';') || t.startsWith('#')) continue;
    const name = t.split(/\s+/)[0];
    if (/^[A-Z][A-Z0-9_]+$/.test(name)) names.push(name);
  }
  return names;
}
