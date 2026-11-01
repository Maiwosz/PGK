// Generuje "przepis" (recipe) trasy misji z danych GTA SA zainstalowanych lokalnie:
//   route.json    – gęsta krzywa toru (układ Unity), flagi most/tunel
//   terrain.json  – metadane siatki wysokości
//   terrain.bin   – wysokości (uint16, skala min..max z terrain.json)
//   terrain_class.bin – klasa powierzchni na wierzchołek (uint8)
//   objects.json  – bryły (pudełka) obiektów przy trasie
//
// Użycie: node build-recipe.mjs "<katalog GTA SA>" "<katalog wyjściowy>"
import fs from 'node:fs';
import path from 'node:path';
import { openGame, loadIde, loadIpl, loadColBounds, readColMesh } from './gta.mjs';

const gameDir = process.argv[2];
const outDir = process.argv[3];
if (!gameDir || !outDir) { console.error('Użycie: node build-recipe.mjs <GTA SA> <wyjście>'); process.exit(1); }
fs.mkdirSync(outDir, { recursive: true });

// ------------------------------------------------------------------ parametry
const ROUTE_FROM = 331;       // indeks węzła w tracks.dat (Unity Station ≈ 330), jazda w stronę malejących indeksów
const ROUTE_TO = 120;         // za mostem Frederick Bridge (most to węzły ok. 121–128)
const CELL = 2;               // m
const TILE = 1024;            // komórek na kafelek terenu (Unity: heightmapResolution 1025)
const MIN_X = 1400, MIN_Y = -2300;
const SAMPLES_X = TILE + 1, SAMPLES_Y = 2 * TILE + 1;
const OBJECT_RADIUS = 220;    // m od toru
const SEA_FLOOR = -8;         // m – dno tam, gdzie nie ma danych o gruncie (morze)
const CARVE_HALF = 7, CARVE_BLEND = 14;   // szerokość podłoża toru i strefa wygładzenia

const g = openGame(gameDir);
const ide = loadIde(g), ipl = loadIpl(g, ide), col = loadColBounds(g);

// ------------------------------------------------------------------ trasa
const all = fs.readFileSync(path.join(gameDir, 'data/Paths/tracks.dat'), 'latin1').trim().split(/\r?\n/).slice(1)
  .map(l => l.trim().split(/\s+/).map(Number));
const raw = [];
for (let i = ROUTE_FROM; i >= ROUTE_TO; i--) raw.push({ x: all[i][0], y: all[i][1], z: all[i][2] });

// Catmull-Rom (centripetalny) -> punkty co ~2 m
function catmull(p0, p1, p2, p3, t) {
  const f = k => {
    const a = p0[k], b = p1[k], c = p2[k], d = p3[k];
    return 0.5 * ((2 * b) + (-a + c) * t + (2 * a - 5 * b + 4 * c - d) * t * t + (-a + 3 * b - 3 * c + d) * t * t * t);
  };
  return { x: f('x'), y: f('y'), z: f('z') };
}
const dense = [];
for (let i = 0; i < raw.length - 1; i++) {
  const p0 = raw[Math.max(0, i - 1)], p1 = raw[i], p2 = raw[i + 1], p3 = raw[Math.min(raw.length - 1, i + 2)];
  const len = Math.hypot(p2.x - p1.x, p2.y - p1.y);
  const n = Math.max(1, Math.round(len / 2));
  for (let s = 0; s < n; s++) dense.push(catmull(p0, p1, p2, p3, s / n));
}
dense.push({ ...raw[raw.length - 1] });
// ponowne próbkowanie równomierne co 2 m
const route = [dense[0]];
{
  let acc = 0;
  for (let i = 1; i < dense.length; i++) {
    let a = dense[i - 1], b = dense[i];
    let seg = Math.hypot(b.x - a.x, b.y - a.y, b.z - a.z);
    while (acc + seg >= 2) {
      const t = (2 - acc) / seg;
      const p = { x: a.x + (b.x - a.x) * t, y: a.y + (b.y - a.y) * t, z: a.z + (b.z - a.z) * t };
      route.push(p); a = p; seg = Math.hypot(b.x - a.x, b.y - a.y, b.z - a.z); acc = 0;
    }
    acc += seg;
  }
}
let routeLen = 0;
for (let i = 1; i < route.length; i++) routeLen += Math.hypot(route[i].x - route[i - 1].x, route[i].y - route[i - 1].y);
console.log('trasa: węzłów', raw.length, 'punktów co 2 m', route.length, 'długość', (routeLen / 1000).toFixed(2), 'km');

function distToRoute(x, y) {
  let best = 1e18, bi = 0;
  for (let i = 0; i < route.length; i += 1) {
    const d = (route[i].x - x) ** 2 + (route[i].y - y) ** 2;
    if (d < best) { best = d; bi = i; }
  }
  return { d: Math.sqrt(best), i: bi };
}

