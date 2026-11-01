import fs from 'node:fs';
import { openGame, loadIde, loadIpl, loadColBounds } from './gta.mjs';
const gameDir = process.argv[2];
const g = openGame(gameDir);
const ide = loadIde(g), ipl = loadIpl(g, ide), col = loadColBounds(g);
const route = JSON.parse(fs.readFileSync('D:/Dev/PGK/Assets/Mission/Recipe/route.json', 'utf8'));
const pts = []; for (let i = 0; i < route.kinds.length; i++) pts.push([route.pts[i * 3], route.pts[i * 3 + 2]]);
const VEG = /^sm_|vbg_|veg|palm|bush|tree|cedar|elm|oak|pine|fir|redwood|cypress|scrub|shrub|hedge|plant|copse|bevhil|grass|fern|cactus|joshua|sapling|cottonwood/i;
const byModel = new Map(); let total = 0; const radius = 300;
for (const o of ipl) {
  if (o.interior !== 0 || /^lod/i.test(o.model) || !VEG.test(o.model)) continue;
  let best = 1e9; for (let i = 0; i < pts.length; i += 4) { const d = Math.hypot(pts[i][0] - o.x, pts[i][1] - o.y); if (d < best) best = d; }
  if (best > radius) continue;
  const c = col.get(o.model.toLowerCase());
  const sz = c ? c.max.map((v, i) => v - c.min[i]) : null;
  const e = byModel.get(o.model) || { n: 0, sz }; e.n++; byModel.set(o.model, e); total++;
}
console.log('roślin w promieniu', radius, 'm od trasy:', total, 'modeli:', byModel.size);
const rows = [...byModel.entries()].sort((a, b) => b[1].n - a[1].n).slice(0, 45);
for (const [m, e] of rows) console.log(m.padEnd(26), String(e.n).padStart(5), e.sz ? e.sz.map(v => v.toFixed(0)).join('x') : '?');
