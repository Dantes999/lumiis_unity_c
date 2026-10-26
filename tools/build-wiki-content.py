#!/usr/bin/env python3
"""Baut Monster, Spawns, Metins, NPCs und Portale für Map1 (Yongan) und Map2 (Jayang).

Gelände (terrain.bin, collision.bin) kommt aus tools/derive-wiki-map.py (Metin2-Wiki, Maßstab wie lumiis-2d:
1 Kachel = 2 Wiki-Einheiten). Monster und ihre Verteilung kommen 1:1 aus lumiis-2d (Stand 4376dae), exportiert mit
tools/export-lumiis-map.mjs nach tools/lumiis2d-<karte>-spawns.json / -mobs.json:
  - Monstergruppen (Mittelpunkt + Mitglieder), Metinsteine (Anzahl + Kandidatenpunkte), Anführer/Einzelgänger
  - Monsterwerte (Level, Leben, Angriff, Verteidigung, EXP, aggressiv, Fernkampf, Tempo, Angriffstakt)
  - Metin-Wellen (Schwellen und Mitglieder)
Die lumiis-2d-Kacheln liegen im selben Raster wie unsere Wiki-Karten; ein kleiner Versatz (Dorfplatz lumiis-2d ->
Dorfplatz hier) gleicht die Abweichung aus. Landet eine Position auf blockiertem Gelände, rückt sie auf die nächste
begehbare Kachel (höchstens 10 Kacheln, sonst entfällt das Monster - unser Wiki-Gelände hat mehr Berge). NPCs, Portale und Denkmäler bleiben nach dem Wiki.

Eigene Wahl (nicht aus lumiis-2d): Yang, Beute, Reichweiten in Wire, Leine, Aggro-Reichweite, Wander-Radius.
Statt zufälliger Steinpositionen zur Laufzeit (lumiis-2d) stehen Steine fest auf "count" weit verteilten
Kandidatenpunkten, Respawnzeit = Mittel aus lumiis-2d.

Schreibt (Quelle der Wahrheit, committet):
  data/mobs.json            Mobs aus lumiis-2d (1-6 bleiben für die Testkarten unverändert)
  data/metins.json          Wellen je Stein
  data/npcs.json            NPC-Typen (1/2 bleiben für die Testkarten)
  data/monster-looks.json   Sheet/Größe/Tönung je Mob (nur Client-Anzeige)
  data/maps/map{1,2}/{map,spawns,npcs,portals}.json, props.json (village + Denkmäler ergänzt)

Aufruf (numpy, scipy, Pillow):  python3 tools/build-wiki-content.py [--preview /tmp/ordner]
"""
import argparse
import json
import struct
import sys
from pathlib import Path

import numpy as np
from PIL import Image
from scipy import ndimage as ndi

ROOT = Path(__file__).resolve().parents[1]
DATA = ROOT / "data"
TOOLS = ROOT / "tools"
SOLID = {3, 5, 6, 8}  # WATER, TREE, ROCK, WALL (wie tools/derive-wiki-map.py)

# ---------------------------------------------------------------------------------------------------------------
# Monster (lumiis-2d)
# ---------------------------------------------------------------------------------------------------------------
# lumiis-2d-Key -> Mob-Id (die Ids der vorherigen Wiki-Version bleiben, damit Looks/Daten stabil sind)
MOB_IDS = {
    # Yongan
    "wild_dog": 20, "wolf": 21, "alpha_wolf": 22, "wild_boar": 23, "blue_wolf": 26, "alpha_blue_wolf": 28,
    "grey_wolf": 29, "alpha_grey_wolf": 31, "red_boar": 32, "grizzly": 35, "black_bear": 36, "brown_bear": 37,
    "tiger": 39, "hungry_wild_dog": 25,
    # eigene Mini-Bosse (Events) für Yongan
    "boss_pack_leader": 40, "boss_tusk_king": 41, "boss_frost_mane": 42, "boss_old_bear": 43, "boss_snow_tiger": 44,
    # Jayang
    "woath_soldier": 10, "woath_archer": 11, "woath_commander": 12, "woath_general": 13, "woath_soldier_c": 14,
    "woath_archer_c": 15, "woath_commander_c": 60, "woath_general_c": 61,
    "evil_storm_soldier": 62, "bw_soldier": 63, "bw_maniac": 64, "evil_storm_archer": 65, "bw_archer": 66,
    "evil_storm_johwan": 67, "bw_jakto": 68, "black_storm_soldier": 69, "evil_storm_kyukjang": 70,
    "black_storm_maniac": 71, "bw_tosu": 72, "evil_storm_phohwan": 73, "black_archer": 74, "bw_guryung": 75,
    "black_storm_johwan": 76, "black_storm_kyukjan": 77, "black_storm_phohwan": 78,
    "mahon": 79, "bo": 80, "goopae": 81, "chuong": 82,
    "st_wild_infantry": 83, "wild_infantry": 84, "wild_minion": 85, "st_wild_minion": 86, "st_wild_archer": 87,
    "wild_archer": 88, "wild_general": 89, "st_wild_general": 90,
    "brutal_soldier": 91, "brutal_maniac": 92, "brutal_archer": 93, "brutal_specialist": 94, "brutal_captain": 95,
    # Steine
    "metin_sorrow": 24, "metin_battle": 50, "metin_black": 51, "metin_greed": 52,
    "metin_blackness": 18, "metin_darkness": 53, "metin_jealousy": 54,
}
LEADERS = {"mahon", "bo", "goopae", "chuong"}
ANIMAL_LOOKS = {"dog", "wolf", "alpha_wolf", "boar", "blue_wolf", "alpha_blue_wolf", "grey_wolf", "alpha_grey_wolf",
                "red_boar", "brown_bear", "black_bear", "grizzly", "tiger"}
DEFAULT_WAVE_AT = [75, 50, 25]  # lumiis-2d-Steine ohne eigene Schwellen


def _variant(base, **kw):
    m = dict(base)
    m.update(kw)
    return m


