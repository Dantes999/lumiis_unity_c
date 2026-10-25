#!/usr/bin/env python3
"""Baut Monster, Spawns, Metins, NPCs und Portale für Map1 (Yongan) und Map2 (Jayang) nach dem Metin2-Wiki.

Grundlage (Wiki, Stand 2026-10-25, Seiten "Yongan" und "Jayang"):
  - Monsterlisten mit Level/Stufe/EXP (Yongan) bzw. Level/Stufe/TP/EXP (Jayang, Tabelle aus lumiis-2d shared/mobs.js,
    dort ebenfalls aus dem Wiki übernommen)
  - Monsterkarten (Yongan_2.jpg / Jayang_2.jpg): farbige Jagdgebiete mit Legende -> Spawn-Gebiete
  - Metinkarten (Yongan-Metinkarte.jpg / Jayang-Metinkarte.jpg): farbige Gebiete je Steinart -> Steinpositionen
  - NPC-Koordinaten aus den NPC-Listen der Wiki-Seiten
Die Wiki-Bilder kommen NICHT ins Repo (keine Referenzbilder); sie werden nur gelesen. Das Gelände (terrain.bin,
collision.bin) muss vorher mit tools/derive-wiki-map.py erzeugt sein.

Was das Wiki nicht nennt, ist eigene Wahl (im Code als Formel dokumentiert): Lebenspunkte der Map1-Tiere,
Angriff/Verteidigung, Tempo, Yang, Beute, Respawn-Zeiten, Gruppengröße und Monsterdichte.

Schreibt (Quelle der Wahrheit, committet):
  data/mobs.json            Mobs 10-15, 18, 20-54, 60-95 (1-6 bleiben für die Testkarten unverändert)
  data/metins.json          Wellen je Stein
  data/npcs.json            NPC-Typen (1/2 bleiben für die Testkarten)
  data/monster-looks.json   Sheet/Größe/Tönung je Mob (nur Client-Anzeige)
  data/maps/map{1,2}/{map,spawns,npcs,portals}.json, props.json (village + Denkmäler ergänzt)

Aufruf (numpy, scipy, Pillow):  python3 tools/build-wiki-content.py /pfad/zu/wiki-ref
"""
import argparse
import json
import math
import random
import struct
import sys
from pathlib import Path

import numpy as np
from PIL import Image
from scipy import ndimage as ndi

ROOT = Path(__file__).resolve().parents[1]
DATA = ROOT / "data"
SOLID = {3, 5, 6, 8}  # WATER, TREE, ROCK, WALL (wie tools/derive-wiki-map.py)
WALL = 8