// ------------------------------------------------------------------ obiekty przy trasie
function qEff(o) { return { x: -o.qx, y: -o.qy, z: -o.qz, w: o.qw }; }   // konwencja IPL SA: odwrócony kwaternion
function rotv(q, x, y, z) {
  const tx = 2 * (q.y * z - q.z * y), ty = 2 * (q.z * x - q.x * z), tz = 2 * (q.x * y - q.y * x);
  return [x + q.w * tx + (q.y * tz - q.z * ty), y + q.w * ty + (q.z * tx - q.x * tz), z + q.w * tz + (q.x * ty - q.y * tx)];
}

const SKIP = /^lod|^sm_|vbg_|redwood|_fir|fir_|copse|cedar|elm|oak|pine|cypress|scrub|tree|veg_|vegas?palm|palm|bush|plant|shrub|flower|lamp|light|sign|pole|bin|bench|hydrant|trash|dyn_|cj_|tag_|billb|bboard|wire(?!stown)|traincross|cone|barrel|crate|hedge|grass|fern|rock|stone|boulder|dumpst|phone|vending|mailbox|parkingm|bollard|fire|sprink|ladder|pipe|vent|aerial|antenna|satell|chimney|crane|tank|silo|sheet/i;
const GROUND = /land|road|grnd|ground|terrain|hill|beach|cliff|desert|dirt|sand|bank|slope|ridge|valley|canyon|quarry|path|flat|floor|cstline|coast|shore|mount|field|edge|spagjun|storm|drain|freeway|flyover|stadt/i;
const RAILPIECE = /trax|trk|trntrk|railroad|rail(?!br|way)|trainstuff|^traintrack/i;
function categorize(name) {
  if (/bridge|brij|brid|viaduct|railbr|archbr|cunterb|fredbar/i.test(name)) return 'bridge';
  if (/tunn/i.test(name)) return 'tunnel';
  if (/stat|platform/i.test(name) && /train|rail|trn/i.test(name)) return 'station';
  if (/fence|fnce/i.test(name)) return 'fence';
  if (RAILPIECE.test(name) || GROUND.test(name)) return null;
  return 'building';
}

const isGroundMat = id => id >= 1 && id <= 40;
const meshCache = new Map();
function getMesh(name) {
  if (meshCache.has(name)) return meshCache.get(name);
  const c = col.get(name.toLowerCase());
  let res = null;
  if (c) {
    const m = readColMesh(g, c);
    if (m) {
      let area = 0, ground = 0;
      for (let i = 0; i < m.mats.length; i++) {
        const a = m.tris[i * 3] * 3, b = m.tris[i * 3 + 1] * 3, cc = m.tris[i * 3 + 2] * 3;
        const ux = m.verts[b] - m.verts[a], uy = m.verts[b + 1] - m.verts[a + 1], uz = m.verts[b + 2] - m.verts[a + 2];
        const vx = m.verts[cc] - m.verts[a], vy = m.verts[cc + 1] - m.verts[a + 1], vz = m.verts[cc + 2] - m.verts[a + 2];
        const nx = uy * vz - uz * vy, ny = uz * vx - ux * vz, nz = ux * vy - uy * vx;
        const ar = Math.hypot(nx, ny, nz) / 2;
        area += ar;
        if (isGroundMat(m.mats[i]) && Math.abs(nz) / (2 * ar + 1e-9) > 0.2) ground += ar;
      }
      res = { m, groundFrac: area > 0 ? ground / area : 0, size: c.max.map((v, i) => v - c.min[i]) };
    }
  }
  meshCache.set(name, res);
  return res;
}

/**
 * Rozkłada siatkę kolizji na prostopadłościany: rysujemy trójkąty z góry na siatkę, dla każdej kolumny bierzemy
 * najniższy i najwyższy punkt, a sąsiednie kolumny o tej samej wysokości łączymy w większe pudełka.
 * Zwraca płaską tablicę [cx,cy,cz,sx,sy,sz, ...] w lokalnym układzie Unity (x, wysokość, z) względem początku modelu.
 */
