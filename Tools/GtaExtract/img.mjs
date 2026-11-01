// Czytnik archiwum IMG (wersja 2, GTA San Andreas).
import fs from 'node:fs';

export class Img {
  constructor(path) {
    this.fd = fs.openSync(path, 'r');
    const head = Buffer.alloc(8);
    fs.readSync(this.fd, head, 0, 8, 0);
    if (head.toString('ascii', 0, 4) !== 'VER2') throw new Error('Nieobsługiwany format IMG: ' + path);
    const count = head.readUInt32LE(4);
    const dir = Buffer.alloc(count * 32);
    fs.readSync(this.fd, dir, 0, dir.length, 8);
    this.entries = new Map();
    for (let i = 0; i < count; i++) {
      const o = i * 32;
      const offset = dir.readUInt32LE(o) * 2048;
      const size = dir.readUInt16LE(o + 4) * 2048;
      let end = o + 8;
      while (dir[end] !== 0 && end < o + 32) end++;
      const name = dir.toString('ascii', o + 8, end);
      this.entries.set(name.toLowerCase(), { name, offset, size });
    }
  }

  has(name) { return this.entries.has(name.toLowerCase()); }

  read(name) {
    const e = this.entries.get(name.toLowerCase());
    if (!e) return null;
    const buf = Buffer.alloc(e.size);
    fs.readSync(this.fd, buf, 0, e.size, e.offset);
    return buf;
  }

  names() { return [...this.entries.values()].map(e => e.name); }
}