# ---------------------------------------------------------------------------------------------------------------
# Monster
# ---------------------------------------------------------------------------------------------------------------
# Spalten: id, Name, Level, Stufe, Wiki-EXP, Wiki-TP (None = nicht im Wiki), Look, Flags
#   Flags: a = aggressiv, r = Fernkampf, h = hungrig/mutlos (weniger Leben), s = Stein, b = Boss
# Looks: Sheet unter client/Assets/Resources/Sprites/Monsters (siehe data/monster-looks.json)
MOBS = [
    # ---- Yongan: Tiere ----
    (20, "Wildhund", 1, 1, 15, None, "dog", ""),
    (25, "Hungriger Wildhund", 1, 1, 11, None, "dog", "ah"),
    (21, "Wolf", 3, 1, 39, None, "wolf", ""),
    (22, "Alpha-Wolf", 4, 1, 51, None, "alpha_wolf", ""),
    (26, "Blauwolf", 6, 1, 75, None, "blue_wolf", ""),
    (27, "Hungriger Blauwolf", 6, 1, 53, None, "blue_wolf_c", "ah"),
    (28, "Alpha-Blauwolf", 9, 1, 111, None, "alpha_blue_wolf", ""),
    (29, "Grauwolf", 13, 1, 159, None, "grey_wolf", ""),
    (30, "Hungriger Grauwolf", 13, 1, 111, None, "grey_wolf_c", "ah"),
    (31, "Alpha-Grauwolf", 16, 2, 234, None, "alpha_grey_wolf", ""),
    (23, "Keiler", 7, 1, 87, None, "boar", ""),
    (32, "Roter Keiler", 10, 2, 148, None, "red_boar", ""),
    (33, "Hungriger Roter Keiler", 10, 2, 103, None, "red_boar_c", "ah"),
    (34, "Bär", 12, 1, 147, None, "brown_bear", ""),
    (35, "Grizzlybär", 15, 2, 220, None, "grizzly", ""),
    (36, "Schwarzbär", 19, 1, 385, None, "black_bear", ""),
    (37, "Braunbär", 21, 2, 510, None, "brown_bear", ""),
    (38, "Hungriger Braunbär", 21, 2, 357, None, "brown_bear_c", "ah"),
    (39, "Tiger", 18, 1, 365, None, "tiger", ""),
    (40, "Weißer Tiger", 24, 2, 582, None, "tiger", ""),
    (41, "Hungriger weißer Tiger", 24, 2, 407, None, "tiger_c", "ah"),
    # ---- Yongan: Weißer Eid (Halbmenschen) ----
    (10, "Weißer-Eid Soldatin", 18, 1, 365, 613, "woath", ""),
    (48, "Feiger Weißer-Eid-Soldat", 18, 2, 256, None, "woath", "h"),
    (11, "W.-Eid Bogenschützin", 20, 1, 405, 506, "woath_archer", "r"),
    (47, "Feiger W.-Eid Schütze", 20, 2, 284, None, "woath_archer", "rh"),
    (12, "Weißer-Eid Kommandantin", 25, 2, 848, 1115, "woath_cmd", "a"),
    (13, "Weißer-Eid Generälin", 25, 2, 848, 1115, "woath_gen", ""),
    (14, "Verfl. Weißer-Eid Soldatin", 18, 2, 558, 1450, "woath_c", "a"),
    (15, "Verfl. Weißer-Eid Bogenschützin", 20, 2, 606, 1622, "woath_archer_c", "ar"),
    (60, "Verfl. Weißer-Eid Kommandantin", 25, 3, 1452, 3207, "woath_cmd_c", "a"),
    (61, "Verfl. Weißer-Eid Generälin", 25, 3, 1452, 3207, "woath_gen_c", "a"),
    (42, "Mi-Jung", 23, 4, 2604, None, "woath_cmd", ""),
    (43, "Mutlose Mi-Jung", 23, 4, 1823, None, "woath_cmd_c", "h"),
    (44, "Eun-Jung", 26, 4, 2940, None, "woath_gen", ""),
    (45, "Se-Rang", 28, 4, 3164, None, "woath_gen_c", ""),
    (46, "Jin-Hee", 31, 4, 3500, None, "woath_gen", ""),
    # ---- Jayang: Schwarzwind und Sturm ----
    (62, "Böser Schw. Sturmsoldat", 26, 1, 515, 785, "evil_storm", ""),
    (63, "Schwarzwindsoldat", 26, 1, 735, 981, "bw_soldier", ""),
    (64, "Schwarzwindbesessener", 27, 1, 763, 1027, "bw_maniac", ""),
    (65, "Böser Sturmbogenschütze", 29, 1, 573, 655, "evil_storm_archer", "r"),
    (66, "Schwarzwindbogenschütze", 29, 1, 819, 818, "bw_archer", "r"),
    (67, "Böser Sturm-Joh-Hwan", 30, 2, 711, 1129, "evil_storm", ""),
    (68, "Schwarzwind-Jak-To", 30, 2, 1016, 1411, "bw_elite", ""),
    (69, "Schwarzer Sturmsoldat", 31, 3, 750, 1571, "black_storm", ""),
    (70, "Böser Sturm-Kyuk-Jang", 33, 2, 782, 1300, "evil_storm", ""),
    (71, "Schw. Sturmbesessener", 33, 3, 798, 1733, "black_storm", ""),
    (72, "Schwarzwind-To-Su", 33, 2, 1117, 1625, "bw_elite", ""),
    (73, "Böser Sturm-Pho-Hwan", 35, 2, 829, 1389, "evil_storm", ""),
    (74, "Schwarzer Bogenschütze", 35, 3, 846, 1852, "black_storm_archer", "r"),
    (75, "Schwarzwind-Gu-Ryung", 35, 2, 1184, 1736, "bw_elite", ""),
    (76, "Schw. Sturm-Joh-Hwan", 36, 4, 1740, 3010, "black_storm", ""),
    (77, "Schw. Sturm-Kyuk-Jan", 38, 4, 1836, 3249, "black_storm", ""),
    (78, "Schw. Sturm-Pho-Hwan", 40, 4, 1932, 3500, "black_storm", ""),
    (79, "Mahon", 32, 4, 3612, 3201, "bw_leader", ""),
    (80, "Bo", 37, 4, 4172, 3911, "bw_leader", ""),
    (81, "Goo-Pae", 39, 4, 4396, 4219, "bw_leader", ""),
    (82, "Chuong", 45, 4, 5068, 5220, "bw_chief", ""),
    # ---- Jayang: Wilde und Brutale ----
    (83, "St. Wilder Infanterist", 29, 1, 819, 1123, "st_wild", ""),
    (84, "Wilder Infanterist", 29, 1, 819, 1404, "wild", ""),
    (85, "Wilder Ergebener", 32, 1, 903, 1625, "wild", ""),
    (86, "St. Wilder Ergebener", 32, 1, 903, 1300, "st_wild", ""),
    (87, "St. Wilder Bogenschütze", 35, 2, 1184, 1010, "st_wild_archer", "r"),
    (88, "Wilder Bogenschütze", 35, 2, 1184, 1262, "wild_archer", "r"),
    (89, "Wilder General", 36, 3, 2030, 2240, "wild_general", ""),
    (90, "St. Wilder General", 36, 3, 2030, 1792, "st_wild_general", ""),
    (91, "Brutaler Soldat", 35, 3, 513, 1852, "brutal", "a"),
    (92, "Brutaler Besessener", 37, 3, 542, 2002, "brutal", "a"),
    (93, "Brutaler Bogenschütze", 40, 4, 1172, 3500, "brutal_archer", "ar"),
    (94, "Brutaler Spezialist", 42, 4, 1230, 3762, "brutal", "a"),
    (95, "Brutaler Hauptmann", 42, 6, 20280, 18810, "brutal_captain", "ab"),
]