function decompose(mesh, bbMin, bbMax, cell) {
  const { verts, tris } = mesh;
  for (let attempt = 0; attempt < 4; attempt++, cell *= 1.7) {
    const nx = Math.max(1, Math.ceil((bbMax[0] - bbMin[0]) / cell)), ny = Math.max(1, Math.ceil((bbMax[1] - bbMin[1]) / cell));
    const top = new Float32Array(nx * ny).fill(-Infinity), bot = new Float32Array(nx * ny).fill(Infinity);
    const mark = (x, y, z) => {
      const ix = Math.min(nx - 1, Math.max(0, Math.floor((x - bbMin[0]) / cell))), iy = Math.min(ny - 1, Math.max(0, Math.floor((y - bbMin[1]) / cell)));
      const k = iy * nx + ix; if (z > top[k]) top[k] = z; if (z < bot[k]) bot[k] = z;
    };
    for (let t = 0; t < tris.length; t += 3) {
      const a = tris[t] * 3, b = tris[t + 1] * 3, c = tris[t + 2] * 3;
      const edge = Math.max(Math.hypot(verts[b] - verts[a], verts[b + 1] - verts[a + 1]), Math.hypot(verts[c] - verts[a], verts[c + 1] - verts[a + 1]), Math.hypot(verts[c] - verts[b], verts[c + 1] - verts[b + 1]));
      const n = Math.min(60, Math.max(1, Math.ceil(edge / (cell * 0.6))));
      for (let i = 0; i <= n; i++) for (let j = 0; j <= n - i; j++) {
        const u = i / n, v = j / n, w = 1 - u - v;
        mark(w * verts[a] + u * verts[b] + v * verts[c], w * verts[a + 1] + u * verts[b + 1] + v * verts[c + 1], w * verts[a + 2] + u * verts[b + 2] + v * verts[c + 2]);
      }
    }
    // klucze wysokości (zaokrąglone do 1 m) i łączenie prostokątów
    const key = new Int32Array(nx * ny).fill(-1);
    const hs = new Map();
    for (let k = 0; k < nx * ny; k++) if (top[k] > -Infinity) {
      const kt = Math.round(top[k]), kb = Math.round(bot[k]);
      const id = kt * 4096 + (kb + 2048); if (!hs.has(id)) hs.set(id, { top: kt, bot: kb }); key[k] = id;
    }
    const used = new Uint8Array(nx * ny); const boxes = [];
    for (let y = 0; y < ny; y++) for (let x = 0; x < nx; x++) {
      const k = y * nx + x; if (key[k] < 0 || used[k]) continue;
      let w = 1; while (x + w < nx && key[k + w] === key[k] && !used[k + w]) w++;
      let h = 1; outer: while (y + h < ny) { for (let i = 0; i < w; i++) { const kk = (y + h) * nx + x + i; if (key[kk] !== key[k] || used[kk]) break outer; } h++; }
      for (let yy = 0; yy < h; yy++) for (let xx = 0; xx < w; xx++) used[(y + yy) * nx + x + xx] = 1;
      const hv = hs.get(key[k]);
      const x0 = bbMin[0] + x * cell, x1 = Math.min(bbMax[0], bbMin[0] + (x + w) * cell), y0 = bbMin[1] + y * cell, y1 = Math.min(bbMax[1], bbMin[1] + (y + h) * cell);
      const zt = hv.top, zb = Math.min(hv.bot, hv.top - 0.5);
      boxes.push([(x0 + x1) / 2, (zt + zb) / 2, (y0 + y1) / 2, x1 - x0, zt - zb, y1 - y0]);   // Unity lokalnie: (x, z->wysokość, y->głębokość)
    }
    if (boxes.length <= 40 || attempt === 3) return boxes.slice(0, 60).flat().map(v => +v.toFixed(2));
  }
  return null;
}

const objects = [];
for (const o of ipl) {
  if (o.interior !== 0) continue;
  const c = col.get(o.model.toLowerCase());
  if (!c) continue;
  if (SKIP.test(o.model) && !/bridge|tunn/i.test(o.model)) continue;
  const cat = categorize(o.model);
  if (!cat) continue;
  // szybkie odrzucenie: odległość od krzywej toru (pozycja obiektu)
  if (o.x < MIN_X - 200 || o.x > MIN_X + CELL * TILE + 200 || o.y < MIN_Y - 200 || o.y > MIN_Y + CELL * 2 * TILE + 200) continue;
  const size = c.max.map((v, i) => v - c.min[i]);
  const center = c.max.map((v, i) => (v + c.min[i]) / 2);
  if (cat === 'building' && (Math.max(size[0], size[1]) < 8 || size[2] < 4)) continue;
  // wielkie płaskie bryły to kawałki gruntu / placów, nie budynki
  if (cat === 'building' && size[0] * size[1] > 20000 && size[2] < 0.25 * Math.max(size[0], size[1])) continue;
  const q = qEff(o);
  const r = rotv(q, center[0], center[1], center[2]);
  const wx = o.x + r[0], wy = o.y + r[1], wz = o.z + r[2];
  const reach = Math.hypot(size[0], size[1]) / 2;
  const dd = distToRoute(wx, wy).d - reach;
  if (dd > OBJECT_RADIUS) continue;
  let parts = null;
  if (cat === 'building' || cat === 'fence' || cat === 'station') {
    const mm = getMesh(o.model);
    if (mm) parts = decompose(mm.m, c.min, c.max, 4);
  }
  // układ Unity: (x, z, y); kwaternion: (qx, qz, qy, qw) po odwróceniu konwencji IPL
  objects.push({
    ...(parts && parts.length >= 6 ? { o: [+o.x.toFixed(2), +o.z.toFixed(2), +o.y.toFixed(2)], b: parts } : {}),
    n: o.model, c: cat,
    p: [+wx.toFixed(2), +wz.toFixed(2), +wy.toFixed(2)],
    s: [+size[0].toFixed(2), +size[2].toFixed(2), +size[1].toFixed(2)],
    q: [+o.qx.toFixed(5), +o.qz.toFixed(5), +o.qy.toFixed(5), +o.qw.toFixed(5)],
  });
}
const catCount = {};
for (const o of objects) catCount[o.c] = (catCount[o.c] || 0) + 1;
console.log('obiekty przy trasie:', objects.length, catCount);

