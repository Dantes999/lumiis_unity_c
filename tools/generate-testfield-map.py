#!/usr/bin/env python3
"""Erzeugt die Testkarte "Testwiese" (data/maps/testfield): reine Graslandschaft mit Baumrand und vielen
3er-Gruppen Wilder Hunde (Mob 20) - zum Ausprobieren von Kampf und Gruppen-Aggro ohne Dorf, NPCs oder Portale.

Aufruf (Repo-Root, nur Python-Standardbibliothek):
  python3 tools/generate-testfield-map.py

Schreibt (deterministisch, fester Seed):
  data/maps/testfield/terrain.bin   "LTR1" wie tools/derive-wiki-map.py (Zeile 0 = Norden)
  data/maps/testfield/collision.bin "LCM1" 1000 x 1000, außerhalb der Karte und Bäume blockiert
  data/maps/testfield/map.json      Name, Start in der Mitte, "terrain": true
  data/maps/testfield/spawns.json   GROUPS Gruppen zu je 3 Wildhunden mit gemeinsamer groupId
"""
import json
import math
import random
import struct
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / "data" / "maps" / "testfield"

GRID = 1000          # Kollisionsraster des Servers
SIZE = 160           # Karte SIZE x SIZE Kacheln, 1 Kachel = 1 Zelle = 100 Wire
ORIGIN = ((GRID - SIZE) // 2, (GRID - SIZE) // 2)
BORDER = 3           # Baumrand in Kacheln
GRASS, TREE = 0, 5
SOLID = {3, 5, 6, 8}

MOB_WILD_DOG = 20
GROUPS = 60
GROUP_SIZE = 3
MIN_GROUP_DIST = 13  # Kacheln zwischen zwei Gruppenmittelpunkten
FREE_RING = 12       # Kacheln um den Start ohne Monster (Test verlangt > 10)
RESPAWN_SECONDS = 20
SEED = 4711
# Mitglieder dicht beim Gruppenmittelpunkt (1-2 Felder Abstand)
MEMBER_OFFSETS = [(0, 0), (2, 1), (-1, 2)]


def build_tiles():
    tiles = [[GRASS] * SIZE for _ in range(SIZE)]
    for ty in range(SIZE):
        for tx in range(SIZE):
            if tx < BORDER or ty < BORDER or tx >= SIZE - BORDER or ty >= SIZE - BORDER:
                tiles[ty][tx] = TREE
    return tiles


def wire(tx, ty):
    """Kachel (Zeile 0 = Norden) -> Welt-Wire-Koordinate der Zellenmitte (y nach oben)."""
    cx = ORIGIN[0] + tx
    cy = ORIGIN[1] + (SIZE - 1 - ty)
    return cx * 100 + 50, cy * 100 + 50


def place_groups(tiles, start):
    rng = random.Random(SEED)
    lo, hi = BORDER + 3, SIZE - BORDER - 4
    centers = []
    tries = 0
    while len(centers) < GROUPS:
        tries += 1
        if tries > 200000:
            raise SystemExit(f"Nur {len(centers)} von {GROUPS} Gruppen passen - MIN_GROUP_DIST verkleinern.")
        tx, ty = rng.randint(lo, hi), rng.randint(lo, hi)
        if math.hypot(tx - start[0], ty - start[1]) < FREE_RING:
            continue
        if any(math.hypot(tx - x, ty - y) < MIN_GROUP_DIST for x, y in centers):
            continue
        if any(tiles[ty + dy][tx + dx] in SOLID for dx, dy in MEMBER_OFFSETS):
            continue
        centers.append((tx, ty))
    return centers


def write_terrain(tiles, path):
    with open(path, "wb") as f:
        f.write(b"LTR1")
        f.write(struct.pack("<HHhh", SIZE, SIZE, ORIGIN[0], ORIGIN[1]))
        f.write(bytes(t for row in tiles for t in row))


def write_collision(tiles, path):
    blocked = bytearray((GRID * GRID + 7) // 8)
    for cy in range(GRID):
        for cx in range(GRID):
            tx, ty = cx - ORIGIN[0], (SIZE - 1) - (cy - ORIGIN[1])
            inside = 0 <= tx < SIZE and 0 <= ty < SIZE
            if not inside or tiles[ty][tx] in SOLID:
                i = cy * GRID + cx
                blocked[i // 8] |= 1 << (i % 8)
    with open(path, "wb") as f:
        f.write(b"LCM1")
        f.write(struct.pack("<ii", GRID, GRID))
        f.write(blocked)


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    tiles = build_tiles()
    start = (SIZE // 2, SIZE // 2)
    centers = place_groups(tiles, start)

    spawns = []
    for group_id, (tx, ty) in enumerate(centers, start=1):
        for dx, dy in MEMBER_OFFSETS:
            x, y = wire(tx + dx, ty + dy)
            spawns.append({"id": len(spawns) + 1, "mobId": MOB_WILD_DOG, "positionX": x, "positionY": y,
                           "respawnSeconds": RESPAWN_SECONDS, "groupId": group_id})

    start_x, start_y = wire(*start)
    write_terrain(tiles, OUT / "terrain.bin")
    write_collision(tiles, OUT / "collision.bin")
    (OUT / "map.json").write_text(json.dumps(
        {"name": "Testwiese", "startX": start_x, "startY": start_y, "terrain": True}, indent=2) + "\n")
    (OUT / "spawns.json").write_text(json.dumps({"spawns": spawns}, indent=1) + "\n")
    print(f"testfield: {SIZE}x{SIZE} Kacheln, origin {ORIGIN}, Start {start_x}/{start_y}, "
          f"{len(centers)} Gruppen, {len(spawns)} Wildhunde")


if __name__ == "__main__":
    main()
