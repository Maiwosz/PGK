import { openGame, loadIde, loadIpl, loadColBounds, readColMesh } from './gta.mjs';
const [gameDir, cx, cy, rad] = [process.argv[2], +process.argv[3], +process.argv[4], +process.argv[5]];
const g = openGame(gameDir);
const ide = loadIde(g), ipl = loadIpl(g, ide), col = loadColBounds(g);
const rows = [];
for (const o of ipl) {
  if (!o.exterior || /^lod/i.test(o.model)) continue;
  if (Math.hypot(o.x - cx, o.y - cy) > rad) continue;
  const c = col.get(o.model.toLowerCase());
  let info = 'nocol';
  if (c) { const m = readColMesh(g, c); info = (m ? 'mesh' + m.mats.length : 'nomesh') + ' ' + c.max.map((v, i) => (v - c.min[i]).toFixed(0)).join('x'); }
  rows.push(`${o.model.padEnd(26)} (${o.x.toFixed(0)},${o.y.toFixed(0)},${o.z.toFixed(0)}) ${info} ${o.src}`);
}
console.log(rows.length); console.log(rows.filter(r => !/tree|veg_|lamp|pole|bush|palm/i.test(r)).slice(0, 60).join('\n'));
