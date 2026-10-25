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
    "tiger": 39,
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
    drops = ([(9, 100), (10, 50), (22, 40), (24, 40), (36, 100), (47, 30)] if src["boss"]
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
MAPS = {
    "yongan": {
        "out": "map1", "name": "Yongan",
        "plaza": (646, 585), "lumiis": "yongan",
        "portal": {"at": (405, 73), "target": "map2"},
        "dummies": True,
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
        "dummies": False,
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


def build_map(key, cfg, mobs_by_id, npc_ids, preview_dir):
    g = Grid(cfg["out"])
    src = load_lumiis(cfg["lumiis"], "spawns")
    spawns = []
    taken = set()
    sid = 1
    dropped = 0

    def add(mob_id, tx, ty, respawn):
        nonlocal sid
        x, y = g.wire(tx, ty)
        spawns.append({"id": sid, "mobId": mob_id, "positionX": x, "positionY": y, "respawnSeconds": respawn})
        taken.add((tx, ty))
        sid += 1

    pcx, pcy = g.snap(cfg["plaza"][0] // 2, cfg["plaza"][1] // 2)
    # lumiis-2d-Raster -> unser Raster: Dorfplatz auf Dorfplatz
    dx, dy = pcx - src["village"]["cx"], pcy - src["village"]["cy"]
    safe = [(s["x0"] + dx, s["y0"] + dy, s["x1"] + dx, s["y1"] + dy) for s in src["safe"]]

    def in_safe(tx, ty):
        return any(x0 <= tx <= x1 and y0 <= ty <= y1 for x0, y0, x1, y1 in safe)

    def place(mob_id, tx, ty, respawn, maxr=10):
        nonlocal dropped
        for r in range(maxr + 1):
            ring = [(tx + ox, ty + oy) for oy in range(-r, r + 1) for ox in range(-r, r + 1) if max(abs(ox), abs(oy)) == r]
            ring.sort(key=lambda p: (p[0] - tx) ** 2 + (p[1] - ty) ** 2)
            for x, y in ring:
                if 0 <= x < g.w and 0 <= y < g.h and g.walk[y, x] and (x, y) not in taken and not in_safe(x, y):
                    add(mob_id, x, y, respawn)
                    return True
        dropped += 1
        return False

    def place_group(members, tx, ty, respawn):
        for k in members:
            place(MOB_IDS[k["key"] if isinstance(k, dict) else k], tx, ty, respawn)

    # Trainingspuppen am Westrand des Dorfplatzes (bestehendes Feature, Mob 6)
    if cfg["dummies"]:
        for i in range(3):
            x, y = g.snap(pcx - 9, pcy - 2 + 2 * i, taken)
            add(6, x, y, 5)

    sp = src["spawns"]
    # Steine zuerst (feste Plätze), dann Anführer/Einzelgänger, dann die normalen Gruppen
    for m in sp["metins"]:
        respawn = sum(m["respawnMs"]) // 2 // 1000
        pts = [(x + dx, y + dy) for x, y in m["points"] if not in_safe(x + dx, y + dy)]
        for x, y in spread(pts, m["count"]):
            place(MOB_IDS[m["key"]], x, y, respawn)
    for s in sp["specials"]:
        respawn = sum(s["respawnMs"]) // 2 // 1000
        pts = [(x + dx, y + dy) for x, y in s["points"] if not in_safe(x + dx, y + dy)]
        for x, y in spread(pts, s["count"]):
            place_group(s["members"], x, y, respawn)
    for grp in sp["groups"]:
        tx, ty = grp["tx"] + dx, grp["ty"] + dy
        if in_safe(tx, ty):
            continue
        place_group(grp["members"], tx, ty, max(10, grp["respawnMs"] // 1000))

    # Portal
    p = cfg["portal"]
    # Portalfläche 3 x 3 Kacheln: Mitte so wählen, dass alle Ecken begehbar sind
    open3 = ndi.minimum_filter(g.walk.astype(np.uint8), size=3, mode="constant", cval=0).astype(bool)
    ys, xs = np.nonzero(open3)
    k = int(np.argmin((xs - p["at"][0] / 2) ** 2 + (ys - p["at"][1] / 2) ** 2))
    ptx, pty = int(xs[k]), int(ys[k])
    portal = {"tile": (ptx, pty), "target": p["target"]}

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
        for s in spawns:
            cx = s["positionX"] // 100 - g.ox
            cy = g.h - 1 - (s["positionY"] // 100 - g.oy)
            lvl = mobs_by_id[s["mobId"]]["level"]
            stone = mobs_by_id[s["mobId"]].get("fightsBack") is False
            im[cy, cx] = [255, 0, 255] if stone else [min(255, 60 + lvl * 8), max(0, 255 - lvl * 6), 40]
        Image.fromarray(im).resize((g.w * 2, g.h * 2), Image.NEAREST).save(Path(preview_dir) / f"{key}-spawns.png")

    print(f"{key}: {len(spawns)} Spawns, {dropped} ohne Platz, {len(placements)} NPCs, Versatz {dx},{dy}, "
          f"Start {g.wire(pcx, pcy)}")
    return g, spawns, placements, (pcx, pcy), portal


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

    for key, (cfg, g, spawns, placements, plaza, portal) in maps.items():
        d = DATA / "maps" / cfg["out"]
        tgt_cfg, tg, _, _, tplaza, tportal = next(v for v in maps.values() if v[0]["out"] == portal["target"])
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