# Metinsteine (Wiki: Level; Lebenspunkte/Verteidigung/EXP eigene Wahl, an lumiis-2d angelehnt x 0,85)
STONES = [
    (24, "Lumiis des Kummers", 5, 3825, 6, 900, "metin_sorrow"),
    (50, "Lumiis des Kampfes", 10, 7650, 12, 2400, "metin_battle"),
    (51, "Lumiis der Schlacht", 15, 11900, 17, 5000, "metin_black"),
    (52, "Lumiis der Gier", 20, 17850, 22, 9000, "metin_greed"),
    (18, "Lumiis der Schwärze", 25, 23800, 27, 12000, "metin_blackness"),
    (53, "Lumiis der Dunkelheit", 30, 29750, 32, 16000, "metin_darkness"),
    (54, "Lumiis der Eifersucht", 35, 35700, 37, 20000, "metin_jealousy"),
]

WAVES = {
    24: [(75, 21, 2), (50, 22, 2), (25, 20, 3)],
    50: [(75, 26, 3), (50, 28, 2), (25, 23, 2)],
    51: [(75, 29, 3), (50, 32, 2), (25, 35, 1)],
    52: [(75, 36, 2), (50, 39, 2), (25, 37, 1)],
    18: [(75, 10, 4), (50, 11, 3), (25, 12, 2)],
    53: [(75, 63, 4), (50, 66, 3), (25, 69, 2)],
    54: [(75, 84, 4), (50, 88, 3), (25, 75, 2)],
}

STONE_DROPS = {
    24: [(8, 100), (9, 100), (45, 25), (49, 20), (50, 15)],
    50: [(9, 100), (21, 25), (26, 20), (44, 25), (35, 30)],
    51: [(9, 100), (23, 20), (27, 20), (45, 25), (36, 20)],
    52: [(9, 100), (10, 10), (22, 15), (24, 15), (50, 25), (36, 30)],
    18: [(9, 100), (10, 15), (22, 15), (24, 15), (48, 20), (36, 30)],
    53: [(9, 100), (10, 20), (22, 20), (24, 20), (46, 25), (36, 40)],
    54: [(9, 100), (10, 30), (22, 25), (24, 25), (47, 25), (48, 20), (36, 50)],
}

# Laufgeschwindigkeit je Look-Familie (Wire-Einheiten/s)
SPEED = {"dog": 420, "wolf": 448, "alpha_wolf": 448, "blue_wolf": 462, "alpha_blue_wolf": 462, "grey_wolf": 476,
         "alpha_grey_wolf": 476, "boar": 420, "red_boar": 434, "brown_bear": 378, "black_bear": 378, "grizzly": 392,
         "tiger": 504}
COOLDOWN = {"brown_bear": 1900, "black_bear": 1900, "grizzly": 1900, "tiger": 1300, "boar": 1700, "red_boar": 1700}

# Größe je Look (aus monster.json "groessen" des Asset-Archivs)
LOOK_SCALE = {"dog": 0.85, "wolf": 1, "alpha_wolf": 1.15, "boar": 1, "blue_wolf": 1, "alpha_blue_wolf": 1.15,
              "grey_wolf": 1.05, "alpha_grey_wolf": 1.15, "brown_bear": 1.3, "red_boar": 1.1, "black_bear": 1.3,
              "grizzly": 1.45, "tiger": 1.2, "woath_cmd_c": 1.1, "woath_gen_c": 1.1, "bw_leader": 1.2, "bw_chief": 1.2,
              "black_storm": 1.2, "black_storm_archer": 1.1, "wild_general": 1.1, "st_wild_general": 1.1, "brutal": 1.1,
              "brutal_archer": 1.2, "brutal_captain": 1.5}
LOOK_SCALE.update({k + "_c": v for k, v in list(LOOK_SCALE.items()) if not k.endswith("_c")})


def base_look(look):
    return look[:-2] if look.endswith("_c") else look


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


def animal_hp(level):
    """Map1-Tiere: das Wiki nennt keine TP. Kurve an die bisherigen Map1-Werte (lumiis-2d x 0,85) angelehnt."""
    return 0.85 * (60 + 75 * level + 2.2 * level * level)