// ------------------------------------------------------------------ rośliny
// Pojedyncze drzewa/krzaki/palmy z mapy gry + rozsypane grupy drzew (jedno pudełko kolizji = las).
// Wynik: płaska tablica po 5 liczb: typ, x, z (Unity), wysokość, promień korony.
// Typy: 0 liściaste, 1 iglaste, 2 palma, 3 krzak/kaktus.
const plants = [];
{
  const VEG = /^sm_|vbg_|veg|palm|bush|tree|cedar|elm|oak|pine|fir|redwood|cypress|scrub|shrub|hedge|plant|copse|bevhil|fern|cactus|cact|agave|joshua|sapling|cottonwood/i;
  const NOT_VEG = /petrol|lamp|sign|pump|light|pole|^lod|bench|bin|fence|stand/i;
  const GROUP = /copse|grp|group|firtbshg|vegvbbig|scabt|^vbg_/i;
  const CONIFER = /fir|pine|redwood|cedar|cypress/i;
  const PALM = /palm/i;
  const BUSH = /bush|shrub|hedge|agave|cact|scrub|fern|plant/i;
  const rng = (seed => () => (seed = (seed * 1664525 + 1013904223) >>> 0) / 4294967296);
  const R_NEAR = 300, R_CLEAR = 9;
  let singles = 0, groups = 0;
  const dist = (x, y) => { let best = 1e18; for (let i = 0; i < route.length; i += 3) { const d = (route[i].x - x) ** 2 + (route[i].y - y) ** 2; if (d < best) best = d; } return Math.sqrt(best); };
  for (const o of ipl) {
    if (o.interior !== 0 || NOT_VEG.test(o.model) || !VEG.test(o.model)) continue;
    const c = col.get(o.model.toLowerCase());
    if (!c) continue;
    const size = c.max.map((v, i) => v - c.min[i]);
    const dd = dist(o.x, o.y);
    if (dd > R_NEAR + Math.hypot(size[0], size[1]) / 2) continue;
    const isGroup = GROUP.test(o.model) && Math.max(size[0], size[1]) > 20;
    const type = PALM.test(o.model) ? 2 : CONIFER.test(o.model) ? 1 : BUSH.test(o.model) ? 3 : 0;
    if (!isGroup) {
      if (dd < R_CLEAR) continue;
      let h = size[2], r = Math.max(size[0], size[1]) / 2;
      if (type === 2) h = Math.min(h * 0.7, 22);
      else if (type === 3) { h = Math.max(0.6, Math.min(h, 4)); r = Math.max(0.5, Math.min(r, 3)); }
      else { h = Math.min(Math.max(h, 4), 30); r = Math.max(1.5, Math.min(r * 0.7, 9)); }
      plants.push([type, +o.x.toFixed(1), +o.y.toFixed(1), +h.toFixed(1), +r.toFixed(1)]);
      singles++;
      continue;
    }
    // grupa: rozsypujemy drzewa w obrysie pudełka (obrócone kwaternionem obiektu)
    const q = qEff(o);
    const cx = (c.max[0] + c.min[0]) / 2, cy = (c.max[1] + c.min[1]) / 2;
    const n = Math.max(4, Math.min(30, Math.round(size[0] * size[1] / 110)));
    const rand = rng(Math.floor(o.x * 7 + o.y * 13) >>> 0);
    const h0 = Math.min(size[2], 30) * 0.8;
    for (let k = 0; k < n; k++) {
      const lx = cx + (rand() - 0.5) * size[0] * 0.9, ly = cy + (rand() - 0.5) * size[1] * 0.9;
      const r = rotv(q, lx, ly, 0);
      const wx = o.x + r[0], wy = o.y + r[1];
      if (dist(wx, wy) < R_CLEAR) continue;
      const h = h0 * (0.6 + rand() * 0.5);
      plants.push([type === 3 ? 0 : type, +wx.toFixed(1), +wy.toFixed(1), +h.toFixed(1), +(h * (type === 1 ? 0.22 : 0.3)).toFixed(1)]);
    }
    groups++;
  }
  console.log('rośliny:', plants.length, '(pojedyncze', singles, ', grup', groups + ')');
}