def custom_mobs(src):
    """Eigene Monster (nicht in lumiis-2d): hungriger Wildhund und die Yongan-Mini-Bosse. Werte aus den lumiis-2d-
    Vorbildern abgeleitet: Bosse ~8x Leben, ~1,35x Angriff, ~1,2x Verteidigung, ~10x EXP der Gebietsmonster."""
    def boss(base, key, name, level, look, size, tint=None):
        b = src[base]
        return _variant(b, key=key, name=name, level=level, hp=b["hp"] * 8, atk=round(b["atk"] * 1.35),
                        **{"def": round(b["def"] * 1.2)}, exp=b["exp"] * 10, aggressive=True, boss=True, look=look,
                        size=size, tint=tint, cooldown=max(1200, b["cooldown"] - 200))
    out = {
        "hungry_wild_dog": _variant(src["wild_dog"], key="hungry_wild_dog", name="Hungriger Wildhund", level=2,
                                    hp=105, atk=11, **{"def": 1}, exp=18, aggressive=True, cooldown=1700,
                                    tint="#ffb4a0"),
        "boss_pack_leader": boss("alpha_wolf", "boss_pack_leader", "Rudelführer Aschpelz", 6, "alpha_wolf", 1.55,
                                 "#d8d0c8"),
        "boss_tusk_king": boss("wild_boar", "boss_tusk_king", "Hauerkönig", 10, "boar", 1.6),
        "boss_frost_mane": boss("alpha_grey_wolf", "boss_frost_mane", "Frostmähne", 15, "alpha_grey_wolf", 1.6,
                                "#c8e0ff"),
        "boss_old_bear": boss("grizzly", "boss_old_bear", "Altbär Grimmtatze", 20, "grizzly", 1.8),
        "boss_snow_tiger": boss("tiger", "boss_snow_tiger", "Schneetiger", 22, "tiger", 1.6, "#f0f4ff"),
    }
    return out

STONE_DROPS = {
    24: [(8, 100), (9, 100), (45, 25), (49, 20), (50, 15)],
    50: [(9, 100), (21, 25), (26, 20), (44, 25), (35, 30)],
    51: [(9, 100), (23, 20), (27, 20), (45, 25), (36, 20)],
    52: [(9, 100), (10, 10), (22, 15), (24, 15), (50, 25), (36, 30)],
    18: [(9, 100), (10, 15), (22, 15), (24, 15), (48, 20), (36, 30)],
    53: [(9, 100), (10, 20), (22, 20), (24, 20), (46, 25), (36, 40)],
    54: [(9, 100), (10, 30), (22, 25), (24, 25), (47, 25), (48, 20), (36, 50)],
}

# Größe je Look (aus monster.json "groessen" des Asset-Archivs)
LOOK_SCALE = {"dog": 0.85, "wolf": 1, "alpha_wolf": 1.15, "boar": 1, "blue_wolf": 1, "alpha_blue_wolf": 1.15,
              "grey_wolf": 1.05, "alpha_grey_wolf": 1.15, "brown_bear": 1.3, "red_boar": 1.1, "black_bear": 1.3,
              "grizzly": 1.45, "tiger": 1.2, "woath_cmd_c": 1.1, "woath_gen_c": 1.1, "bw_leader": 1.2, "bw_chief": 1.2,
              "black_storm": 1.2, "black_storm_archer": 1.1, "wild_general": 1.1, "st_wild_general": 1.1, "brutal": 1.1,
              "brutal_archer": 1.2, "brutal_captain": 1.5}
LOOK_SCALE.update({k + "_c": v for k, v in list(LOOK_SCALE.items()) if not k.endswith("_c")})



def drops_for(level, human):
    if human:
        if level <= 25:
            return [(35, 10), (9, 3), (51, 15), (5, 1), (21, 1)]
        if level <= 35:
            return [(36, 12), (9, 5), (51, 15), (24, 1), (22, 1), (50, 5)]
        return [(36, 20), (9, 8), (50, 10), (24, 2), (22, 2), (10, 1)]
    if level <= 5:
        return [(8, 30), (6, 10), (40, 20), (41, 10)]
    if level <= 12:
        return [(8, 25), (4, 12), (7, 10), (25, 8), (41, 15), (42, 10), (43, 15)]
    if level <= 20:
        return [(35, 15), (21, 3), (26, 3), (42, 15), (44, 10), (9, 3)]
    return [(35, 20), (23, 2), (27, 2), (44, 12), (45, 8), (9, 5)]



def build_mob(key, src):
    mid = MOB_IDS[key]
    level = src["level"]
    if src["stone"]:
        return {
            "id": mid, "name": src["name"], "speedWire": 1, "wanderRadiusWire": 1,
            "wanderIntervalMinMs": 60000, "wanderIntervalMaxMs": 60000,
            "maxHp": src["hp"], "attack": 0, "defense": src["def"],
            "yangMin": 40 * level, "yangMax": 80 * level,
            "meleeRangeWire": 180, "attackCooldownMs": 1400,
            "aggressive": False, "aggroRangeWire": 0, "leashRangeWire": 200,
            "level": level, "baseXp": src["exp"],
            "drops": [{"itemId": i, "chancePercent": c} for i, c in STONE_DROPS[mid]],
            "fightsBack": False,
        }
    boss = src["boss"] or key in LEADERS
    ranged = src["range"] >= 50
    human = src["look"] not in ANIMAL_LOOKS
    drops = ([(9, 100)] + [(i, min(100, c * 3)) for i, c in drops_for(level, human)] if key.startswith("boss_")
             else [(9, 100), (10, 50), (22, 40), (24, 40), (36, 100), (47, 30)] if src["boss"]
             else [(9, 60), (10, 15), (36, 60), (50, 20)] if key in LEADERS else drops_for(level, human))
    return {
        "id": mid, "name": src["name"],
        "speedWire": round(src["speed"] * 140),
        "wanderRadiusWire": 150 if boss else 400,
        "wanderIntervalMinMs": 3000, "wanderIntervalMaxMs": 7000,
        "maxHp": src["hp"], "attack": src["atk"], "defense": src["def"],
        "yangMin": 3 * level, "yangMax": 6 * level * (10 if boss else 1),
        "meleeRangeWire": 350 if ranged else (200 if boss else 150),
        "attackCooldownMs": src["cooldown"],
        "aggressive": src["aggressive"],
        "aggroRangeWire": 500 if src["aggressive"] else 0,
        "leashRangeWire": 900,
        "level": level,
        "baseXp": src["exp"],
        "drops": [{"itemId": i, "chancePercent": c} for i, c in drops],
    }


def build_waves(metin):
    """lumiis-2d-Wellen -> metins.json: je Schwelle ein Eintrag pro Monsterart (count = Anzahl in der Welle)."""
    at = metin["waveAt"] or DEFAULT_WAVE_AT
    waves = []
    for pct, members in zip(at, metin["waves"]):
        for key in dict.fromkeys(members):
            waves.append({"hpPercent": pct, "mobId": MOB_IDS[key], "count": members.count(key)})
    return waves


