import fs from 'node:fs';
import { writePng } from './png.mjs';
const dir = process.argv[2];
const meta = JSON.parse(fs.readFileSync(dir + '/terrain.json', 'utf8'));
const hb = fs.readFileSync(dir + '/terrain.bin'); const h16 = new Uint16Array(hb.buffer, hb.byteOffset, hb.length / 2);
const cls = fs.readFileSync(dir + '/terrain_class.bin');
const route = JSON.parse(fs.readFileSync(dir + '/route.json', 'utf8'));
const objs = JSON.parse(fs.readFileSync(dir + '/objects.json', 'utf8')).objects;
const { samplesX: NX, samplesY: NY, minX, minY, cell, zMin, zMax } = meta;
const S = 2, w = Math.floor(NX / S), h = Math.floor(NY / S);
const rgb = new Uint8Array(w * h * 3);
const palette = [[150, 150, 130], [90, 90, 95], [90, 150, 70], [170, 130, 80], [225, 210, 150], [130, 120, 120]];
const hv = (x, y) => zMin + h16[y * NX + x] / 65535 * (zMax - zMin);
for (let y = 0; y < h; y++) for (let x = 0; x < w; x++) {
  const X = x * S, Y = y * S;
  const z = hv(X, Y);
  // cieniowanie z gradientu
  const dzx = hv(Math.min(NX - 1, X + 1), Y) - hv(Math.max(0, X - 1), Y), dzy = hv(X, Math.min(NY - 1, Y + 1)) - hv(X, Math.max(0, Y - 1));
  const shade = Math.max(0.35, Math.min(1.4, 1 + (dzx - dzy) * 0.12));
  const c = palette[cls[Y * NX + X]] || palette[0];
  const wl = z < 0;
  const o = ((h - 1 - y) * w + x) * 3;
  if (wl) { rgb[o] = 30; rgb[o + 1] = 70; rgb[o + 2] = 140; continue; }
  const t = 0.6 + 0.4 * Math.min(1, z / 100);
  rgb[o] = Math.min(255, c[0] * shade * t); rgb[o + 1] = Math.min(255, c[1] * shade * t); rgb[o + 2] = Math.min(255, c[2] * shade * t);
}
const dot = (wx, wz, col, r = 1) => {
  const x = Math.floor((wx - minX) / cell / S), y = h - 1 - Math.floor((wz - minY) / cell / S);
  for (let dy = -r; dy <= r; dy++) for (let dx = -r; dx <= r; dx++) { const xx = x + dx, yy = y + dy; if (xx < 0 || yy < 0 || xx >= w || yy >= h) continue; const o = (yy * w + xx) * 3; rgb[o] = col[0]; rgb[o + 1] = col[1]; rgb[o + 2] = col[2]; }
};
const colors = { building: [200, 60, 200], bridge: [60, 220, 255], tunnel: [255, 160, 0], fence: [255, 255, 0], station: [255, 255, 255] };
for (const o of objs) dot(o.p[0], o.p[2], colors[o.c], o.c === 'building' ? 0 : 1);
for (let i = 0; i < route.kinds.length; i++) { const p = [route.pts[i * 3], route.pts[i * 3 + 1], route.pts[i * 3 + 2]]; dot(p[0], p[2], route.kinds[i] === 1 ? [0, 120, 255] : route.kinds[i] === 2 ? [255, 120, 0] : [255, 0, 0], 1); }
writePng(dir + '/../../../Tools/GtaExtract/recipe-debug.png', w, h, rgb);
console.log('ok', w, h);
