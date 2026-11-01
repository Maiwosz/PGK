// Sprawdza, czy teren nie wystaje ponad tor (tor "pod ziemią") – na danych przepisu.
import fs from 'node:fs';
const dir = process.argv[2];
const meta = JSON.parse(fs.readFileSync(dir + '/terrain.json', 'utf8'));
const hb = fs.readFileSync(dir + '/terrain.bin'); const h16 = new Uint16Array(hb.buffer, hb.byteOffset, hb.length / 2);
const route = JSON.parse(fs.readFileSync(dir + '/route.json', 'utf8'));
const { samplesX: NX, samplesY: NY, minX, minY, cell, zMin, zMax } = meta;
const H = (x, y) => zMin + h16[y * NX + x] / 65535 * (zMax - zMin);
function terrain(wx, wz) {
  const fx = (wx - minX) / cell, fy = (wz - minY) / cell;
  const x0 = Math.max(0, Math.min(NX - 2, Math.floor(fx))), y0 = Math.max(0, Math.min(NY - 2, Math.floor(fy)));
  const tx = fx - x0, ty = fy - y0;
  return (H(x0, y0) * (1 - tx) + H(x0 + 1, y0) * tx) * (1 - ty) + (H(x0, y0 + 1) * (1 - tx) + H(x0 + 1, y0 + 1) * tx) * ty;
}
const n = route.kinds.length;
let bad = [], cur = null;
for (let i = 0; i < n; i++) {
  if (route.kinds[i] === 2) continue;   // tunel: teren nad nim jest zamierzony
  const x = route.pts[i * 3], y = route.pts[i * 3 + 1], z = route.pts[i * 3 + 2];
  // max wysokości terenu w poprzek toru (±3 m) względem wysokości toru
  let worst = -1e9;
  const nx = i + 1 < n ? route.pts[(i + 1) * 3] - x : x - route.pts[(i - 1) * 3];
  const nz = i + 1 < n ? route.pts[(i + 1) * 3 + 2] - z : z - route.pts[(i - 1) * 3 + 2];
  const l = Math.hypot(nx, nz) || 1; const px = -nz / l, pz = nx / l;
  for (const off of [-3, -1.5, 0, 1.5, 3]) worst = Math.max(worst, terrain(x + px * off, z + pz * off) - y);
  if (worst > 0.35) { if (!cur || i - cur.end > 3) { cur = { start: i, end: i, kind: route.kinds[i], max: worst }; bad.push(cur); } else { cur.end = i; cur.max = Math.max(cur.max, worst); cur.kind = Math.max(cur.kind, route.kinds[i]); } }
}
console.log('odcinki, gdzie teren wystaje ponad tor (>0.35 m):', bad.length, ' łącznie m:', bad.reduce((s, b) => s + (b.end - b.start + 1) * 2, 0));
for (const b of bad.slice(0, 40)) console.log(`  idx ${b.start}-${b.end} (${(b.end - b.start + 1) * 2} m) kind=${b.kind} max=+${b.max.toFixed(1)} m  @ (${route.pts[b.start * 3].toFixed(0)}, ${route.pts[b.start * 3 + 2].toFixed(0)})`);