def load_lumiis(name, kind):
    return json.loads((TOOLS / f"lumiis2d-{name}-{kind}.json").read_text(encoding="utf-8"))

# ---------------------------------------------------------------------------------------------------------------
# Karten
# ---------------------------------------------------------------------------------------------------------------
def read_terrain(out):
    raw = (DATA / "maps" / out / "terrain.bin").read_bytes()
    assert raw[:4] == b"LTR1", "terrain.bin fehlt/kaputt - erst tools/derive-wiki-map.py ausführen"
    w, h, ox, oy = struct.unpack("<HHhh", raw[4:12])
    t = np.frombuffer(raw[12:], np.uint8).reshape(h, w)
    return t, (ox, oy)


class Grid:
    def __init__(self, out):
        self.t, (self.ox, self.oy) = read_terrain(out)
        self.h, self.w = self.t.shape
        self.walk = ~np.isin(self.t, list(SOLID))

    def wire(self, tx, ty):
        return (self.ox + tx) * 100 + 50, (self.oy + (self.h - 1 - ty)) * 100 + 50

    def cell_x(self, tx):
        return self.ox + tx

    def cell_y(self, ty):
        return self.oy + (self.h - 1 - ty)

    def snap(self, tx, ty, taken=(), maxr=40):
        tx, ty = int(np.clip(tx, 0, self.w - 1)), int(np.clip(ty, 0, self.h - 1))
        for r in range(maxr + 1):
            best = None
            for dy in range(-r, r + 1):
                for dx in range(-r, r + 1):
                    if max(abs(dx), abs(dy)) != r:
                        continue
                    x, y = tx + dx, ty + dy
                    if 0 <= x < self.w and 0 <= y < self.h and self.walk[y, x] and (x, y) not in taken:
                        d = dx * dx + dy * dy
                        if best is None or d < best[0]:
                            best = (d, x, y)
            if best:
                return best[1], best[2]
        raise SystemExit(f"keine begehbare Kachel nahe {tx},{ty}")



def spread(points, n):
    """n möglichst weit verteilte Punkte aus points (erster Punkt, dann jeweils der fernste)."""
    pts = [tuple(p) for p in points]
    if len(pts) <= n:
        return pts
    chosen = [pts[0]]
    while len(chosen) < n:
        chosen.append(max(pts, key=lambda p: min((p[0] - c[0]) ** 2 + (p[1] - c[1]) ** 2 for c in chosen)))
    return chosen


def geodesic(g, sources):
    """Laufweg-Abstand (4er-Nachbarschaft über begehbare Kacheln) von den Quellkacheln aus; unerreichbar = inf."""
    from collections import deque
    dist = np.full(g.t.shape, np.inf)
    q = deque()
    for x, y in sources:
        dist[y, x] = 0
        q.append((x, y))
    while q:
        x, y = q.popleft()
        d = dist[y, x] + 1
        for nx, ny in ((x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1)):
            if 0 <= nx < g.w and 0 <= ny < g.h and g.walk[ny, nx] and d < dist[ny, nx]:
                dist[ny, nx] = d
                q.append((nx, ny))
    return dist


def poisson(mask, radius, rng):
    """Zufällige Punkte in mask mit Mindestabstand radius (einfaches Dart-Throwing auf einem Raster)."""
    ys, xs = np.nonzero(mask)
    order = rng.permutation(len(xs))
    cell = radius / 1.42
    grid = {}
    out = []
    r2 = radius * radius
    for i in order:
        x, y = int(xs[i]), int(ys[i])
        cx, cy = int(x / cell), int(y / cell)
        ok = True
        for gy in range(cy - 2, cy + 3):
            for gx in range(cx - 2, cx + 3):
                p = grid.get((gx, gy))
                if p and (p[0] - x) ** 2 + (p[1] - y) ** 2 < r2:
                    ok = False
                    break
            if not ok:
                break
        if ok:
            grid[(cx, cy)] = (x, y)
            out.append((x, y))
    return out