def build_mob(row):
    mid, name, level, stufe, wexp, tp, look, flags = row
    sf = 1 + 0.1 * (stufe - 1)
    human = not base_look(look) in SPEED
    if tp is not None:
        hp = tp * 2.5
    else:
        hp = animal_hp(level) * {1: 1.0, 2: 1.35, 3: 1.7, 4: 2.6}[stufe]
        if "h" in flags:
            hp *= 0.75
    if "b" in flags:
        hp = tp * 2.5
    ranged = "r" in flags
    aggressive = "a" in flags
    bl = base_look(look)
    return {
        "id": mid, "name": name,
        "speedWire": SPEED.get(bl, 450),
        "wanderRadiusWire": 150 if "b" in flags else 400,
        "wanderIntervalMinMs": 3000, "wanderIntervalMaxMs": 7000,
        "maxHp": int(round(hp)),
        "attack": int(round(max(3.6 * level + 7.4, 4 * level + 4) * sf * (1.3 if "b" in flags else 1))),
        "defense": max(1, int(round(1.5 * level * sf))),
        "yangMin": 3 * level, "yangMax": 6 * level * (10 if "b" in flags else 1),
        "meleeRangeWire": 350 if ranged else (200 if "b" in flags else 150),
        "attackCooldownMs": 1800 if ranged else COOLDOWN.get(bl, 1500),
        "aggressive": aggressive,
        "aggroRangeWire": 500 if aggressive else 0,
        "leashRangeWire": 900,
        "level": level,
        "baseXp": max(1, int(round(wexp * 0.8))),
        "drops": [{"itemId": i, "chancePercent": c} for i, c in
                  ([(9, 100), (10, 50), (22, 40), (24, 40), (36, 100), (47, 30)] if "b" in flags else drops_for(level, human))],
    }


def build_stone(row):
    mid, name, level, hp, defense, xp, sheet = row
    return {
        "id": mid, "name": name, "speedWire": 1, "wanderRadiusWire": 1,
        "wanderIntervalMinMs": 60000, "wanderIntervalMaxMs": 60000,
        "maxHp": hp, "attack": 0, "defense": defense,
        "yangMin": 40 * level, "yangMax": 80 * level,
        "meleeRangeWire": 180, "attackCooldownMs": 1400,
        "aggressive": False, "aggroRangeWire": 0, "leashRangeWire": 200,
        "level": level, "baseXp": xp,
        "drops": [{"itemId": i, "chancePercent": c} for i, c in STONE_DROPS[mid]],
        "fightsBack": False,
    }


def respawn_for(mob, stone=False):
    if stone:
        return 300
    lvl = mob["level"]
    if mob["id"] == 95:
        return 1200
    if mob["id"] in (79, 80, 81, 82):
        return 1200
    row = MOB_BY_ID[mob["id"]]
    if row[3] >= 4:
        return 120
    return 15 if lvl < 10 else 25 if lvl < 20 else 35


MOB_BY_ID = {r[0]: r for r in MOBS}

# ---------------------------------------------------------------------------------------------------------------
# Karten
# ---------------------------------------------------------------------------------------------------------------
# Hintergrundfarben der Wiki-Karten (Gelände ohne Gebietsfarbe), für die Klassifizierung der Gebietsflächen
BASES_YONGAN = [[120, 115, 98], [70, 88, 50], [60, 80, 85], [150, 140, 120], [45, 55, 35]]
BASES_JAYANG = [[51, 44, 20], [77, 66, 28], [102, 86, 57], [22, 17, 4], [133, 114, 85], [161, 139, 108],
                [185, 166, 138], [74, 91, 86], [252, 247, 235]]