// flagi mostu/tunelu dla punktów trasy (test w obrysie orientowanego pudełka)
const kinds = new Uint8Array(route.length);
for (const o of objects) {
  if (o.c !== 'bridge' && o.c !== 'tunnel') continue;
  const kind = o.c === 'bridge' ? 1 : 2;
  // odwrotność kwaternionu Unity: ten sam układ lewoskrętny, więc używamy lokalnej osi przez kwaternion sprzężony
  const [qx, qy, qz, qw] = o.q;
  // kwaternion Unity = (qx,qz,qy,qw)->  w node trzymamy już zamienione (x,z,y); obrót wokół Y (Unity)
  const u = { x: qx, y: qy, z: qz, w: qw };
  const inv = { x: -u.x, y: -u.y, z: -u.z, w: u.w };
  for (let i = 0; i < route.length; i++) {
    const dx = route[i].x - o.p[0], dy = route[i].z - o.p[1], dz = route[i].y - o.p[2];
    const l = rotv(inv, dx, dy, dz);
    if (Math.abs(l[0]) <= o.s[0] / 2 + 3 && Math.abs(l[2]) <= o.s[2] / 2 + 3 && Math.abs(l[1]) <= o.s[1] / 2 + 10) kinds[i] = Math.max(kinds[i], kind);
  }
}
// obiekty stojące na torze (zwykłe budynki/ogrodzenia) usuwamy – tor ma przejazd
{
  const before = objects.length;
  for (let k = objects.length - 1; k >= 0; k--) {
    const o = objects[k];
    if (o.c === 'bridge' || o.c === 'tunnel') continue;
    const [qx, qy, qz, qw] = o.q;
    const inv = { x: -qx, y: -qy, z: -qz, w: qw };
    let hit = false;
    const org = o.o || o.p;
    for (let i = 0; i < route.length && !hit; i++) {
      const dx = route[i].x - org[0], dy = route[i].z - org[1], dz = route[i].y - org[2];
      if (dx * dx + dz * dz > 150 * 150) continue;
      const l = rotv(inv, dx, dy, dz);
      if (o.b) {
        for (let k = 0; k < o.b.length && !hit; k += 6)
          if (Math.abs(l[0] - o.b[k]) <= o.b[k + 3] / 2 + 4 && Math.abs(l[2] - o.b[k + 2]) <= o.b[k + 5] / 2 + 4 && Math.abs(l[1] - o.b[k + 1]) <= o.b[k + 4] / 2 + 6) hit = true;
      } else {
        const pl = rotv(inv, route[i].x - o.p[0], route[i].z - o.p[1], route[i].y - o.p[2]);
        if (Math.abs(pl[0]) <= o.s[0] / 2 + 4 && Math.abs(pl[2]) <= o.s[2] / 2 + 4 && Math.abs(pl[1]) <= o.s[1] / 2 + 6) hit = true;
      }
    }
    if (hit) objects.splice(k, 1);
  }
  console.log('usunięto obiektów nachodzących na tor:', before - objects.length);
}
let nb = 0, nt = 0; for (const k of kinds) { if (k === 1) nb++; if (k === 2) nt++; }
console.log('punkty na moście', nb, '(', nb * 2, 'm ), w tunelu', nt, '(', nt * 2, 'm )');

// ext-tunnel: obudowa tunelu sięga 24 m poza wykryty tunel – portal ma wejść w zbocze, a nie w wykop
{
  const orig = Uint8Array.from(kinds);
  for (let i = 0; i < route.length; i++) if (orig[i] === 2)
    for (let j = Math.max(0, i - 12); j <= Math.min(route.length - 1, i + 12); j++) if (kinds[j] === 0) kinds[j] = 2;
  let c = 0; for (const k of kinds) if (k === 2) c++;
  console.log('tunel po wydłużeniu: ' + c * 2 + ' m');
}

// ------------------------------------------------------------------ teren
const GROUND_NAME = /land|road|grnd|ground|terrain|hill|beach|cliff|rock|desert|dirt|grass|sand|bank|slope|ridge|valley|canyon|quarry|path|flat|floor|cstline|coast|shore|mount|field|edge/i;
const EXCLUDE_TERRAIN = /bridge|brij|brid|viaduct|tunn|rail|trax|train|overpass|ramp|^lod|storm|drain|flyover|freeway|spag|pillar|fence|stadt|sign|pole|wire(?!stown)|tree|veg_|lamp|light|tower|crane/i;
function surfClass(m) {
  if ([1, 2, 3, 4, 5, 7, 8, 34].includes(m)) return 1;       // asfalt / beton / chodnik
  if ((m >= 9 && m <= 17) || m === 19 || m === 20 || m === 23 || m === 40) return 2;   // trawa
  if (m === 6 || m === 21 || m === 22 || (m >= 24 && m <= 27)) return 3;   // żwir / ziemia / błoto
  if (m >= 28 && m <= 33) return 4;                              // piasek
  if (m === 18 || (m >= 35 && m <= 37)) return 5;                // skała
  return 0;
}


