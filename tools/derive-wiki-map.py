#!/usr/bin/env python3
"""Leitet eine komplette Karte (Gelände, Bäume, Häuser, Kollision) aus der Übersichtskarte im Metin2-Wiki ab.

Die Wiki-Übersichtskarten haben 1 Bildpunkt = 1 Wiki-Einheit (Yongan 1024 x 1280, Jayang 1024 x 1024). Wir bauen die
Karten im Maßstab 2 Wiki-Einheiten je Kachel (wie lumiis-2d): Yongan 512 x 640, Jayang 512 x 512 Kacheln. Jede Kachel ist
eine Kollisionszelle; die Karte liegt mit einem Versatz (origin) im globalen 1000 x 1000-Raster des Servers.

Das Wiki-Bild selbst kommt NICHT ins Repo (Konvention: keine Original-Assets/Referenzbilder). Es wird nur gelesen, um den
Grundriss in unsere eigenen Geländeklassen zu übersetzen; gezeichnet wird alles mit unserer eigenen Pixelart (Client,
TerrainArt). Ergebnis (committet, Quelle der Wahrheit):
  data/maps/<map>/terrain.bin   "LTR1", u16 Breite, u16 Höhe, i16 originX, i16 originY, dann Breite*Höhe Kachel-Bytes
                                (Zeile 0 = Norden/oben im Wiki-Bild; Kachel-IDs wie lumiis-2d shared/map.js, siehe TILE_*)
  data/maps/<map>/props.json    Häuser, Laternen, Brunnen, Denkmäler, Stege (Kachelkoordinaten, Zeile 0 = Norden)
  data/maps/<map>/collision.bin Kollisionskarte 1000 x 1000 (Format wie tools/bake-collision.py)

Klassifizierung: jeder Bildpunkt wird dem nächsten von 15 Farbzentren zugeordnet (einmal per k-Means aus dem jeweiligen
Bild bestimmt und hier fest eingetragen), jedes Zentrum ist einer Geländeklasse zugeordnet. Danach je Kachel ein 4 x 4-
Fenster ausgezählt, das begehbare Land als zusammenhängende Fläche ab dem Dorfplatz bestimmt, Baumgruppen zu einzelnen
Bäumen und rote Dachflächen zu Häusern zusammengefasst.

Aufruf (numpy, scipy, Pillow):
  python3 tools/derive-wiki-map.py yongan /pfad/zu/Yongan.jpg
  python3 tools/derive-wiki-map.py jayang /pfad/zu/Jayang.png
Optional --preview /tmp/vorschau.png schreibt ein Klassenbild zur Kontrolle.
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
GRID = 1000  # globale Kartengröße in Zellen (GameConstants.MapWidth / 100)

# Kachel-IDs (wie lumiis-2d shared/map.js, gleiche Zahlen wie tools/import-map1.py)
GRASS, FLOWERS, PATH, WATER, SAND, TREE, ROCK, STONE, WALL, BRIDGE, PORTAL = range(11)
SOLID = {WATER, TREE, ROCK, WALL}

# Bildpunkt-Klassen der Klassifizierung
P_GRASS, P_WATER, P_PATH, P_ROCK, P_DARK, P_PLAZA, P_ROOF, P_SAND = range(8)

MAPS = {
    "yongan": {
        "out": "map1",
        "wiki": (1024, 1280),
        # Dorfplatz (Wiki: Wächter des Dorfplatzes 647,582) als Startpunkt für die Landfläche
        "seed": (646, 582),
        # Im Wiki-Bild ist das Dorfgebiet aufgehellt: eigene Farbzentren innerhalb dieses Rechtecks
        "regions": [
            {
                "rect": (512, 256, 967, 767),
                "centers": [[84.5, 96.3, 39.3], [91.4, 90.9, 85.2], [119.3, 118.9, 108.4], [60.5, 67.4, 27.5],
                            [137.5, 134.5, 119.2], [77.4, 98.5, 92.1], [106.3, 100.7, 59.5], [81.2, 58.5, 39.5],
                            [70.3, 71.9, 69.7], [124.9, 110.8, 73.3], [43.4, 45.2, 14.5], [106.4, 106.3, 97.0],
                            [46.2, 45.6, 46.8], [147.6, 131.3, 90.2], [76.0, 92.3, 30.4]],
                "classes": [P_GRASS, P_ROCK, P_ROCK, P_DARK, P_PLAZA, P_WATER, P_ROCK, P_ROOF,
                            P_ROCK, P_PATH, P_DARK, P_ROCK, P_ROCK, P_PATH, P_GRASS],
            },
            {
                "rect": None,
                "centers": [[96.0, 79.0, 47.9], [48.8, 63.1, 36.3], [132.9, 125.2, 106.0], [76.0, 98.3, 92.4],
                            [105.7, 102.2, 84.8], [30.0, 35.8, 19.9], [57.1, 70.5, 43.5], [160.9, 151.3, 133.1],
                            [73.9, 72.0, 48.6], [43.1, 48.7, 31.5], [88.9, 86.1, 68.8], [120.0, 114.0, 95.4],
                            [145.7, 136.7, 117.5], [118.8, 93.5, 63.4], [13.4, 17.3, 7.2]],
                "classes": [P_PATH, P_GRASS, P_ROCK, P_WATER, P_ROCK, P_DARK, P_GRASS, P_ROCK,
                            P_ROCK, P_DARK, P_ROCK, P_ROCK, P_ROCK, P_PATH, P_DARK],
            },
        ],
        "sand": True,
        "plaza": None,  # Pflaster kommt aus der Klassifizierung (heller Steinkreis)
        # Brücken und Stege aus dem Wiki-Bild abgelesen (Wiki-Koordinaten Anfang/Ende); die dünnen Planken überlebt die
        # Farbklassifizierung nicht, deshalb von Hand
        "bridges": [(358, 498, 401, 498), (211, 700, 285, 700), (175, 1086, 248, 1086), (279, 1009, 327, 1009),
                    (487, 261, 487, 338), (598, 283, 598, 332), (746, 341, 726, 369), (748, 597, 780, 597),
                    (768, 1120, 857, 1120)],
        "piers": [(304, 1178, 304, 1212), (490, 1137, 490, 1170), (591, 1166, 591, 1199), (249, 868, 274, 882),
                  (815, 291, 815, 323), (692, 348, 677, 373), (319, 582, 319, 620)],
    },
    "jayang": {
        "out": "map2",
        "wiki": (1024, 1024),
        # Dorfplatz (Wiki: Wächter des Dorfplatzes 458,631)
        "seed": (458, 631),
        "regions": [
            {
                "rect": None,
                "centers": [[158.2, 139.5, 109.9], [67.2, 61.8, 19.2], [99.0, 84.0, 55.9], [43.9, 40.3, 22.6],
                            [67.7, 88.9, 81.8], [128.0, 111.6, 82.8], [91.4, 75.1, 39.4], [143.2, 125.1, 95.6],
                            [54.4, 50.3, 34.4], [114.2, 95.7, 67.2], [29.2, 27.8, 9.9], [56.1, 52.0, 17.2],
                            [76.8, 68.9, 25.6], [74.2, 67.2, 46.9], [174.8, 156.9, 128.1]],
                "classes": [P_ROCK, P_GRASS, P_ROCK, P_DARK, P_WATER, P_ROCK, P_GRASS, P_ROCK,
                            P_ROCK, P_PATH, P_DARK, P_GRASS, P_GRASS, P_ROCK, P_ROCK],
            },
        ],
        "sand": False,
        # Runder gepflasterter Dorfplatz (Wiki-Koordinaten Mitte, Radius)
        "plaza": (458, 634, 21),
        # Jayangs Dächer sind dunkel und gehen in der Farbklassifizierung unter: Häuser von Hand (Wiki-Bildpunkte)
        "houses": [(435, 583, 485, 605), (400, 592, 422, 622), (440, 550, 453, 563), (485, 665, 502, 682)],
        "bridges": [(194, 72, 265, 72), (431, 203, 431, 277), (717, 275, 717, 322), (829, 142, 918, 142),
                    (258, 355, 282, 355), (240, 457, 301, 457), (234, 569, 293, 569), (686, 452, 760, 452),
                    (196, 720, 250, 770), (463, 696, 463, 722), (467, 783, 467, 859), (690, 549, 744, 563)],
        "piers": [],
    },
}


def classify_pixels(img, cfg):
    h, w, _ = img.shape
    pc = np.full((h, w), P_ROCK, np.uint8)
    done = np.zeros((h, w), bool)
    for reg in cfg["regions"]:
        mask = np.ones((h, w), bool) if reg["rect"] is None else np.zeros((h, w), bool)
        if reg["rect"] is not None:
            x0, y0, x1, y1 = reg["rect"]
            mask[y0:y1, x0:x1] = True
        mask &= ~done
        px = img[mask]
        centers = np.asarray(reg["centers"], np.float32)
        d = ((px[:, None, :] - centers[None, :, :]) ** 2).sum(axis=2)
        lab = d.argmin(axis=1)
        pc[mask] = np.asarray(reg["classes"], np.uint8)[lab]
        done |= mask
    return pc


def detect_sand(img, pc):
    lum = img.mean(axis=2)
    m = ndi.uniform_filter(lum, 5)
    m2 = ndi.uniform_filter(lum * lum, 5)
    std = np.sqrt(np.maximum(m2 - m * m, 0))
    r, g, b = img[..., 0], img[..., 1], img[..., 2]
    near_water = ndi.binary_dilation(pc == P_WATER, iterations=16)
    sand = (pc == P_ROCK) & (std < 12) & (lum > 105) & (r > g) & (g > b + 6) & near_water
    sand = ndi.binary_opening(sand, iterations=1)
    out = pc.copy()
    out[sand] = P_SAND
    return out


def split_roofs(pc):
    """Große rote Flächen sind Dächer (Häuser), kleine rote Tupfen Herbstbäume."""
    roof = pc == P_ROOF
    lab, n = ndi.label(roof)
    houses = []
    out = pc.copy()
    for i, sl in enumerate(ndi.find_objects(lab), start=1):
        part = lab[sl] == i
        area = int(part.sum())
        ys, xs = sl
        if area >= 45 and (xs.stop - xs.start) >= 8 and (ys.stop - ys.start) >= 6:
            houses.append((xs.start, ys.start, xs.stop, ys.stop))
        else:
            out[sl][part] = P_DARK
    return out, houses


def tile_fractions(pc, tw, th):
    f = {}
    for c in range(8):
        a = (pc == c).astype(np.float32)
        f[c] = ndi.uniform_filter(a, 4)[1::2, 1::2][:th, :tw]
    return f


def build_tiles(pc, tw, th):
    f = tile_fractions(pc, tw, th)
    t = np.full((th, tw), GRASS, np.uint8)
    t[f[P_ROCK] >= 0.5] = ROCK
    t[f[P_DARK] >= 0.5] = TREE  # Waldfläche (wird unten zu Einzelbäumen oder Fels)
    t[f[P_SAND] >= 0.45] = SAND
    t[f[P_PLAZA] >= 0.45] = STONE
    t[f[P_PATH] >= 0.30] = PATH
    t[f[P_WATER] >= 0.5] = WATER
    return t


def mode_filter(t, keep, iters=2):
    """Einzelne Ausreißer-Kacheln an die Mehrheit der 3 x 3-Nachbarn angleichen (Wege bleiben, sie sind schmal)."""
    for _ in range(iters):
        counts = np.stack([ndi.uniform_filter((t == c).astype(np.float32), 3) for c in range(11)])
        best = counts.argmax(axis=0).astype(np.uint8)
        strong = counts.max(axis=0) >= 6 / 9
        change = strong & ~np.isin(t, keep) & (best != t)
        t = t.copy()
        t[change] = best[change]
    return t


def land_mask(t, seed_t):
    core = np.isin(t, [GRASS, PATH, SAND, STONE])
    lab, _ = ndi.label(core)
    sid = lab[seed_t[1], seed_t[0]]
    if sid == 0:
        raise SystemExit(f"Startpunkt {seed_t} liegt nicht auf begehbarem Land")
    land = lab == sid
    # Waldstücke zählen zum Land, wenn sie überwiegend von Land umgeben sind (Waldinseln im Gras), sonst zum Gebirge
    forest = t == TREE
    flab, _ = ndi.label(forest)
    for i, sl in enumerate(ndi.find_objects(flab), start=1):
        ys, xs = sl
        y0, y1 = max(0, ys.start - 1), min(t.shape[0], ys.stop + 1)
        x0, x1 = max(0, xs.start - 1), min(t.shape[1], xs.stop + 1)
        part = flab[y0:y1, x0:x1] == i
        edge = ndi.binary_dilation(part) & ~part
        total = edge.sum()
        if total and (land[y0:y1, x0:x1] & edge).sum() / total >= 0.55:
            land[y0:y1, x0:x1] |= part
    return land


def place_trees(pc, land, tw, th, rng):
    """Baum-Positionen aus den dunklen Baumtupfen im Wiki-Bild (je Tupfen ein Baum, große Wälder auf einem Raster)."""
    land_px = np.repeat(np.repeat(land, 2, axis=0), 2, axis=1)[: pc.shape[0], : pc.shape[1]]
    dark = (pc == P_DARK) & land_px
    lab, _ = ndi.label(dark)
    pts = []
    for i, sl in enumerate(ndi.find_objects(lab), start=1):
        part = lab[sl] == i
        area = int(part.sum())
        if area < 10:
            continue
        ys, xs = np.nonzero(part)
        ys = ys + sl[0].start
        xs = xs + sl[1].start
        n = max(1, int(round(area / 70)))
        if n == 1:
            pts.append((xs.mean(), ys.mean()))
        else:
            # Raster mit 8 Bildpunkten Abstand über die Fläche, leicht versetzt
            for gy in range(sl[0].start, sl[0].stop, 8):
                for gx in range(sl[1].start + (gy // 8 % 2) * 4, sl[1].stop, 8):
                    if 0 <= gy < pc.shape[0] and 0 <= gx < pc.shape[1] and dark[gy, gx] and lab[gy, gx] == i:
                        pts.append((gx + rng.uniform(-1.5, 1.5), gy + rng.uniform(-1.5, 1.5)))
    trees = np.zeros((th, tw), bool)
    taken = np.zeros((th, tw), bool)
    for x, y in pts:
        tx, ty = int(x // 2), int(y // 2)
        if not (0 <= tx < tw and 0 <= ty < th):
            continue
        if taken[max(0, ty - 1): ty + 2, max(0, tx - 1): tx + 2].any():
            continue
        trees[ty, tx] = True
        taken[ty, tx] = True
    return trees


def stamp_bridge(t, seg, tw, th, width=3):
    """Brücke/Steg als 3 Kacheln breites Band. Wo seitlich daneben Wasser liegt, sind es Planken (BRIDGE), sonst ein Weg
    zum Ufer. Unter den Planken erkennt die Farbklassifizierung oft Weg/Fels statt Wasser, deshalb der Blick zur Seite."""
    x0, y0, x1, y1 = (v / 2 for v in seg)
    dx, dy = x1 - x0, y1 - y0
    ln = max(1e-6, (dx * dx + dy * dy) ** 0.5)
    n = int(ln * 2) + 1
    nx, ny = -dy / ln, dx / ln
    src = t.copy()

    def water_at(px, py):
        ix, iy = int(round(px)), int(round(py))
        return 0 <= ix < tw and 0 <= iy < th and src[iy, ix] == WATER

    for i in range(n + 1):
        cx, cy = x0 + dx * i / n, y0 + dy * i / n
        side = any(water_at(cx + nx * k * sgn, cy + ny * k * sgn) for k in range(3, 8) for sgn in (1, -1))
        for k in np.linspace(-(width - 1) / 2, (width - 1) / 2, width * 2):
            tx, ty = int(round(cx + nx * k)), int(round(cy + ny * k))
            if not (0 <= tx < tw and 0 <= ty < th):
                continue
            if side or src[ty, tx] == WATER:
                t[ty, tx] = BRIDGE
            elif src[ty, tx] in (ROCK, TREE):
                t[ty, tx] = PATH


def house_props(houses, tw, th):
    props = []
    if not houses:
        return props
    areas = sorted(((x1 - x0) * (y1 - y0) for x0, y0, x1, y1 in houses), reverse=True)
    big = areas[min(2, len(areas) - 1)]
    for i, (x0, y0, x1, y1) in enumerate(sorted(houses, key=lambda b: (b[1], b[0]))):
        w = max(4, int(round((x1 - x0) / 2)))
        h = max(3, int(round((y1 - y0) / 2)))
        tx, ty = int(x0 // 2), int(y0 // 2)
        a = (x1 - x0) * (y1 - y0)
        kind = "hall" if a == areas[0] else "main" if a >= big else "small"
        props.append({"type": "house", "kind": kind, "tx": tx, "ty": ty, "w": w, "h": h, "v": i})
    return props


def write_collision(t, origin, path):
    ox, oy = origin
    h, w = t.shape
    grid = np.ones((GRID, GRID), bool)  # alles außerhalb der Karte blockiert
    solid = np.isin(t, list(SOLID))
    # Zeile 0 der Karte = Norden = größtes Welt-y (Unity: y nach oben)
    for ty in range(h):
        cy = oy + (h - 1 - ty)
        grid[cy, ox: ox + w] = solid[ty]
    bits = np.packbits(grid.reshape(-1), bitorder="little")
    with open(path, "wb") as f:
        f.write(b"LCM1")
        f.write(struct.pack("<ii", GRID, GRID))
        f.write(bits.tobytes())


def write_terrain(t, origin, path):
    h, w = t.shape
    with open(path, "wb") as f:
        f.write(b"LTR1")
        f.write(struct.pack("<HHhh", w, h, origin[0], origin[1]))
        f.write(t.astype(np.uint8).tobytes())


def preview(t, props, path):
    cols = np.array([[70, 120, 50], [90, 150, 60], [170, 130, 80], [40, 80, 140], [220, 200, 140], [20, 50, 20],
                     [120, 120, 120], [220, 220, 210], [200, 40, 40], [150, 100, 60], [200, 0, 200]], np.uint8)
    im = cols[t]
    for p in props:
        if p["type"] == "house":
            im[p["ty"]: p["ty"] + p["h"], p["tx"]: p["tx"] + p["w"]] = [200, 40, 40]
    Image.fromarray(im).resize((t.shape[1] * 2, t.shape[0] * 2), Image.NEAREST).save(path)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("map", choices=sorted(MAPS))
    ap.add_argument("image")
    ap.add_argument("--preview")
    a = ap.parse_args()
    cfg = MAPS[a.map]
    img = np.asarray(Image.open(a.image).convert("RGB")).astype(np.float32)
    if (img.shape[1], img.shape[0]) != cfg["wiki"]:
        raise SystemExit(f"Bild hat {img.shape[1]}x{img.shape[0]}, erwartet {cfg['wiki']}")
    tw, th = cfg["wiki"][0] // 2, cfg["wiki"][1] // 2
    origin = ((GRID - tw) // 2, (GRID - th) // 2)
    rng = np.random.default_rng(1)

    pc = classify_pixels(img, cfg)
    if cfg["sand"]:
        pc = detect_sand(img, pc)
    pc, houses = split_roofs(pc)
    houses += cfg.get("houses", [])
    t = build_tiles(pc, tw, th)
    t = mode_filter(t, keep=[PATH, WATER])
    if cfg["plaza"]:
        cx, cy, r = cfg["plaza"]
        yy, xx = np.mgrid[0:th, 0:tw]
        t[(xx * 2 + 1 - cx) ** 2 + (yy * 2 + 1 - cy) ** 2 <= r * r] = STONE
    for seg in cfg["bridges"] + cfg["piers"]:
        stamp_bridge(t, seg, tw, th)
    seed = (cfg["seed"][0] // 2, cfg["seed"][1] // 2)
    t2 = t.copy()
    t2[t2 == BRIDGE] = PATH
    land = land_mask(t2, seed)
    land |= (t == BRIDGE) & ndi.binary_dilation(land, iterations=4)

    out = t.copy()
    # Alles Nicht-Land außer Wasser wird Gebirge (auch abgeschnittene Grasflecken auf den Bergen)
    out[~land & (out != WATER) & (out != BRIDGE)] = ROCK
    # Waldflächen im Land: Boden ist Gras, darauf Einzelbäume
    out[land & (out == TREE)] = GRASS
    trees = place_trees(pc, land, tw, th, rng)
    out[trees & land & np.isin(out, [GRASS, SAND])] = TREE

    props = house_props(houses, tw, th)
    for p in props:
        out[p["ty"]: p["ty"] + p["h"], p["tx"]: p["tx"] + p["w"]] = WALL

    dst = ROOT / "data" / "maps" / cfg["out"]
    dst.mkdir(parents=True, exist_ok=True)
    write_terrain(out, origin, dst / "terrain.bin")
    write_collision(out, origin, dst / "collision.bin")
    old = {}
    pj = dst / "props.json"
    if pj.exists():
        old = json.loads(pj.read_text())
    # Laternen, Brunnen, Denkmäler und Stege pflegt man von Hand in props.json; neu berechnet werden nur die Häuser
    keep = [p for p in old.get("props", []) if p.get("type") != "house"]
    pj.write_text(json.dumps({"_hinweis": "Häuser aus tools/derive-wiki-map.py, übrige Einträge von Hand. Kachelkoordinaten, Zeile 0 = Norden.",
                              "props": props + keep}, ensure_ascii=False, indent=1) + "\n")
    if a.preview:
        preview(out, props, a.preview)
    counts = {name: int((out == v).sum()) for name, v in
              [("gras", GRASS), ("weg", PATH), ("wasser", WATER), ("sand", SAND), ("baum", TREE), ("fels", ROCK),
               ("pflaster", STONE), ("haus", WALL), ("bruecke", BRIDGE)]}
    print(f"{a.map}: {tw}x{th} Kacheln, origin {origin}, {len(props)} Häuser, {counts}")


if __name__ == "__main__":
    main()
