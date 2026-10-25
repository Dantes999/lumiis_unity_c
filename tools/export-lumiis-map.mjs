// Exports a map of the old Lumiis-2D project (tiles, portals, NPCs, spawns) or the monsters used on it as compact JSON.
// Source: ~/Desktop/lumiis-2d, exported at commit 4376dae. The maps there are not files: buildMap(id) in shared/maps.js
// generates each one deterministically from its seed and town layout, and shared/spawns.js derives the spawns from the
// same seed. This script runs that original code, so the output matches what Lumiis-2D builds.
// Tile ids GRASS..GATE_OPEN = 0..13 are the same as in tools/import-map1.py.
//
// Usage (Node 20+, no dependencies):
//   node tools/export-lumiis-map.mjs map  <mapId> > tools/map2-jayang-source.json
//   node tools/export-lumiis-map.mjs mobs <mapId> > tools/map2-jayang-mobs.json
// LUMIIS_2D=/path/to/lumiis-2d overrides the source folder.

import os from 'node:os';
import path from 'node:path';
import { pathToFileURL } from 'node:url';

const ROOT = process.env.LUMIIS_2D || path.join(os.homedir(), 'Desktop', 'lumiis-2d');
const load = (rel) => import(pathToFileURL(path.join(ROOT, rel)).href);

const [mode, id] = process.argv.slice(2);
if (!['map', 'mobs'].includes(mode) || !id) {
  console.error('usage: node tools/export-lumiis-map.mjs map|mobs <mapId>');
  process.exit(1);
}

const { MAPS, buildMap, makeMobMap, zonesOf } = await load('shared/maps.js');
const { buildSpawns, buildMetinPoints, buildSpecialPoints, spawnSetOf } = await load('shared/spawns.js');
const { METIN_SETS } = await load('shared/metins.js');
const { MOB_TYPES, MOB_INDEX } = await load('shared/mobs.js');
const { npcsFor } = await load('shared/npcs.js');
const { TILE } = await load('shared/constants.js');

const map = buildMap(id);
const mobMap = makeMobMap(map);
const mobRef = (i) => ({ key: MOB_TYPES[i].key, name: MOB_TYPES[i].name, level: MOB_TYPES[i].level });

const groups = buildSpawns(map, mobMap, 1);
const metinDefs = METIN_SETS[map.zoneSet] || METIN_SETS.map1;
const metinPoints = buildMetinPoints(map, mobMap, metinDefs, groups);
const specialDefs = spawnSetOf(map)?.specials || [];
const avoid = [];
for (const pts of metinPoints.values()) for (const p of pts) avoid.push(p);
Object.defineProperty(avoid, 'gone', { value: metinPoints.gone || [] });
const specialPoints = buildSpecialPoints(map, mobMap, specialDefs, groups, avoid);

if (mode === 'map') {
  const out = {
    id: MAPS[id].tier === 2 ? 'map2' : id,
    sourceMapId: id,
    name: MAPS[id].name,
    source: `lumiis-2d@4376dae shared/maps.js buildMap("${id}") (seed ${map.seed}), full map`,
    x0: 0, y0: 0, w: map.w, h: map.h,
    tileIds: 'GRASS..GATE_OPEN = 0..13',
    village: map.village ?? null,
    safe: map.safe,
    camp: map.camp ?? null,
    zones: zonesOf(map).map((z, i) => ({ index: i, name: z.name, lv: z.lv })),
    portals: map.portals,
    npcs: npcsFor(map).map((n) => ({ type: n.type, tx: Math.floor(n.x / TILE), ty: Math.floor((n.y - 4) / TILE) })),
    spawns: {
      groups: groups.map((g) => ({ tx: g.tx, ty: g.ty, zone: g.zone, respawnMs: g.respawnMs, members: g.members.map(mobRef) })),
      metins: metinDefs.map((d) => ({
        ...mobRef(d.type), zone: d.zone, count: d.count, respawnMs: d.respawnMs,
        points: (metinPoints.get(d.type) || []).map((p) => [p.tx, p.ty]),
      })),
      specials: specialDefs.map((d) => ({
        key: d.key, zone: d.zone, count: d.count, respawnMs: d.respawnMs, members: d.members.map(mobRef),
        points: (specialPoints.get(d.key) || []).map((p) => [p.tx, p.ty]),
      })),
    },
    tiles: Array.from(map.tiles),
  };
  process.stdout.write(JSON.stringify(out));
} else {
  const { DROP, BOSS_LOOT, MOB_LOOT, MAP2_MATS } = await load('server/drops.js');
  const keyOf = (i) => MOB_TYPES[i].key;
  const used = new Set();
  for (const g of groups) for (const i of g.members) used.add(i);
  const metins = metinDefs.map((d) => {
    used.add(d.type);
    for (const i of d.guards || []) used.add(i);
    for (const w of d.waves || []) for (const i of w) used.add(i);
    return {
      key: keyOf(d.type), zone: d.zone, count: d.count, points: d.points, respawnMs: d.respawnMs,
      guards: (d.guards || []).map(keyOf), waveAt: d.waveAt ?? null, waves: (d.waves || []).map((w) => w.map(keyOf)), drops: d.drops,
    };
  });
  for (const d of specialDefs) for (const i of d.members) used.add(i);
  const mobs = [...used].sort((a, b) => a - b).map((i) => {
    const t = MOB_TYPES[i];
    const drops = {};
    if (Object.hasOwn(BOSS_LOOT, t.key)) drops.boss = BOSS_LOOT[t.key];
    if (Object.hasOwn(MOB_LOOT, t.key)) drops.wiki = MOB_LOOT[t.key];
    if (Object.hasOwn(MAP2_MATS, t.key)) drops.mats = MAP2_MATS[t.key];
    return { ...t, drops: Object.keys(drops).length ? drops : null };
  });
  const out = {
    sourceMapId: id,
    source: 'lumiis-2d@4376dae shared/mobs.js MOB_TYPES (resolved values), shared/metins.js, server/drops.js',
    note: 'Ordinary monsters without drops use the generic chances in genericDrops (server/drops.js DROP).',
    genericDrops: DROP,
    mobs,
    metins,
  };
  if (!mobs.every((m) => MOB_INDEX[m.key] !== undefined)) throw new Error('unknown monster key');
  process.stdout.write(JSON.stringify(out));
}
