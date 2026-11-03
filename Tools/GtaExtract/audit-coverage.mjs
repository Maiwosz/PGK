// Audyt: co z obiektów GTA SA w pobliżu trasy trafia do przepisu, a co i dlaczego odpada.
// Użycie: node audit-coverage.mjs "<katalog GTA SA>" "<katalog przepisu>"
import fs from 'node:fs';
import { openGame, loadIde, loadIpl, loadColBounds, readColMesh } from './gta.mjs';
const [gameDir, recipeDir] = process.argv.slice(2);
const g = openGame(gameDir), ide = loadIde(g), ipl = loadIpl(g, ide), col = loadColBounds(g);
const route = JSON.parse(fs.readFileSync(recipeDir + '/route.json', 'utf8')).pts;     // Unity: x, y(wys.), z
const pts = []; for (let i = 0; i < route.length; i += 3) pts.push([route[i], route[i + 2]]);   // x, y(GTA)
const dist = (x, y) => { let b = 1e18; for (const p of pts) { const d = (p[0] - x) ** 2 + (p[1] - y) ** 2; if (d < b) b = d; } return Math.sqrt(b); };
const objs = JSON.parse(fs.readFileSync(recipeDir + '/objects.json', 'utf8')).objects;
const inRecipe = new Set(objs.map(o => o.n + '|' + o.p.map(v => Math.round(v)).join(',')));

const SKIP = /^lod|^sm_|vbg_|redwood|_fir|fir_|copse|cedar|elm|oak|pine|cypress|scrub|tree|veg_|vegas?palm|palm|bush|plant|shrub|flower|lamp|light|sign|pole|bin|bench|hydrant|trash|dyn_|cj_|tag_|billb|bboard|wire(?!stown)|traincross|cone|barrel|crate|hedge|grass|fern|rock|stone|boulder|dumpst|phone|vending|mailbox|parkingm|bollard|fire|sprink|ladder|pipe|vent|aerial|antenna|satell|chimney|crane|tank|silo|sheet/i;
const GROUND = /land|road|grnd|ground|terrain|hill|beach|cliff|desert|dirt|sand|bank|slope|ridge|valley|canyon|quarry|path|flat|floor|cstline|coast|shore|mount|field|edge|spagjun|storm|drain|freeway|flyover|stadt/i;
const RAILPIECE = /trax|trk|trntrk|railroad|rail(?!br|way)|trainstuff|^traintrack/i;
const R = 220;
const B = {};   // powód -> { n, vol, ex: [] }
const add = (k, o, size) => { const e = (B[k] ??= { n: 0, vol: 0, ex: new Map() }); e.n++; e.vol += size[0] * size[1] * size[2]; e.ex.set(o.model, (e.ex.get(o.model) || 0) + 1); (e.big ??= new Map()).set(o.model, Math.max(e.big.get(o.model) || 0, size[0] * size[1] * size[2])); (e.dim ??= new Map()).set(o.model, size.map(v => v.toFixed(0)).join("x")); };
let nearTotal = 0;
for (const o of ipl) {
  if (!o.exterior) continue;
  const d0 = dist(o.x, o.y);
  if (d0 > R + 60) continue;
  const c = col.get(o.model.toLowerCase());
  if (!c) { if (d0 <= R) { nearTotal++; add('0. brak kolizji w grze (nic do wyciągnięcia)', o, [0, 0, 0]); } continue; }
  const size = c.max.map((v, i) => v - c.min[i]);
  const reach = Math.hypot(size[0], size[1]) / 2;
  if (d0 - reach > R) continue;
  nearTotal++;
  const key = o.model + '|' + [o.x, o.z, o.y].map(v => Math.round(v)).join(',');
  let reason;
  if (SKIP.test(o.model) && !/bridge|tunn/i.test(o.model)) reason = /^sm_|vbg_|redwood|_fir|fir_|copse|cedar|elm|oak|pine|cypress|scrub|tree|veg_|palm|bush|plant|shrub|hedge|grass|fern|flower/i.test(o.model) ? '1. roślinność (osobna lista roślin)' : '2. filtr nazw SKIP (lampy, znaki, słupy, skały, zbiorniki, dźwigi, rury…)';
  else if (/bridge|brij|brid|viaduct|railbr|archbr|cunterb|fredbar/i.test(o.model)) reason = '3. most (w builderze pomijane – tor ma własny most)';
  else if (/tunn/i.test(o.model)) reason = '3. tunel (w builderze pomijane – tor ma własną obudowę)';
  else if (/fence|fnce/i.test(o.model) || (/stat|platform/i.test(o.model) && /train|rail|trn/i.test(o.model))) reason = '7. WZIĘTE jako obiekt';
  else if (RAILPIECE.test(o.model) || GROUND.test(o.model)) reason = (/trailer/i.test(o.model) ? '4b. PRZYCZYNA BŁĘDU: "trailer" zawiera "rail" → wzięte za szynę' : '4. grunt/droga/tor – tylko jako teren (siatka 2 m, widok z góry)');
  else if (Math.max(size[0], size[1]) < 8 || size[2] < 4) reason = '5. za mały/niski (<8 m lub <4 m wysokości)';
  else if (size[0] * size[1] > 20000 && size[2] < 0.25 * Math.max(size[0], size[1])) reason = '6. wielka płaska bryła (uznana za grunt)';
  else reason = '7. WZIĘTE jako obiekt';
  if (reason.startsWith('7') && !inRecipe.has(key) && !objs.some(x => x.n === o.model)) reason = '8. odpadło: stoi na torze lub poza zasięgiem';
  add(reason, o, size);
}
console.log('Obiekty z kolizją w promieniu ' + R + ' m od trasy (łącznie z "brak kolizji"): ' + nearTotal + '\n');
for (const k of Object.keys(B).sort()) {
  const e = B[k]; const top = [...e.ex.entries()].sort((a, b) => b[1] - a[1]).slice(0, 12).map(([n, c]) => n + (c > 1 ? '×' + c : '')).join(', ');
  console.log(k + ': ' + e.n + ' szt.' + (e.vol ? ', obj. łączna ~' + Math.round(e.vol / 1000) + ' tys. m³' : '') + '\n    np. ' + top + '\n');
  if (/^(2|4|5)/.test(k)) console.log('    NAJWIĘKSZE: ' + [...e.big.entries()].sort((a, b) => b[1] - a[1]).slice(0, 16).map(([n, v]) => n + ' (' + e.dim.get(n) + ' m)').join(', ') + '\n');
}
// typ kolizji: ile modeli z kolizją to czyste prymitywy (pudła/kule) bez siatki
let mesh = 0, nomesh = 0, col1 = 0; const nm = [];
const seen = new Set();
for (const o of ipl) {
  if (!o.exterior || dist(o.x, o.y) > R + 60) continue;
  const c = col.get(o.model.toLowerCase()); if (!c || seen.has(o.model)) continue; seen.add(o.model);
  if (c.version === 'COLL') col1++;
  const m = readColMesh(g, c); if (m && m.tris.length) mesh++; else { nomesh++; if (nm.length < 15) nm.push(o.model + ' (' + c.version + ')'); }
}
console.log('Modele (unikalne) przy trasie: z siatką trójkątów ' + mesh + ', BEZ siatki (prymitywy/COL1 – nieczytane) ' + nomesh + ' (z czego COL1: ' + col1 + ')\n    np. ' + nm.join(', '));
