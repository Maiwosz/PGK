// Wczytywanie danych mapy GTA SA: IDE (id -> model), IPL (rozmieszczenie obiektów), COL (kolizje).
import fs from 'node:fs';
import path from 'node:path';
import { Img } from './img.mjs';

export function openGame(gameDir) {
  const g = { dir: gameDir, img: new Img(path.join(gameDir, 'models', 'gta3.img')) };
  g.gtaDat = readLines(path.join(gameDir, 'data', 'gta.dat'));
  return g;
}

function readLines(p) {
  return fs.readFileSync(p, 'latin1').split(/\r?\n/);
}

function gamePath(g, rel) {
  return path.join(g.dir, rel.split(String.fromCharCode(92)).join(path.sep));
}

/** Tokenizuje wiersz "a, b, c" (komentarze po #). */
function tok(line) {
  const l = line.split('#')[0].trim();
  if (!l) return null;
  return l.split(',').map(s => s.trim());
}

/** Wczytuje wszystkie pliki IDE z gta.dat -> Map(id -> { id, model, txd, drawDist, flags }). */
export function loadIde(g) {
  const objects = new Map();
  for (const line of g.gtaDat) {
    const m = /^IDE\s+(.+)$/i.exec(line.trim());
    if (!m) continue;
    const p = gamePath(g, m[1].trim());
    if (!fs.existsSync(p)) continue;
    let section = '';
    for (const raw of readLines(p)) {
      const t = raw.trim();
      if (!t || t.startsWith('#')) continue;
      if (t === 'end') { section = ''; continue; }
      if (!section) { section = t.toLowerCase(); continue; }
      if (section !== 'objs' && section !== 'tobj') continue;
      const f = tok(t);
      if (!f || f.length < 5) continue;
      const id = parseInt(f[0], 10);
      objects.set(id, { id, model: f[1], txd: f[2], flags: parseInt(f[f.length - 1], 10) || 0 });
    }
  }
  return objects;
}

/** Wczytuje IPL (tekstowe z gta.dat + binarne strumieniowane z gta3.img). */
export function loadIpl(g, ide) {
  const out = [];
  const add = (id, interior, x, y, z, qx, qy, qz, qw, lod, src) => {
    const o = ide.get(id);
    out.push({ id, model: o ? o.model : `#${id}`, interior, x, y, z, qx, qy, qz, qw, lod, src });
  };

  const parseText = (text, src) => {
    let inInst = false;
    for (const raw of text.split(/\r?\n/)) {
      const t = raw.trim();
      if (t === 'inst') { inInst = true; continue; }
      if (t === 'end') { inInst = false; continue; }
      if (!inInst) continue;
      const f = tok(t);
      if (!f || f.length < 10) continue;
      add(parseInt(f[0], 10), parseInt(f[2], 10), +f[3], +f[4], +f[5], +f[6], +f[7], +f[8], +f[9], parseInt(f[10] ?? '-1', 10), src);
    }
  };

  const parseBinary = (buf, src) => {
    const num = buf.readUInt32LE(4);
    const off = buf.readUInt32LE(28);
    for (let i = 0; i < num; i++) {
      const o = off + i * 40;
      add(buf.readInt32LE(o + 28), buf.readInt32LE(o + 32),
        buf.readFloatLE(o), buf.readFloatLE(o + 4), buf.readFloatLE(o + 8),
        buf.readFloatLE(o + 12), buf.readFloatLE(o + 16), buf.readFloatLE(o + 20), buf.readFloatLE(o + 24),
        buf.readInt32LE(o + 36), src);
    }
  };

  for (const line of g.gtaDat) {
    const m = /^IPL\s+(.+)$/i.exec(line.trim());
    if (!m) continue;
    const p = gamePath(g, m[1].trim());
    if (!fs.existsSync(p)) continue;
    const buf = fs.readFileSync(p);
    if (buf.toString('ascii', 0, 4) === 'bnry') parseBinary(buf, path.basename(p));
    else parseText(buf.toString('latin1'), path.basename(p));
  }
  for (const name of g.img.names()) {
    if (!/\.ipl$/i.test(name)) continue;
    const buf = g.img.read(name);
    if (buf.toString('ascii', 0, 4) === 'bnry') parseBinary(buf, name);
  }
  return out;
}

/**
 * Wczytuje nagłówki kolizji (COL) ze wszystkich plików .col w gta3.img.
 * Zwraca Map(nazwaModeluMałymiLiterami -> { min, max, file, offset, size, version }).
 */
export function loadColBounds(g) {
  const cols = new Map();
  for (const name of g.img.names()) {
    if (!/\.col$/i.test(name)) continue;
    const buf = g.img.read(name);
    let p = 0;
    while (p + 32 <= buf.length) {
      const magic = buf.toString('ascii', p, p + 4);
      if (!/^COL[1-4L]$/.test(magic)) break;
      const size = buf.readUInt32LE(p + 4);
      let end = p + 8;
      while (buf[end] !== 0 && end < p + 30) end++;
      const model = buf.toString('ascii', p + 8, end).toLowerCase();
      let min, max;
      const b = p + 32;
      if (magic === 'COLL') { // COL1: radius, center, min, max
        min = [buf.readFloatLE(b + 16), buf.readFloatLE(b + 20), buf.readFloatLE(b + 24)];
        max = [buf.readFloatLE(b + 28), buf.readFloatLE(b + 32), buf.readFloatLE(b + 36)];
      } else { // COL2/3: min, max, center, radius
        min = [buf.readFloatLE(b), buf.readFloatLE(b + 4), buf.readFloatLE(b + 8)];
        max = [buf.readFloatLE(b + 12), buf.readFloatLE(b + 16), buf.readFloatLE(b + 20)];
      }
      cols.set(model, { min, max, file: name, offset: p, size: size + 8, version: magic });
      p += size + 8;
    }
  }
  return cols;
}

/**
 * Siatka trójkątów kolizji modelu (COL2/COL3). Zwraca { verts: Float32Array(3n), tris: Uint16Array(3m), mats: Uint8Array(m) }
 * lub null (brak siatki / nieobsługiwana wersja). Współrzędne lokalne modelu (układ GTA).
 */
export function readColMesh(g, colInfo) {
  g._colFiles ??= new Map();
  let buf = g._colFiles.get(colInfo.file);
  if (!buf) { buf = g.img.read(colInfo.file); g._colFiles.set(colInfo.file, buf); }
  const p = colInfo.offset;
  const magic = buf.toString('ascii', p, p + 4);
  if (magic !== 'COL2' && magic !== 'COL3') return null;
  const base = p + 4;
  const numFaces = buf.readUInt16LE(p + 76);
  if (numFaces === 0) return null;
  const offVerts = buf.readUInt32LE(p + 96);
  const offFaces = buf.readUInt32LE(p + 100);
  if (!offVerts || !offFaces || offFaces <= offVerts) return null;
  const nv = Math.floor((offFaces - offVerts) / 6);
  const verts = new Float32Array(nv * 3);
  for (let i = 0; i < nv * 3; i++) verts[i] = buf.readInt16LE(base + offVerts + i * 2) / 128;
  const tris = new Uint16Array(numFaces * 3);
  const mats = new Uint8Array(numFaces);
  for (let i = 0; i < numFaces; i++) {
    const o = base + offFaces + i * 8;
    tris[i * 3] = buf.readUInt16LE(o);
    tris[i * 3 + 1] = buf.readUInt16LE(o + 2);
    tris[i * 3 + 2] = buf.readUInt16LE(o + 4);
    mats[i] = buf[o + 6];
  }
  return { verts, tris, mats };
}