def direction_name(dx, dy):
    """Himmelsrichtung vom Dorf aus (dy positiv = Süden, Kachelzeile 0 ist Norden)."""
    import math
    if dx * dx + dy * dy < 40 * 40:
        return "nahe dem Dorf"
    a = (math.degrees(math.atan2(-dy, dx)) + 360) % 360
    names = ["im Osten", "im Nordosten", "im Norden", "im Nordwesten", "im Westen", "im Südwesten", "im Süden",
             "im Südosten"]
    return names[int((a + 22.5) // 45) % 8]


def habitat(g):
    """Je Kachel: Felsnähe, Wassernähe (inkl. Sand/Küste) und Offenheit (Abstand zum nächsten Hindernis)."""
    rock = ndi.uniform_filter(np.isin(g.t, [6, 8]).astype(float), size=9)
    wet = ndi.uniform_filter(np.isin(g.t, [3, 4]).astype(float), size=13)
    open_ = ndi.distance_transform_edt(g.walk)
    return {"rock": rock, "wet": wet, "open": np.minimum(open_, 12) / 12}


def build_zones(key, cfg, g, plaza, portal_tile):
    """Level-Karte, Gebiete (Regionen) und Themen je Gebiet. Ergebnis: Liste von Regionen mit Kacheln + Thema."""
    rng = np.random.default_rng(cfg["seed"])
    pcx, pcy = plaza
    safe = np.zeros(g.t.shape, bool)
    r = cfg["safe_r"]
    safe[max(0, pcy - r):pcy + r + 1, max(0, pcx - r):pcx + r + 1] = True
    yy, xx = np.mgrid[0:g.h, 0:g.w]
    safe |= (xx - portal_tile[0]) ** 2 + (yy - portal_tile[1]) ** 2 <= 9 ** 2
    sources = [plaza] + ([cfg["extra_source"]] if cfg.get("extra_source") else [])
    dist = geodesic(g, sources)
    huntable = g.walk & ~safe & np.isfinite(dist)
    # Perzentil des Laufwegs -> 0..1 (gleich große Flächenanteile je Abschnitt)
    dvals = np.sort(dist[huntable])
    pct = np.zeros(g.t.shape)
    pct[huntable] = np.searchsorted(dvals, dist[huntable]) / len(dvals)
    bands = cfg["bands"]
    edges = [b["upto"] for b in bands]
    band_of = np.full(g.t.shape, -1)
    t_in = np.zeros(g.t.shape)
    lo = 0.0
    for i, hi in enumerate(edges):
        m = huntable & (pct >= lo) & (pct < hi if i < len(edges) - 1 else pct <= 1.0)
        band_of[m] = i
        t_in[m] = (pct[m] - lo) / (hi - lo)
        lo = hi
    level = np.zeros(g.t.shape)
    for i, b in enumerate(bands):
        m = band_of == i
        level[m] = b["levels"][0] + t_in[m] * (b["levels"][1] - b["levels"][0])
    hab = habitat(g)

    # Gebiete: je Abschnitt Saatpunkte im Abstand ~region, jede Kachel zum nächsten Saatpunkt desselben Abschnitts
    regions = []
    for i, b in enumerate(bands):
        mask = band_of == i
        if not mask.any():
            continue
        seeds = poisson(mask, cfg["region"], rng)
        ys, xs = np.nonzero(mask)
        pts = np.array(seeds)
        lab = np.zeros(len(xs), int)
        best = np.full(len(xs), np.inf)
        for k, (sx, sy) in enumerate(pts):
            d = (xs - sx) ** 2 + (ys - sy) ** 2
            better = d < best
            best[better] = d[better]
            lab[better] = k
        for k in range(len(pts)):
            sel = lab == k
            if sel.sum() < 40:
                continue
            tx, ty = xs[sel], ys[sel]
            regions.append({
                "band": i, "tiles": (tx, ty),
                "t": float(t_in[ty, tx].mean()), "level": float(level[ty, tx].mean()),
                "rock": float(hab["rock"][ty, tx].mean()), "wet": float(hab["wet"][ty, tx].mean()),
                "open": float(hab["open"][ty, tx].mean()), "size": int(sel.sum()),
                "center": (int(tx.mean()), int(ty.mean())),
            })
    # Habitat-Werte je Abschnitt als Rang 0..1, damit "felsig"/"nass"/"offen" relativ zum Abschnitt gilt
    for i in range(len(bands)):
        rs = [rg for rg in regions if rg["band"] == i]
        for f in ("rock", "wet", "open"):
            vals = sorted(rg[f] for rg in rs)
            for rg in rs:
                rg[f + "R"] = vals.index(rg[f]) / max(1, len(vals) - 1)
    for i, b in enumerate(bands):
        rs = [rg for rg in regions if rg["band"] == i]
        b["assign"](rs, rng)
    return {"level": level, "band": band_of, "huntable": huntable, "regions": regions, "safe": safe}


def pick_theme(region, themes, rng):
    """Thema mit dem besten Habitat-Treffer; t-Bereich muss passen. Kleine Zufallsstreuung für Abwechslung."""
    best, best_score = None, -1e9
    for th in themes:
        lo, hi = th.get("t", (0, 1))
        if not (lo - 0.15 <= region["t"] <= hi + 0.15):
            continue
        h = th.get("habitat")
        score = 0.5 if h is None else region[h + "R"]
        if not (lo <= region["t"] <= hi):
            score -= 0.4
        score += rng.uniform(0, 0.35)
        if score > best_score:
            best, best_score = th, score
    return best


def assign_by_habitat(themes):
    def run(regions, rng):
        for rg in regions:
            rg["theme"] = pick_theme(rg, themes, rng)
    return run


def assign_with_specials(themes, specials):
    """specials: [(Thema, Merkmal, Anzahl)] - das Gebiet mit dem höchsten Merkmal und seine nächsten Nachbarn
    (zusammen 'Anzahl') bekommen das Thema fest, z. B. eine zusammenhängende Ebene für den Weißen Eid."""
    def run(regions, rng):
        fixed = set()
        for th, feat, n in specials:
            free_rs = [rg for rg in regions if id(rg) not in fixed]
            core = max(free_rs, key=lambda rg: rg[feat] * rg["size"] ** 0.5)
            cx, cy = core["center"]
            near = sorted(free_rs, key=lambda rg: (rg["center"][0] - cx) ** 2 + (rg["center"][1] - cy) ** 2)
            for rg in near[:n]:
                rg["theme"] = th
                fixed.add(id(rg))
        for rg in regions:
            if id(rg) not in fixed:
                rg["theme"] = pick_theme(rg, themes, rng)
    return run


def pack(name, members, size, leader=None, habitat=None, t=(0, 1)):
    return {"name": name, "members": members, "size": size, "leader": leader, "habitat": habitat, "t": t}


def faction_themes(factions):
    """Jayang: je Fraktion ein Thema; Mitglieder werden je Region passend zum Gebietslevel gefiltert."""
    def run(regions, rng):
        for rg in regions:
            lvl = rg["level"]
            best, best_score = None, -1e9
            for f in factions:
                lv = [MOB_LEVELS[m] for m in f["members"]]
                if not (min(lv) - 2 <= lvl <= max(lv) + 2):
                    continue
                h = f.get("habitat")
                score = (0.5 if h is None else rg[h + "R"]) + rng.uniform(0, 0.5)
                if score > best_score:
                    best, best_score = f, score
            if best is None:
                best = min(factions, key=lambda f: min(abs(MOB_LEVELS[m] - lvl) for m in f["members"]))
            near = [m for m in best["members"] if lvl - 4 <= MOB_LEVELS[m] <= lvl + 2]
            if not near:
                near = [min(best["members"], key=lambda m: abs(MOB_LEVELS[m] - lvl))]
            leader = None
            if best.get("leaders"):
                ls = [m for m in best["leaders"] if MOB_LEVELS[m] <= lvl + 3]
                leader = ls if ls and rg["t"] > 0.4 else None
            rg["theme"] = pack(best["name"], near, best["size"], leader=leader)
    return run


GROUP_SIZE = 3
MOB_LEVELS = {}  # wird in main() aus den lumiis-2d-Werten gefüllt


MAPS = {
    "yongan": {
        "out": "map1", "name": "Yongan",
        "plaza": (646, 585), "lumiis": "yongan",
        "portal": {"at": (405, 73), "target": "map2"},
        "dummies": True, "seed": 11, "safe_r": 36, "region": 46, "group_spacing": 13,
        # Level-Ringe ums Dorf (Anteil der Jagdfläche nach Laufweg) - innen Wildhunde, außen Tiger/Grizzlys
        "bands": [
            {"upto": 0.10, "levels": (1, 2), "assign": assign_by_habitat([
                pack("Wildhunde", ["wild_dog"], (3, 4), t=(0, 0.55)),
                pack("Hungrige Wildhunde", ["hungry_wild_dog"], (3, 4), t=(0.45, 1))])},
            {"upto": 0.24, "levels": (3, 5), "assign": assign_by_habitat([
                pack("Wölfe", ["wolf"], (3, 4), t=(0, 0.5)),
                pack("Alphawolf-Rudel", ["wolf"], (3, 4), leader=["alpha_wolf"], t=(0.5, 1))])},
            {"upto": 0.42, "levels": (6, 10), "assign": assign_by_habitat([
                pack("Wildschweine", ["wild_boar"], (2, 4), habitat="open"),
                pack("Blauwölfe", ["blue_wolf"], (3, 4), habitat="rock")])},
            {"upto": 0.60, "levels": (11, 14), "assign": assign_by_habitat([
                pack("Blauwolf-Rudel", ["blue_wolf"], (3, 4), leader=["alpha_blue_wolf"], t=(0, 0.5)),
                pack("Grauwolf-Rudel", ["grey_wolf"], (3, 4), leader=["alpha_grey_wolf"], t=(0.5, 1))])},
            {"upto": 0.78, "levels": (15, 17), "assign": assign_by_habitat([
                pack("Braunbären", ["brown_bear"], (2, 3), habitat="rock", t=(0, 0.6)),
                pack("Rote Keiler", ["red_boar"], (2, 4), habitat="wet"),
                pack("Schwarzbären", ["black_bear"], (2, 3), habitat="rock", t=(0.4, 1))])},
            {"upto": 1.00, "levels": (18, 21), "assign": assign_with_specials([
                pack("Grizzlys", ["grizzly"], (2, 3), habitat="rock"),
                pack("Tiger", ["tiger", "tiger", "red_boar"], (2, 3), habitat="wet"),
            ], [
                (pack("Weißer Eid", ["woath_soldier", "woath_soldier", "woath_archer"], (3, 4)), "openR", 3),
                (pack("Tiger", ["tiger", "tiger", "red_boar"], (2, 3)), "wetR", 3),
            ])},
        ],
        "stones": [("metin_sorrow", 6, 180), ("metin_battle", 6, 225), ("metin_greed", 6, 270),
                   ("metin_black", 6, 330)],
        "events": [
            {"boss": "boss_pack_leader", "escort": [("wolf", 3)], "interval": 600, "themes": ["Alphawolf-Rudel"]},
            {"boss": "boss_tusk_king", "escort": [("wild_boar", 3)], "interval": 900, "themes": ["Wildschweine"]},
            {"boss": "boss_frost_mane", "escort": [("grey_wolf", 2), ("alpha_grey_wolf", 1)], "interval": 1200,
             "themes": ["Grauwolf-Rudel"]},
            {"boss": "boss_old_bear", "escort": [("grizzly", 2)], "interval": 1500, "themes": ["Grizzlys"]},
            {"boss": "boss_snow_tiger", "escort": [("tiger", 2)], "interval": 1800, "themes": ["Tiger"],
             "area_level": 21},
        ],
        "npcs": [
            ("Wächter des Dorfplatzes", 647, 582), ("Schmied", 672, 556), ("Waffenhändler", 596, 557),
            ("Rüstungshändler", 596, 605), ("Gemischtwarenhändlerin", 678, 565), ("Lagerverwalter", 674, 607),
            ("Alte Frau", 633, 622), ("Alter Mann", 76, 601), ("Ah-Yu", 698, 513), ("Alchemist", 622, 511),
            ("Aranyo", 686, 474), ("Baek-Go", 695, 657), ("Biologe Chaegirab", 896, 610),
            ("Drachenwächter Jaharr", 555, 533), ("Gayahändler", 652, 475), ("Handelsverwalterin", 684, 581),
            ("Handu-Up", 545, 397), ("Hauptmann", 573, 531), ("Kampfzonen-Meister", 665, 634), ("Mirine", 582, 578),
            ("Octavio", 623, 630), ("Schlachtenverwalter", 657, 619), ("Seon-Hae", 655, 646), ("Soon", 629, 471),
            ("Stadtwächter", 605, 663), ("Stallbursche", 718, 577), ("Teleporter", 555, 625),
            ("Theowahdan", 732, 480), ("Uriel", 713, 605), ("Wonda-Rim", 545, 412), ("Yonah", 597, 511),
        ],
        "monuments": [("Statue der Illusionen", 540, 500), ("Weol-Denkmal", 582, 347)],
    },
    "jayang": {
        "out": "map2", "name": "Jayang",
        "plaza": (458, 634), "lumiis": "jayang",
        "portal": {"at": (948, 804), "target": "map1"},
        "dummies": False, "seed": 23, "safe_r": 36, "region": 46, "group_spacing": 12,
        # Aufsteigend ab Portal (Ankunft am Ostrand) und Dorf: dort Weißer Eid, je weiter weg desto härter
        "level_from_portal": True,
        "bands": [
            {"upto": 1.00, "levels": (18, 42), "assign": faction_themes([
                {"name": "Weißer Eid", "members": ["woath_soldier", "woath_archer"], "size": (3, 4)},
                {"name": "Verfluchter Weißer Eid", "members": ["woath_soldier_c", "woath_archer_c"], "size": (3, 4),
                 "leaders": ["woath_commander", "woath_general", "woath_commander_c", "woath_general_c"]},
                {"name": "Böser Sturm", "members": ["evil_storm_soldier", "evil_storm_archer", "evil_storm_johwan",
                                                    "evil_storm_kyukjang", "evil_storm_phohwan"],
                 "size": (3, 4), "habitat": "wet"},
                {"name": "Schwarzwind", "members": ["bw_soldier", "bw_maniac", "bw_archer", "bw_jakto", "bw_tosu",
                                                    "bw_guryung"], "size": (3, 4), "habitat": "open"},
                {"name": "Wilde", "members": ["st_wild_infantry", "wild_infantry", "wild_minion", "st_wild_minion",
                                              "st_wild_archer", "wild_archer", "wild_general", "st_wild_general"],
                 "size": (3, 4), "habitat": "open"},
                {"name": "Schwarzer Sturm", "members": ["black_storm_soldier", "black_storm_maniac", "black_archer",
                                                        "black_storm_johwan", "black_storm_kyukjan",
                                                        "black_storm_phohwan"], "size": (2, 3), "habitat": "rock"},
                {"name": "Brutale", "members": ["brutal_soldier", "brutal_maniac", "brutal_archer",
                                                "brutal_specialist"], "size": (2, 3), "habitat": "rock"},
            ])},
        ],
        "stones": [("metin_blackness", 5, 900), ("metin_darkness", 5, 900), ("metin_jealousy", 5, 900)],
        "events": [
            {"boss": "mahon", "escort": [("bw_archer", 2), ("bw_guryung", 2)], "interval": 1500,
             "themes": ["Schwarzwind"]},
            {"boss": "bo", "escort": [("bw_archer", 2), ("bw_guryung", 2)], "interval": 1500,
             "themes": ["Schwarzwind"], "area_level": 35},
            {"boss": "goopae", "escort": [("bw_archer", 2), ("bw_guryung", 2)], "interval": 1800,
             "themes": ["Schwarzwind", "Böser Sturm"], "area_level": 36},
            {"boss": "chuong", "escort": [("bw_guryung", 1), ("bw_jakto", 1), ("bw_tosu", 1), ("bw_archer", 2)],
             "interval": 2400, "area_level": 42},
            {"boss": "brutal_captain", "escort": [("wild_archer", 2), ("wild_general", 2)], "interval": 2400,
             "themes": ["Brutale"], "area_level": 42},
        ],
        "npcs": [
            ("Wächter des Dorfplatzes", 458, 631), ("Schmied", 423, 618), ("Waffenhändler", 450, 608),
            ("Rüstungshändler", 471, 608), ("Gemischtwarenhändlerin", 422, 608), ("Lagerverwalter", 430, 574),
            ("Alte Frau", 487, 666), ("Alter Mann", 100, 150), ("Ariyoung", 408, 581), ("Deokbae", 582, 561),
            ("Fahrender Händler", 516, 579), ("Harang", 583, 585), ("Huahn-So", 419, 563), ("Nakajima", 481, 298),
            ("Schlachtenverwalter", 478, 633), ("Stallbursche", 548, 588), ("Taurean", 475, 684),
            ("Teleporter", 500, 575), ("Verräter Balso", 541, 620), ("Yang-Shin", 564, 603), ("Yu-Hwan", 415, 639),
            ("Yu-Rang", 549, 562),
        ],
        "monuments": [],
    },
}


SHOPS = {
    "Waffenhändler": ("Merchant", [20, 21, 2, 23, 3, 22]),
    "Rüstungshändler": ("Merchant", [25, 4, 5, 24, 6, 26, 7, 27]),
    "Gemischtwarenhändlerin": ("Merchant", [8, 35, 36, 9, 10]),
    "Fahrender Händler": ("Merchant", [8, 35, 36, 9]),
    "Schmied": ("Blacksmith", [9, 10]),
}
GUARDS = {"Wächter des Dorfplatzes", "Stadtwächter", "Hauptmann", "Drachenwächter Jaharr"}



def build_map(key, cfg, mobs_by_id, npc_ids, preview_dir):
    g = Grid(cfg["out"])
    src = load_lumiis(cfg["lumiis"], "spawns")
    rng = np.random.default_rng(cfg["seed"] + 1)
    spawns = []
    taken = set()
    sid = 1

    def add(mob_id, tx, ty, respawn, group=None):
        nonlocal sid
        x, y = g.wire(tx, ty)
        sp = {"id": sid, "mobId": mob_id, "positionX": x, "positionY": y, "respawnSeconds": respawn}
        if group:
            sp["groupId"] = group  # Server: Gruppe greift gemeinsam an und wandert zusammen
        spawns.append(sp)
        taken.add((tx, ty))
        sid += 1

    pcx, pcy = g.snap(cfg["plaza"][0] // 2, cfg["plaza"][1] // 2)

    # Portal: Fläche 3 x 3 Kacheln, Mitte so wählen, dass alle Ecken begehbar sind
    p = cfg["portal"]
    open3 = ndi.minimum_filter(g.walk.astype(np.uint8), size=3, mode="constant", cval=0).astype(bool)
    ys, xs = np.nonzero(open3)
    k = int(np.argmin((xs - p["at"][0] / 2) ** 2 + (ys - p["at"][1] / 2) ** 2))
    ptx, pty = int(xs[k]), int(ys[k])
    portal = {"tile": (ptx, pty), "target": p["target"]}
    if cfg.get("level_from_portal"):
        cfg["extra_source"] = (ptx, pty)

    z = build_zones(key, cfg, g, (pcx, pcy), (ptx, pty))
    level, huntable = z["level"], z["huntable"]

    def free(x, y):
        return 0 <= x < g.w and 0 <= y < g.h and huntable[y, x] and (x, y) not in taken

    def place(mob_id, tx, ty, respawn, maxr=4, group=None):
        for r in range(maxr + 1):
            ring = [(tx + ox, ty + oy) for oy in range(-r, r + 1) for ox in range(-r, r + 1) if max(abs(ox), abs(oy)) == r]
            rng.shuffle(ring)
            for x, y in ring:
                if free(x, y):
                    add(mob_id, x, y, respawn, group)
                    return True
        return False

    # Trainingspuppen am Westrand des Dorfplatzes (bestehendes Feature, Mob 6)
    if cfg["dummies"]:
        for i in range(3):
            x, y = g.snap(pcx - 9, pcy - 2 + 2 * i, taken)
            add(6, x, y, 5)

    # Lumiis-Steine: dort, wo das Gebietslevel zur Steinstufe passt, möglichst weit verteilt
    open5 = ndi.minimum_filter(g.walk.astype(np.uint8), size=5, mode="constant", cval=0).astype(bool)
    for stone, count, respawn in cfg["stones"]:
        lv = MOB_LEVELS[stone]
        for win in (1.0, 2.0, 3.5):
            m = huntable & open5 & (np.abs(level - lv) <= win)
            if m.sum() >= count * 20:
                break
        ys, xs = np.nonzero(m)
        pts = list(zip(xs.tolist(), ys.tolist()))
        rng.shuffle(pts)
        for x, y in spread(pts[:4000], count):
            place(MOB_IDS[stone], x, y, respawn)

    # Monstergruppen je Gebiet
    zone_names = {}
    gid = 0
    for rg in z["regions"]:
        th = rg["theme"]
        zone_names.setdefault(th["name"], 0)
        zone_names[th["name"]] += 1
        mask = np.zeros(g.t.shape, bool)
        mask[rg["tiles"][1], rg["tiles"][0]] = True
        centers = poisson(mask, cfg["group_spacing"], rng)
        for cx, cy in centers:
            # Wie im Original: immer 3 Monster desselben Typs je Gruppe (Bosse ausgenommen, siehe Events)
            if th.get("leader") and rng.random() < 0.3:
                kind = str(rng.choice(th["leader"]))
            else:
                kind = str(rng.choice(th["members"]))
            group = [kind] * GROUP_SIZE
            lvl = MOB_LEVELS[kind]
            respawn = 30 if lvl < 10 else 45 if lvl < 25 else 60
            gid += 1
            for m in group:
                place(MOB_IDS[m], cx, cy, respawn, group=gid)

    # Mini-Boss-Events: Gebiet mit passendem Level (und Habitat), mehrere Erscheinungsorte
    events = []
    for i, ev in enumerate(cfg["events"]):
        boss = ev["boss"]
        lv = ev.get("area_level", MOB_LEVELS[boss])
        cand = [rg for rg in z["regions"] if (not ev.get("themes") or rg["theme"]["name"] in ev["themes"])]
        if not cand:
            cand = z["regions"]
        cand.sort(key=lambda rg: abs(rg["level"] - lv) - (0.3 * rg["size"] / 1000))
        rg = cand[0]
        tx, ty = rg["tiles"]
        ok = open5[ty, tx] & huntable[ty, tx]
        pts = list(zip(tx[ok].tolist(), ty[ok].tolist()))
        rng.shuffle(pts)
        spots = spread(pts[:3000], 4)
        events.append({
            "id": i + 1, "bossMobId": MOB_IDS[boss],
            "escort": [{"mobId": MOB_IDS[k], "count": c} for k, c in ev["escort"]],
            "intervalSeconds": ev["interval"], "areaName": direction_name(rg["center"][0] - pcx, rg["center"][1] - pcy),
            "points": [{"x": g.wire(x, y)[0], "y": g.wire(x, y)[1]} for x, y in spots],
        })

    # NPCs
    placements = []
    npc_taken = set()
    for i, (name, wx, wy) in enumerate(cfg["npcs"]):
        x, y = g.snap(wx // 2, wy // 2, npc_taken | taken)
        npc_taken.add((x, y))
        px, py = g.wire(x, y)
        placements.append({"id": i + 1, "npcId": npc_ids[name], "positionX": px, "positionY": py})

    if preview_dir:
        im = np.where(g.walk[..., None], 40, 10).astype(np.uint8).repeat(3, 2)
        lv = level / max(1, level.max())
        im[huntable] = (np.stack([40 + 120 * lv, 120 - 80 * lv, 60 + 0 * lv], -1)[huntable]).astype(np.uint8)
        for s in spawns:
            cx = s["positionX"] // 100 - g.ox
            cy = g.h - 1 - (s["positionY"] // 100 - g.oy)
            m = mobs_by_id[s["mobId"]]
            stone = m.get("fightsBack") is False
            im[cy, cx] = [255, 0, 255] if stone else [255, max(0, 255 - m["level"] * 6), 40]
        for ev in events:
            for pt in ev["points"]:
                cx, cy = pt["x"] // 100 - g.ox, g.h - 1 - (pt["y"] // 100 - g.oy)
                im[max(0, cy - 2):cy + 3, max(0, cx - 2):cx + 3] = [0, 255, 255]
        Image.fromarray(im).resize((g.w * 2, g.h * 2), Image.NEAREST).save(Path(preview_dir) / f"{key}-spawns.png")
        zim = np.where(g.walk[..., None], 30, 0).astype(np.uint8).repeat(3, 2)
        names = sorted({rg["theme"]["name"] for rg in z["regions"]})
        pal = [(230, 25, 75), (60, 180, 75), (255, 225, 25), (0, 130, 200), (245, 130, 48), (145, 30, 180),
               (70, 240, 240), (240, 50, 230), (210, 245, 60), (250, 190, 212), (0, 128, 128), (170, 110, 40),
               (128, 0, 0), (170, 255, 195), (128, 128, 0)]
        for rg in z["regions"]:
            zim[rg["tiles"][1], rg["tiles"][0]] = pal[names.index(rg["theme"]["name"]) % len(pal)]
        zim[z["safe"] & g.walk] = (255, 255, 255)
        Image.fromarray(zim).resize((g.w * 2, g.h * 2), Image.NEAREST).save(Path(preview_dir) / f"{key}-zones.png")
        print("   Farben:", {n: pal[i % len(pal)] for i, n in enumerate(names)})

    print(f"{key}: {len(spawns)} Spawns in {len(z['regions'])} Gebieten, {len(placements)} NPCs, "
          f"{len(events)} Events, Start {g.wire(pcx, pcy)}")
    for n, c in zone_names.items():
        print(f"   {n}: {c} Gebiete")
    return g, spawns, placements, (pcx, pcy), portal, events


def portal_rect(g, tx, ty):
    x1, x2 = g.cell_x(tx - 1) * 100, g.cell_x(tx + 1) * 100 + 99
    y1, y2 = g.cell_y(ty + 1) * 100, g.cell_y(ty - 1) * 100 + 99
    return x1, y1, x2, y2


def arrival(g, portal_tile, plaza, steps=7):
    """Begehbare Kachel 'steps' Schritte vom Portal entfernt, möglichst Richtung Dorf."""
    from collections import deque
    sx, sy = portal_tile
    seen = {(sx, sy): 0}
    q = deque([(sx, sy)])
    ring = []
    while q:
        x, y = q.popleft()
        d = seen[(x, y)]
        if d == steps:
            ring.append((x, y))
            continue
        for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
            nx, ny = x + dx, y + dy
            if 0 <= nx < g.w and 0 <= ny < g.h and g.walk[ny, nx] and (nx, ny) not in seen:
                seen[(nx, ny)] = d + 1
                q.append((nx, ny))
    ring = [p for p in ring if max(abs(p[0] - sx), abs(p[1] - sy)) >= 3]
    if not ring:
        raise SystemExit(f"kein Ankunftspunkt bei Portal {portal_tile}")
    return min(ring, key=lambda p: (p[0] - plaza[0]) ** 2 + (p[1] - plaza[1]) ** 2)


def npc_types():
    names = []
    for cfg in MAPS.values():
        for n, _, _ in cfg["npcs"]:
            if n not in names:
                names.append(n)
    npcs = [
        {"id": 1, "name": "Dorfhaendler", "appearance": {"bodyId": 1, "hairStyleId": 1, "hairColorId": 1, "armorId": 3,
                                                         "helmetId": 0, "weaponId": 0, "shieldId": 0},
         "shopItemIds": [8, 2, 3, 4, 6, 7, 9, 10, 20, 21, 22, 23, 24, 25, 26, 27, 35, 36, 40, 41, 42, 43, 44, 45, 46,
                         47, 48, 49, 50, 51], "service": "Merchant"},
    ]
    ids = {}
    next_id = 3
    for n in names:
        if n == "Schmied":
            ids[n] = 2
            continue
        ids[n] = next_id
        next_id += 1
    out = list(npcs)
    for n in names:
        i = ids[n]
        service, shop = SHOPS.get(n, ("None", []))
        guard = n in GUARDS
        app = {"bodyId": 1, "hairStyleId": 1 + i % 3, "hairColorId": 1 + (i * 3) % 8,
               "armorId": 4 if guard else 1 + i % 3, "helmetId": 2 if guard else (1 if i % 5 == 0 else 0),
               "weaponId": 2 if guard else (4 if n == "Stallbursche" else 0), "shieldId": 1 if guard else 0}
        if n == "Schmied":
            app = {"bodyId": 1, "hairStyleId": 1, "hairColorId": 1, "armorId": 3, "helmetId": 0, "weaponId": 0,
                   "shieldId": 0}
        e = {"id": i, "name": n, "appearance": app, "shopItemIds": shop}
        if service != "None":
            e["service"] = service
        out.append(e)
    out.sort(key=lambda e: e["id"])
    return out, ids



def monster_looks(mobs_src):
    looks = [
        {"mobId": 1, "sheet": "alpha_wolf"}, {"mobId": 2, "sheet": "boar"},
        {"mobId": 3, "sheet": "metin_sorrow", "stone": True}, {"mobId": 4, "sheet": "bw_leader", "scale": 1.3},
        {"mobId": 5, "sheet": "alpha_wolf", "tint": "#ffb4a0"}, {"mobId": 6, "sheet": "dummy"},
    ]
    for key, src in mobs_src.items():
        mid = MOB_IDS[key]
        e = {"mobId": mid, "sheet": src["look"]}
        if src["stone"]:
            e["stone"] = True
        else:
            s = LOOK_SCALE.get(src["look"], 1.0) * src.get("size", 1)
            if key.startswith("boss_"):
                s = src["size"]
            if src.get("tint"):
                e["tint"] = src["tint"]
            if key in LEADERS:
                s = {"mahon": 1.15, "bo": 1.2, "goopae": 1.25, "chuong": 1.3}[key]
            if s != 1.0:
                e["scale"] = round(s, 2)
        looks.append(e)
    looks.sort(key=lambda e: e["mobId"])
    return looks


def dump(path, obj):
    path.write_text(json.dumps(obj, ensure_ascii=False, indent=2) + "\n", encoding="utf-8", newline="\n")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--preview", help="Ordner für Kontrollbilder")
    a = ap.parse_args()

    # --- Mobs (alle auf den beiden Karten vorkommenden lumiis-2d-Typen) ---
    mobs_src, metins_src = {}, []
    for cfg in MAPS.values():
        doc = load_lumiis(cfg["lumiis"], "mobs")
        for m in doc["mobs"]:
            if m["key"] not in MOB_IDS:
                raise SystemExit(f"lumiis-2d-Monster {m['key']} hat keine Mob-Id in MOB_IDS")
            mobs_src.setdefault(m["key"], m)
        metins_src += doc["metins"]
    for k, m in custom_mobs(mobs_src).items():
        mobs_src[k] = m
    MOB_LEVELS.update({k: m["level"] for k, m in mobs_src.items()})
    mobs_doc = json.loads((DATA / "mobs.json").read_text(encoding="utf-8"))
    keep = [m for m in mobs_doc["mobs"] if m["id"] <= 6]
    all_mobs = sorted(keep + [build_mob(k, s) for k, s in mobs_src.items()], key=lambda m: m["id"])
    ids = [m["id"] for m in all_mobs]
    assert len(ids) == len(set(ids)), "doppelte Mob-Id"
    mobs_doc["mobs"] = all_mobs
    mobs_by_id = {m["id"]: m for m in all_mobs}

    metins_doc = json.loads((DATA / "metins.json").read_text(encoding="utf-8"))
    metins_doc["metins"] = [m for m in metins_doc["metins"] if m["mobId"] == 3] + [
        {"mobId": MOB_IDS[m["key"]], "waves": build_waves(m)} for m in metins_src]

    npcs, npc_ids = npc_types()

    maps = {}
    for key, cfg in MAPS.items():
        maps[key] = (cfg,) + build_map(key, cfg, mobs_by_id, npc_ids, a.preview)

    for key, (cfg, g, spawns, placements, plaza, portal, events) in maps.items():
        d = DATA / "maps" / cfg["out"]
        dump(d / "events.json", {"_hinweis": "Mini-Boss-Events: erscheinen zeitgesteuert an einem der Punkte, "
                                             "Ankündigung im Kartenchat. Erzeugt von tools/build-wiki-content.py.",
                                 "events": events})
        tgt_cfg, tg, _, _, tplaza, tportal, _ = next(v for v in maps.values() if v[0]["out"] == portal["target"])
        ax, ay = arrival(tg, tportal["tile"], tplaza)
        x1, y1, x2, y2 = portal_rect(g, *portal["tile"])
        tx, ty = tg.wire(ax, ay)
        dump(d / "portals.json", {"portals": [{"id": 1, "x1": x1, "y1": y1, "x2": x2, "y2": y2,
                                               "targetMapId": portal["target"], "targetX": tx, "targetY": ty}]})
        sx, sy = g.wire(*plaza)
        dump(d / "map.json", {"name": cfg["name"], "startX": sx, "startY": sy, "terrain": True})
        dump(d / "spawns.json", {"spawns": spawns})
        dump(d / "npcs.json", {"placements": placements})
        props_path = d / "props.json"
        props = json.loads(props_path.read_text(encoding="utf-8"))
        props["props"] = [p for p in props["props"] if p["type"] == "house"]
        for name, wx, wy in cfg["monuments"]:
            mx, my = g.snap(wx // 2, wy // 2)
            props["props"].append({"type": "monument", "name": name, "tx": mx, "ty": my})
        props["village"] = {"cx": plaza[0], "cy": plaza[1], "r": 11 if key == "yongan" else 10}
        dump(props_path, props)

    dump(DATA / "mobs.json", mobs_doc)
    dump(DATA / "metins.json", metins_doc)
    dump(DATA / "npcs.json", {"npcs": npcs})
    dump(DATA / "monster-looks.json", {
        "_hinweis": "Nur Client-Anzeige: Sheet unter Resources/Sprites/Monsters, Größe, Tönung. Erzeugt von "
                    "tools/build-wiki-content.py.",
        "looks": monster_looks(mobs_src)})


if __name__ == "__main__":
    main()