const height = new Float32Array(SAMPLES_X * SAMPLES_Y).fill(-Infinity);
const cls = new Uint8Array(SAMPLES_X * SAMPLES_Y);
const MAX_X = MIN_X + TILE * CELL, MAX_Y = MIN_Y + 2 * TILE * CELL;

let used = 0;
for (const o of ipl) {
  if (o.interior !== 0) continue;
  if (o.x < MIN_X - 300 || o.x > MAX_X + 300 || o.y < MIN_Y - 300 || o.y > MAX_Y + 300) continue;
  if (EXCLUDE_TERRAIN.test(o.model)) continue;
  const mm = getMesh(o.model);
  if (!mm) continue;
  let isGround = GROUND_NAME.test(o.model);
  if (!isGround) {
    const [w, d, h] = mm.size;
    if (w * d >= 2500 && h <= 0.2 * Math.max(w, d)) isGround = true;
    else if (mm.groundFrac >= 0.5) isGround = true;
  }
  if (!isGround) continue;
  used++;
  const q = qEff(o);
  const { verts, tris: T, mats } = mm.m;
  const wv = new Float32Array(verts.length);
  for (let i = 0; i < verts.length; i += 3) {
    const r = rotv(q, verts[i], verts[i + 1], verts[i + 2]);
    wv[i] = r[0] + o.x; wv[i + 1] = r[1] + o.y; wv[i + 2] = r[2] + o.z;
  }
  for (let t = 0; t < mats.length; t++) {
    const mat = mats[t];
    if (mat >= 38 && mat <= 39) continue;   // dno rzek/płytka woda
    const a = T[t * 3] * 3, b = T[t * 3 + 1] * 3, c = T[t * 3 + 2] * 3;
    const ax = wv[a], ay = wv[a + 1], az = wv[a + 2], bx = wv[b], by = wv[b + 1], bz = wv[b + 2], cx = wv[c], cy = wv[c + 1], cz = wv[c + 2];
    const det = (by - cy) * (ax - cx) + (cx - bx) * (ay - cy);
    if (Math.abs(det) < 1e-6) continue;
    const nz = (bx - ax) * (cy - ay) - (by - ay) * (cx - ax);
    const ex = Math.hypot(bx - ax, by - ay, bz - az), ey = Math.hypot(cx - ax, cy - ay, cz - az);
    if (Math.abs(nz) < 0.3 * ex * ey * 0.5) continue;
    const x0 = Math.max(0, Math.ceil((Math.min(ax, bx, cx) - MIN_X) / CELL)), x1 = Math.min(SAMPLES_X - 1, Math.floor((Math.max(ax, bx, cx) - MIN_X) / CELL));
    const y0 = Math.max(0, Math.ceil((Math.min(ay, by, cy) - MIN_Y) / CELL)), y1 = Math.min(SAMPLES_Y - 1, Math.floor((Math.max(ay, by, cy) - MIN_Y) / CELL));
    const cl = surfClass(mat);
    for (let gy = y0; gy <= y1; gy++) {
      const py = MIN_Y + gy * CELL;
      for (let gx = x0; gx <= x1; gx++) {
        const px = MIN_X + gx * CELL;
        const l1 = ((by - cy) * (px - cx) + (cx - bx) * (py - cy)) / det;
        const l2 = ((cy - ay) * (px - cx) + (ax - cx) * (py - cy)) / det;
        const l3 = 1 - l1 - l2;
        if (l1 < -1e-4 || l2 < -1e-4 || l3 < -1e-4) continue;
        const z = l1 * az + l2 * bz + l3 * cz;
        const idx = gy * SAMPLES_X + gx;
        if (z > height[idx]) { height[idx] = z; cls[idx] = cl; }
      }
    }
  }
}
let covered = 0;
for (let i = 0; i < height.length; i++) if (height[i] > -Infinity) covered++;
console.log('teren: instancji', used, 'pokrycie', (100 * covered / height.length).toFixed(1) + '%');