MAPS = {
    "yongan": {
        "out": "map1", "name": "Yongan",
        "plaza": (646, 585), "safe": 50, "ring": 85,
        "zoneImage": "Yongan_2.jpg", "zoneRegion": (28, 95, 550, 608), "zoneAlpha": 0.55, "bases": BASES_YONGAN,
        # Bild-Pixel der Monsterkarte -> Wiki-Koordinaten (aus Passpunkten bestimmt)
        "zoneAffine": ((1.936, -0.119, -10.0), (0.0365, 2.508, -252.4)),
        # Legende der Monsterkarte: Farbe -> Monster (Gewichte)
        "zones": [
            ((120, 121, 153), {10: 4, 11: 3, 48: 2}),                # Weißer Eid
            ((182, 182, 229), {47: 3, 11: 2, 44: 0.4}),              # Eun-Jung, Feige W.-Eid Schützen
            ((75, 72, 94), {42: 1, 43: 1, 45: 0.6, 46: 0.4}),        # (Mutlose) Mi-Jung, Se-Rang, Jin-Hee
            ((35, 88, 125), {40: 3, 41: 2, 37: 2}),                  # (Hungrige) Weiße Tiger, Braunbären
            ((102, 166, 174), {39: 3, 36: 3, 31: 2, 38: 1}),         # Tiger, Schwarzbär, Alpha-Grauwolf, Hungr. Braunbär
            ((182, 208, 118), {33: 2, 35: 3, 34: 3}),                # Hungrige Rote Keiler, Grizzly, Bär
            ((240, 212, 184), {32: 3, 23: 1, 30: 2, 29: 2}),         # Rote Keiler, Keiler, (Hungriger) Grauwolf
            ((152, 88, 30), {27: 2, 28: 3, 23: 2, 26: 3}),           # (Hungriger) Blauwolf, Alpha-Blauwolf, Keiler
            ((209, 99, 86), {27: 2, 22: 3, 23: 3}),                  # Hungriger Blauwolf, Alpha-Wolf, Keiler
            ((73, 75, 68), {25: 3, 21: 1}),                          # Hungrige Wildhunde
        ],
        "beginner": {20: 4, 21: 3, 25: 1, 22: 1},
        "zoneRules": {"westOnly": [0, 1], "westMaxX": 420, "nearVillage": [9], "nearVillageR": 115},
        "density": 1 / 720,   # Gruppen je begehbarer Kachel
        "metinImage": "Yongan-Metinkarte.jpg", "metinScale": 0.903, "metinAlpha": 0.45,
        "metins": [((23, 128, 9), 24, 4), ((44, 229, 215), 50, 5), ((234, 245, 67), 51, 6),
                   ((229, 45, 205), 52, 5), ((192, 24, 24), 18, 4)],
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
        "plaza": (458, 634), "safe": 45, "ring": 0,
        "zoneImage": "Jayang_2.jpg", "zoneRegion": (30, 95, 542, 612), "zoneAlpha": 0.55, "bases": BASES_JAYANG,
        "zoneAffine": ((2.0387, 0.0123, -69.91), (0.0491, 1.9354, -172.61)),
        "zones": [
            ((100, 114, 161), {10: 4, 11: 3, 14: 2, 15: 1, 60: 0.25, 61: 0.25}),   # Weißer Eid
            ((51, 204, 202), {83: 3, 73: 2, 70: 2, 86: 3, 90: 0.4}),              # St. Wilde, Sturm-Pho-Hwan/-Kyuk-Jang
            ((52, 102, 249), {87: 2, 88: 2, 89: 0.6, 85: 3, 84: 3}),              # Wilde
            ((242, 80, 69), {84: 3, 63: 3, 67: 2, 65: 2, 62: 2}),                 # Wilder Inf., Schwarzwind, Böser Sturm
            ((247, 140, 79), {85: 3, 64: 3, 66: 2, 62: 3}),                       # Wilde Ergebene, Schwarzwind
            ((182, 210, 105), {68: 3, 75: 2, 72: 2, 73: 2, 69: 0.6, 71: 0.5, 74: 0.5}),  # Jak-To, Gu-Ryung, To-Su
        ],
        "zoneRules": {"eastOnly": [0], "eastMinX": 760},
        "density": 1 / 560,
        # Einzelgänger: Brutale (Wiki: meist allein) und Anführer der Schwarzwind-Gruppen
        "singles": [(91, 1, [1, 2]), (92, 3, [1, 2]), (93, 3, [1, 2]), (94, 3, [1, 2]),
                    (79, 1, [5]), (80, 1, [5]), (81, 1, [5]), (82, 1, [5]),
                    (76, 2, [5]), (77, 2, [5]), (78, 2, [5])],
        "boss": (95, 727, 77),
        "metinImage": "Jayang-Metinkarte.jpg", "metinScale": 0.7227, "metinAlpha": 0.6,
        "metins": [((250, 212, 3), 18, 6), ((140, 2, 2), 53, 6), ((143, 3, 250), 54, 6)],
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


def classify(img_path, region, alpha, swatches, bases):
    im = np.asarray(Image.open(img_path).convert("RGB")).astype(np.float32)
    x0, y0, x1, y1 = region
    im = ndi.uniform_filter(im[y0:y1, x0:x1], (5, 5, 1))
    cands, labels = [], []
    for b in bases:
        cands.append(b)
        labels.append(0)
    for zi, c in enumerate(swatches):
        for b in bases:
            cands.append(alpha * np.array(c, np.float32) + (1 - alpha) * np.array(b, np.float32))
            labels.append(zi + 1)
    cands = np.array(cands, np.float32)
    labels = np.array(labels)
    lab = np.zeros(im.shape[:2], np.int64)
    for r in range(0, im.shape[0], 128):
        blk = im[r:r + 128]
        d = ((blk[:, :, None, :] - cands[None, None]) ** 2).sum(-1)
        lab[r:r + 128] = labels[d.argmin(-1)]
    n = len(swatches) + 1
    cnt = np.stack([ndi.uniform_filter((lab == i).astype(np.float32), 11) for i in range(n)])
    return cnt.argmax(0)


def fill_nearest(z, mask_valid, mask_target, max_dist=30):
    """Setzt in mask_target jede Kachel ohne gültiges Gebiet auf das Gebiet der nächsten gültigen Kachel, sofern
    diese höchstens max_dist Kacheln entfernt ist (Lücken der Klassifizierung schließen, leere Wege bleiben leer)."""
    if not mask_valid.any():
        return z
    dist, (iy, ix) = ndi.distance_transform_edt(~mask_valid, return_indices=True)
    target = mask_target & (dist <= max_dist)
    out = z.copy()
    out[target] = z[iy[target], ix[target]]
    return out


def zone_tiles(cfg, g, ref):
    lab = classify(ref / cfg["zoneImage"], cfg["zoneRegion"], cfg["zoneAlpha"], [c for c, _ in cfg["zones"]],
                   cfg["bases"])
    (a, b, c), (d, e, f) = cfg["zoneAffine"]
    inv = np.linalg.inv(np.array([[a, b], [d, e]]))
    ty, tx = np.mgrid[0:g.h, 0:g.w]
    wx, wy = 2 * tx + 1 - c, 2 * ty + 1 - f
    px = inv[0, 0] * wx + inv[0, 1] * wy - cfg["zoneRegion"][0]
    py = inv[1, 0] * wx + inv[1, 1] * wy - cfg["zoneRegion"][1]
    pxi, pyi = np.round(px).astype(int), np.round(py).astype(int)
    inside = (pxi >= 0) & (pyi >= 0) & (pxi < lab.shape[1]) & (pyi < lab.shape[0])
    z = np.zeros((g.h, g.w), np.int64)
    z[inside] = lab[pyi[inside], pxi[inside]]
    z[~g.walk] = 0

    pcx, pcy = cfg["plaza"][0] / 2, cfg["plaza"][1] / 2
    dist = np.hypot(tx - pcx, ty - pcy)
    rules = cfg["zoneRules"]
    for k in rules.get("westOnly", []):
        z[(z == k + 1) & (2 * tx > rules["westMaxX"])] = 0
    for k in rules.get("eastOnly", []):
        z[(z == k + 1) & (2 * tx < rules["eastMinX"])] = 0
    for k in rules.get("nearVillage", []):
        z[(z == k + 1) & (dist > rules["nearVillageR"])] = 0
    # kleine Splitter (Klassifizierungsrauschen) entfernen
    for k in range(1, len(cfg["zones"]) + 1):
        comp, n = ndi.label(z == k)
        if n:
            sizes = ndi.sum(np.ones_like(comp), comp, range(1, n + 1))
            small = np.isin(comp, np.nonzero(sizes < 150)[0] + 1)
            z[small] = 0
    z = fill_nearest(z, z > 0, g.walk & (z == 0))
    z[~g.walk] = 0
    if cfg["ring"]:
        z[g.walk & (dist <= cfg["ring"])] = 99  # Anfängergebiet rund ums Dorf
    z[dist <= cfg["safe"]] = 0                # Dorf selbst bleibt frei
    return z, lab


def poisson_centers(mask, n, min_d, rng):
    ys, xs = np.nonzero(mask)
    if len(xs) == 0 or n <= 0:
        return []
    order = rng.permutation(len(xs))
    chosen = []
    cell = {}
    cs = max(1, int(min_d))
    for i in order:
        x, y = int(xs[i]), int(ys[i])
        gx, gy = x // cs, y // cs
        ok = True
        for nx in range(gx - 1, gx + 2):
            for ny in range(gy - 1, gy + 2):
                for (px, py) in cell.get((nx, ny), ()):
                    if (px - x) ** 2 + (py - y) ** 2 < min_d * min_d:
                        ok = False
                        break
                if not ok:
                    break
            if not ok:
                break
        if ok:
            chosen.append((x, y))
            cell.setdefault((gx, gy), []).append((x, y))
            if len(chosen) >= n:
                break
    return chosen


def group_tiles(g, cx, cy, count, taken, rng):
    tiles = []
    cand = [(cx + dx, cy + dy) for dy in range(-2, 3) for dx in range(-2, 3)]
    rng.shuffle(cand)
    cand.sort(key=lambda p: (p[0] - cx) ** 2 + (p[1] - cy) ** 2 + rng.random() * 3)
    for x, y in cand:
        if 0 <= x < g.w and 0 <= y < g.h and g.walk[y, x] and (x, y) not in taken:
            tiles.append((x, y))
            if len(tiles) == count:
                break
    return tiles


def pick(weights, rng):
    keys = list(weights)
    w = np.array([weights[k] for k in keys], float)
    return keys[rng.choice(len(keys), p=w / w.sum())]


def metin_tiles(cfg, g, ref, z_safe):
    img = Image.open(ref / cfg["metinImage"]).convert("RGB")
    W, H = img.size
    lab = classify(ref / cfg["metinImage"], (0, 0, W, H), cfg["metinAlpha"], [c for c, _, _ in cfg["metins"]],
                   cfg["bases"])
    if cfg["out"] == "map1":
        # Gestreifte Flächen der Yongan-Karte: grüne Streifen im Süden markieren den Lumiis des Kampfes
        yy = np.arange(H)[:, None]
        lab[(lab == 1) & (yy > 600)] = 2
    ty, tx = np.mgrid[0:g.h, 0:g.w]
    px = np.clip(((2 * tx + 1) / cfg["metinScale"]).astype(int), 0, W - 1)
    py = np.clip(((2 * ty + 1) / cfg["metinScale"]).astype(int), 0, H - 1)
    return lab[py, px] * (g.walk & z_safe)


def farthest_points(mask, n, rng, avoid=()):
    ys, xs = np.nonzero(mask)
    if len(xs) == 0:
        return []
    pts = np.stack([xs, ys], 1).astype(float)
    chosen = []
    dmin = np.full(len(pts), np.inf)
    for a in avoid:
        dmin = np.minimum(dmin, ((pts - a) ** 2).sum(1))
    if not avoid:
        i = rng.integers(len(pts))
    else:
        i = int(dmin.argmax())
    for _ in range(n):
        chosen.append((int(pts[i, 0]), int(pts[i, 1])))
        dmin = np.minimum(dmin, ((pts - pts[i]) ** 2).sum(1))
        i = int(dmin.argmax())
        if dmin[i] < 12 * 12:
            break
    return chosen


def build_map(key, cfg, ref, mobs_by_id, npc_ids, preview_dir):
    g = Grid(cfg["out"])
    rng = np.random.default_rng(20261025 + len(key))
    z, _ = zone_tiles(cfg, g, ref)
    spawns = []
    taken = set()
    sid = 1

    def add(mob_id, tx, ty, stone=False):
        nonlocal sid
        x, y = g.wire(tx, ty)
        spawns.append({"id": sid, "mobId": mob_id, "positionX": x, "positionY": y,
                       "respawnSeconds": respawn_for(mobs_by_id[mob_id], stone)})
        taken.add((tx, ty))
        sid += 1

    pcx, pcy = g.snap(cfg["plaza"][0] // 2, cfg["plaza"][1] // 2)

    # Trainingspuppen am Westrand des Dorfplatzes (bestehendes Feature, Mob 6)
    if cfg["dummies"]:
        for i in range(3):
            x, y = g.snap(pcx - 9, pcy - 2 + 2 * i, taken)
            spawns.append({"id": sid, "mobId": 6, "positionX": g.wire(x, y)[0], "positionY": g.wire(x, y)[1],
                           "respawnSeconds": 5})
            taken.add((x, y))
            sid += 1

    # Metinsteine zuerst, damit die Gruppen ihnen ausweichen
    ty, tx = np.mgrid[0:g.h, 0:g.w]
    dist = np.hypot(tx - pcx, ty - pcy)
    mlab = metin_tiles(cfg, g, ref, dist > cfg["safe"] + 10)
    stone_pts = []
    for k, (_, mob_id, n) in enumerate(cfg["metins"]):
        pts = farthest_points(mlab == k + 1, n, rng, avoid=[np.array(p, float) for p in stone_pts])
        if len(pts) < n:
            print(f"  Warnung: {mobs_by_id[mob_id]['name']} nur {len(pts)}/{n} Plätze", file=sys.stderr)
        for x, y in pts:
            add(mob_id, x, y, stone=True)
            stone_pts.append((x, y))
    near_stone = np.zeros((g.h, g.w), bool)
    for x, y in stone_pts:
        near_stone[max(0, y - 4): y + 5, max(0, x - 4): x + 5] = True

    # Boss
    if cfg.get("boss"):
        mob_id, wx, wy = cfg["boss"]
        x, y = g.snap(wx // 2, wy // 2, taken)
        add(mob_id, x, y)

    # Gruppen je Gebiet
    zone_defs = {k + 1: w for k, (_, w) in enumerate(cfg["zones"])}
    if cfg.get("beginner"):
        zone_defs[99] = cfg["beginner"]
    stats = {}
    for zid, weights in zone_defs.items():
        mask = (z == zid) & ~near_stone
        area = int(mask.sum())
        special = all(MOB_BY_ID[m][3] >= 4 for m in weights)
        factor = 0.5 if special else 1.6 if zid == 99 else 1.0
        n = max(len(weights), int(round(area * cfg["density"] * factor))) if area > 300 else 0
        centers = poisson_centers(mask, n, 9 if not special else 14, rng)
        order = list(weights)
        for gi, (cx, cy) in enumerate(centers):
            # jede Monsterart der Legende kommt mindestens einmal vor, danach nach Gewicht
            mob_id = order[gi] if gi < len(order) else pick(weights, rng)
            stufe = MOB_BY_ID[mob_id][3]
            count = 1 if stufe >= 4 else int(rng.integers(2, 4 if MOB_BY_ID[mob_id][5] else 5))
            for x, y in group_tiles(g, cx, cy, count, taken, rng):
                add(mob_id, x, y)
                stats[mob_id] = stats.get(mob_id, 0) + 1
        stats.setdefault(("zone", zid), (area, len(centers)))

    # Einzelgänger (Jayang)
    for mob_id, n, zone_ids in cfg.get("singles", []):
        mask = np.isin(z, zone_ids) & ~near_stone
        for x, y in poisson_centers(mask, n, 30, rng):
            if (x, y) not in taken:
                add(mob_id, x, y)

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
        cols = np.array([[0, 0, 0]] + [list(c) for c, _ in cfg["zones"]], np.uint8)
        im = np.where(g.walk[..., None], 40, 10).astype(np.uint8).repeat(3, 2)
        zz = np.where(z == 99, 0, z)
        im[zz > 0] = cols[zz[zz > 0]]
        im[z == 99] = [255, 255, 255]
        for s in spawns:
            cx = s["positionX"] // 100 - g.ox
            cy = g.h - 1 - (s["positionY"] // 100 - g.oy)
            im[cy, cx] = [255, 0, 255] if s["respawnSeconds"] == 300 else [255, 255, 0]
        Image.fromarray(im).resize((g.w * 2, g.h * 2), Image.NEAREST).save(Path(preview_dir) / f"{key}-spawns.png")

    print(f"{key}: {len(spawns)} Spawns ({sum(1 for s in spawns if s['respawnSeconds'] == 300)} Steine), "
          f"{len(placements)} NPCs, Start {g.wire(pcx, pcy)}")
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


def monster_looks(mobs):
    looks = [
        {"mobId": 1, "sheet": "alpha_wolf"}, {"mobId": 2, "sheet": "boar"},
        {"mobId": 3, "sheet": "metin_sorrow", "stone": True}, {"mobId": 4, "sheet": "bw_leader", "scale": 1.3},
        {"mobId": 5, "sheet": "alpha_wolf", "tint": "#ffb4a0"}, {"mobId": 6, "sheet": "dummy"},
    ]
    for r in MOBS:
        mid, name, *_ , look, flags = r
        e = {"mobId": mid, "sheet": look}
        s = LOOK_SCALE.get(look, 1.0)
        if mid in (42, 43, 44, 45, 46):
            s *= 1.1
        if mid in (79, 80, 81):
            s = {79: 1.15, 80: 1.2, 81: 1.25}[mid]
        if s != 1.0:
            e["scale"] = round(s, 2)
        if mid in (40, 41):
            e["desaturate"] = True
        if mid == 34:
            e["tint"] = "#e8c8a0"  # Bär: helleres Fell als der Braunbär
        if mid == 25:
            e["tint"] = "#ffb4a0"  # hungrig: rötlich (kein eigenes Sheet für den Hund)
        looks.append(e)
    for mid, name, level, hp, d, xp, sheet in STONES:
        looks.append({"mobId": mid, "sheet": sheet, "stone": True})
    looks.sort(key=lambda e: e["mobId"])
    return looks


def dump(path, obj):
    path.write_text(json.dumps(obj, ensure_ascii=False, indent=2) + "\n", encoding="utf-8", newline="\n")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("ref", help="Ordner mit den Wiki-Bildern (Yongan_2.jpg, Jayang_2.jpg, *-Metinkarte.jpg)")
    ap.add_argument("--preview", help="Ordner für Kontrollbilder")
    a = ap.parse_args()
    ref = Path(a.ref)

    # --- Mobs ---
    mobs_doc = json.loads((DATA / "mobs.json").read_text(encoding="utf-8"))
    keep = [m for m in mobs_doc["mobs"] if m["id"] <= 6]
    new = [build_mob(r) for r in MOBS] + [build_stone(r) for r in STONES]
    all_mobs = sorted(keep + new, key=lambda m: m["id"])
    ids = [m["id"] for m in all_mobs]
    assert len(ids) == len(set(ids)), "doppelte Mob-Id"
    mobs_doc["mobs"] = all_mobs
    mobs_by_id = {m["id"]: m for m in all_mobs}

    metins_doc = json.loads((DATA / "metins.json").read_text(encoding="utf-8"))
    metins_doc["metins"] = [m for m in metins_doc["metins"] if m["mobId"] == 3] + [
        {"mobId": sid, "waves": [{"hpPercent": p, "mobId": w, "count": c} for p, w, c in WAVES[sid]]}
        for sid, *_ in STONES]

    npcs, npc_ids = npc_types()

    maps = {}
    for key, cfg in MAPS.items():
        maps[key] = (cfg,) + build_map(key, cfg, ref, mobs_by_id, npc_ids, a.preview)

    by_out = {cfg["out"]: v for cfg, *v in maps.values()}
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
        "looks": monster_looks(all_mobs)})


if __name__ == "__main__":
    main()