const hadData = new Uint8Array(height.length);
for (let i = 0; i < height.length; i++) if (height[i] > -Infinity) hadData[i] = 1;
{
  // odsetek komórek bez danych z gry w promieniu 60 m od punktów trasy
  const R = 30; let runs = [], cur = null;
  for (let i = 0; i < route.length; i += 5) {
    const p = route[i]; let tot = 0, miss = 0;
    for (let gy = Math.floor((p.y - 60 - MIN_Y) / CELL); gy <= Math.ceil((p.y + 60 - MIN_Y) / CELL); gy += 2)
      for (let gx = Math.floor((p.x - 60 - MIN_X) / CELL); gx <= Math.ceil((p.x + 60 - MIN_X) / CELL); gx += 2) {
        if (gx < 0 || gy < 0 || gx >= SAMPLES_X || gy >= SAMPLES_Y) continue;
        tot++; if (!hadData[gy * SAMPLES_X + gx]) miss++;
      }
    const frac = tot ? miss / tot : 0;
    if (frac > 0.4) { if (!cur || i - cur.end > 15) { cur = { s: i, end: i, max: frac }; runs.push(cur); } else { cur.end = i; cur.max = Math.max(cur.max, frac); } }
  }
  console.log('odcinki trasy z >40% brakujących danych terenu w promieniu 60 m:', runs.length, 'łącznie m:', runs.reduce((a, r) => a + (r.end - r.s + 5) * 2, 0));
  for (const r of runs) console.log('  idx', r.s, '-', r.end, 'max brak', (r.max * 100).toFixed(0) + '%', '@', route[r.s].x.toFixed(0), route[r.s].y.toFixed(0));
}
// wypełnianie dziur: fala od pokrytych komórek; daleko od gruntu -> dno morskie
{
  const NX = SAMPLES_X, NY = SAMPLES_Y;
  const dist = new Int16Array(NX * NY).fill(-1);
  let frontier = [];
  for (let i = 0; i < height.length; i++) if (height[i] > -Infinity) { dist[i] = 0; frontier.push(i); }
  const sum = new Float32Array(NX * NY), cnt = new Uint8Array(NX * NY), clsv = new Uint8Array(NX * NY);
  let level = 0;
  while (frontier.length && level < 200) {
    level++;
    const next = [];
    for (const i of frontier) {
      const x = i % NX, y = (i / NX) | 0;
      for (const [dx, dy] of [[1, 0], [-1, 0], [0, 1], [0, -1]]) {
        const xx = x + dx, yy = y + dy;
        if (xx < 0 || yy < 0 || xx >= NX || yy >= NY) continue;
        const j = yy * NX + xx;
        if (dist[j] === 0 || (dist[j] > 0 && dist[j] < level)) continue;
        if (dist[j] === -1) { dist[j] = level; next.push(j); }
        sum[j] += height[i]; cnt[j]++; clsv[j] = clsv[j] || cls[i];
      }
    }
    for (const j of next) { height[j] = sum[j] / cnt[j]; cls[j] = height[j] > 1.5 ? 2 : 4; }   // dziury: trawa nad wodą, piasek przy brzegu
    // komórki dopiero co wypełnione stają się źródłem dla następnej fali (dist > 0 – nie liczą się jako "pewne")
    frontier = next;
  }
  for (let i = 0; i < height.length; i++) {
    if (dist[i] === -1) { height[i] = SEA_FLOOR; cls[i] = 4; }
    else if (dist[i] > 0) {
      const t = Math.max(0, Math.min(1, (dist[i] - 10) / 50));
      height[i] = height[i] * (1 - t) + SEA_FLOOR * t;
    }
  }
}

const origHeight = Float32Array.from(height);
// korytarz toru: wyrównanie podłoża do wysokości toru.
//  • zwykły odcinek / tunel: podłoże dokładnie pod podsypką (nasyp albo wykop),
//  • most: teren tylko obniżamy tak, by nie wystawał ponad spód płyty mostu (przekop przez wzgórze).
{
  const R = CARVE_HALF + CARVE_BLEND;
  let carved = 0;
  for (let i = 0; i < route.length; i++) {
    if (kinds[i] === 2) continue;      // tunel: teren nad nim zostaje (przywrócony niżej)
    const bridge = kinds[i] === 1;
    const p = route[i];
    const target = bridge ? p.z - 1.8 : p.z - 0.4;
    const gx0 = Math.max(0, Math.floor((p.x - R - MIN_X) / CELL)), gx1 = Math.min(SAMPLES_X - 1, Math.ceil((p.x + R - MIN_X) / CELL));
    const gy0 = Math.max(0, Math.floor((p.y - R - MIN_Y) / CELL)), gy1 = Math.min(SAMPLES_Y - 1, Math.ceil((p.y + R - MIN_Y) / CELL));
    for (let gy = gy0; gy <= gy1; gy++) for (let gx = gx0; gx <= gx1; gx++) {
      const d = Math.hypot(MIN_X + gx * CELL - p.x, MIN_Y + gy * CELL - p.y);
      if (d > R) continue;
      const idx = gy * SAMPLES_X + gx;
      let w;
      if (d <= CARVE_HALF) w = 1; else { const t = (d - CARVE_HALF) / CARVE_BLEND; w = 1 - t * t * (3 - 2 * t); }
      const nh = height[idx] * (1 - w) + target * w;
      if (bridge) {
        if (nh < height[idx]) { height[idx] = nh; carved++; }       // most: tylko obniżamy
      } else if (Math.abs(nh - target) < Math.abs(height[idx] - target)) { height[idx] = nh; carved++; }
      if (d <= CARVE_HALF && !bridge) cls[idx] = 3;
    }
  }
  // Nad tunelem teren wraca do oryginalnej wysokości (wzgórze), a portal powstaje na granicy wykopu i tunelu.
  {
    let restored = 0;
    for (let i = 0; i < route.length; i++) {
      if (kinds[i] !== 2) continue;
      const p = route[i];
      const gx0 = Math.max(0, Math.floor((p.x - R - MIN_X) / CELL)), gx1 = Math.min(SAMPLES_X - 1, Math.ceil((p.x + R - MIN_X) / CELL));
      const gy0 = Math.max(0, Math.floor((p.y - R - MIN_Y) / CELL)), gy1 = Math.min(SAMPLES_Y - 1, Math.ceil((p.y + R - MIN_Y) / CELL));
      for (let gy = gy0; gy <= gy1; gy++) for (let gx = gx0; gx <= gx1; gx++) {
        const wx = MIN_X + gx * CELL, wy = MIN_Y + gy * CELL;
        if (Math.hypot(wx - p.x, wy - p.y) > R) continue;
        // najbliższy punkt trasy (szukamy w oknie indeksów wokół i)
        let best = 1e18, bi = i;
        for (let j = Math.max(0, i - 40); j <= Math.min(route.length - 1, i + 40); j++) {
          const d = (route[j].x - wx) ** 2 + (route[j].y - wy) ** 2;
          if (d < best) { best = d; bi = j; }
        }
        if (kinds[bi] !== 2) continue;
        const idx = gy * SAMPLES_X + gx;
        if (height[idx] !== origHeight[idx]) { height[idx] = origHeight[idx]; restored++; }
      }
    }
    console.log('przywrócono teren nad tunelem:', restored, 'wierzchołków');
  }
  // Ostateczne zabezpieczenie: w pasie ±5 m od osi teren nigdy nie wystaje ponad tor.
  let clamped = 0;
  for (let i = 0; i < route.length; i++) {
    if (kinds[i] === 2) continue;
    const p = route[i];
    const ceil = kinds[i] === 1 ? p.z - 1.8 : p.z - 0.4;
    const gx0 = Math.max(0, Math.floor((p.x - 5 - MIN_X) / CELL)), gx1 = Math.min(SAMPLES_X - 1, Math.ceil((p.x + 5 - MIN_X) / CELL));
    const gy0 = Math.max(0, Math.floor((p.y - 5 - MIN_Y) / CELL)), gy1 = Math.min(SAMPLES_Y - 1, Math.ceil((p.y + 5 - MIN_Y) / CELL));
    for (let gy = gy0; gy <= gy1; gy++) for (let gx = gx0; gx <= gx1; gx++) {
      if (Math.hypot(MIN_X + gx * CELL - p.x, MIN_Y + gy * CELL - p.y) > 5) continue;
      const idx = gy * SAMPLES_X + gx;
      if (height[idx] > ceil) { height[idx] = ceil; clamped++; }
    }
  }
  console.log('wyrównano komórek wzdłuż toru', carved, ', dociśnięto do sufitu', clamped);
}

let zmin = Infinity, zmax = -Infinity;
for (const h of height) { if (h < zmin) zmin = h; if (h > zmax) zmax = h; }
zmin = Math.floor(zmin) - 1; zmax = Math.ceil(zmax) + 1;
const h16 = new Uint16Array(height.length);
for (let i = 0; i < height.length; i++) h16[i] = Math.max(0, Math.min(65535, Math.round((height[i] - zmin) / (zmax - zmin) * 65535)));
console.log('wysokości terenu: min', zmin, 'max', zmax);

// ------------------------------------------------------------------ zapis
const f = n => path.join(outDir, n);
fs.writeFileSync(f('terrain.bin'), Buffer.from(h16.buffer));
fs.writeFileSync(f('terrain_class.bin'), Buffer.from(cls.buffer));
fs.writeFileSync(f('terrain.json'), JSON.stringify({
  minX: MIN_X, minY: MIN_Y, cell: CELL, tile: TILE, samplesX: SAMPLES_X, samplesY: SAMPLES_Y, zMin: zmin, zMax: zmax,
  note: 'Układ GTA: x = wschód, y = północ. Unity: x, z=y. Wiersz 0 = południowa krawędź.',
}));
fs.writeFileSync(f('route.json'), JSON.stringify({
  spacing: 2, lengthM: +routeLen.toFixed(1), from: ROUTE_FROM, to: ROUTE_TO,
  // punkty (x, wysokość, z) w układzie Unity; k = 0 zwykły, 1 most, 2 tunel
  pts: route.flatMap(p => [+p.x.toFixed(2), +p.z.toFixed(2), +p.y.toFixed(2)]),
  kinds: Array.from(kinds),
}));
fs.writeFileSync(f('objects.json'), JSON.stringify({ objects }));
fs.writeFileSync(f('plants.json'), JSON.stringify({ d: plants.flat() }));
for (const n of ['terrain.bin', 'terrain_class.bin', 'route.json', 'objects.json', 'plants.json']) console.log(n, (fs.statSync(f(n)).size / 1024).toFixed(0) + ' KB');
