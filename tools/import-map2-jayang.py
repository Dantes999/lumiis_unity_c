"""Baut Map2 (Jayang) aus den exportierten lumiis-2d-Daten (tools/map2-jayang-source.json).

Aufruf (Repo-Root): python3 tools/import-map2-jayang.py

Schneidet aus der 512x512-Originalkarte den Ausschnitt um das Dorf und das erste Jagdgebiet
("Lager des Weissen Eids", Level 18-25 - passt zum Alpha-Levelbereich 1-25) und schreibt:
- client/Assets/Resources/Maps/map2.png (16 px/Tile, 4096x4096)
- data/maps/map2/collision.bin (1000x1000 Zellen, Ausschnitt bei ORIGIN, y gespiegelt wie map1)
- data/maps/map2/map.json, portals.json, npcs.json, spawns.json

Die Tile-Grafik kommt aus tools/import-map1.py (gleiche Palette), damit beide Karten gleich aussehen.
Mob-Ids der Zone stehen in MOB_IDS (Werte in data/mobs.json, Originalwerte in tools/map2-jayang-mobs.json).
"""
import importlib.util
import json
import struct
from pathlib import Path

from PIL import Image

ROOT = Path(__file__).resolve().parents[1]
DATA = ROOT / "data" / "maps" / "map2"
RES = ROOT / "client" / "Assets" / "Resources" / "Maps"

# Ausschnitt aus der Originalkarte (Tile-Koordinaten) und Lage in der 1000x1000-Zellenkarte.
CROP_X0, CROP_Y0, CROP = 136, 200, 256
ORIGIN = (1000 - CROP) // 2  # 372 - Ausschnitt mittig
MAP_SIZE = 1000
TILE_PX = 16
SPAWN_ZONE = 1  # Lager des Weissen Eids

MOB_IDS = {
    "woath_soldier": 10,
    "woath_archer": 11,
    "woath_commander": 12,
    "woath_general": 13,
    "woath_soldier_c": 14,
    "woath_archer_c": 15,
    "metin_blackness": 18,
}
# Npc-Typen aus lumiis-2d -> data/npcs.json (nur Dienste, die es hier schon gibt).
NPC_IDS = {"general": 1, "smith": 2}

# Portal nach Map1: Map1 ist nur ein Ausschnitt von Yongan, das Original-Nordportal liegt ausserhalb.
# Es sitzt deshalb am oberen Ende des Weges im Map1-Ausschnitt (siehe data/maps/map1/portals.json).
MAP1_ARRIVAL = (44250, 51950)


def load_map1_module():
    spec = importlib.util.spec_from_file_location("import_map1", ROOT / "tools" / "import-map1.py")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def to_local(tx, ty):
    return tx - CROP_X0, ty - CROP_Y0


def in_crop(tx, ty):
    lx, ly = to_local(tx, ty)
    return 0 <= lx < CROP and 0 <= ly < CROP


def cell_of(lx, ly):
    """Lokale Tile-Koordinate (y nach unten) -> Kollisionszelle (y nach oben)."""
    return ORIGIN + lx, ORIGIN + (CROP - 1) - ly


def wire_center(tx, ty):
    cx, cy = cell_of(*to_local(tx, ty))
    return cx * 100 + 50, cy * 100 + 50


def main():
    m1 = load_map1_module()
    src = json.loads((ROOT / "tools" / "map2-jayang-source.json").read_text())
    w = src["w"]
    full = src["tiles"]

    tiles = [full[(CROP_Y0 + y) * w + (CROP_X0 + x)] for y in range(CROP) for x in range(CROP)]
    # Geschlossener Baumrand, damit niemand aus dem Ausschnitt in den leeren Rest der Zellenkarte laeuft.
    for i in range(CROP):
        for b in (0, 1, CROP - 2, CROP - 1):
            tiles[b * CROP + i] = m1.TREE
            tiles[i * CROP + b] = m1.TREE

    def walkable(lx, ly):
        return 0 <= lx < CROP and 0 <= ly < CROP and tiles[ly * CROP + lx] not in m1.SOLID

    DATA.mkdir(parents=True, exist_ok=True)

    # Bild: Tiles der map1-Palette, auf 16 px verkleinert.
    palette = {t: m1.tile_image(t).resize((TILE_PX, TILE_PX), Image.BOX) for t in range(14)}
    img = Image.new("RGBA", (CROP * TILE_PX, CROP * TILE_PX), (0, 0, 0, 0))
    for y in range(CROP):
        for x in range(CROP):
            img.paste(palette[tiles[y * CROP + x]], (x * TILE_PX, y * TILE_PX))
    img.save(RES / "map2.png", optimize=True)

    packed = bytearray((MAP_SIZE * MAP_SIZE + 7) // 8)
    blocked = 0
    for cell_y in range(MAP_SIZE):
        for cell_x in range(MAP_SIZE):
            lx = cell_x - ORIGIN
            ly = ORIGIN + (CROP - 1) - cell_y
            if not walkable(lx, ly):
                idx = cell_y * MAP_SIZE + cell_x
                packed[idx // 8] |= 1 << (idx % 8)
                blocked += 1
    with open(DATA / "collision.bin", "wb") as f:
        f.write(b"LCM1")
        f.write(struct.pack("<ii", MAP_SIZE, MAP_SIZE))
        f.write(packed)

    def nearest_walkable(tx, ty):
        for r in range(0, 12):
            for dy in range(-r, r + 1):
                for dx in range(-r, r + 1):
                    if max(abs(dx), abs(dy)) == r and walkable(*to_local(tx + dx, ty + dy)):
                        return tx + dx, ty + dy
        raise SystemExit(f"Kein begehbares Tile nahe {tx}/{ty}")

    village = src["village"]
    # Die Dorfmitte selbst ist ein Denkmal (blockiert) - Start auf dem naechsten freien Platz-Tile.
    start_x, start_y = wire_center(*nearest_walkable(village["cx"], village["cy"] + 3))
    (DATA / "map.json").write_text(json.dumps({
        "name": src["name"],
        "startX": start_x,
        "startY": start_y,
        "visualOriginX": ORIGIN,
        "visualOriginY": ORIGIN,
        "visualWidth": CROP,
        "visualHeight": CROP,
        "visualPixelsPerUnit": TILE_PX,
    }, indent=2) + "\n")

    portals = []
    for p in src["portals"]:
        if p["to"] != "yongan":
            continue  # Hasun Dong (Dungeon) gibt es noch nicht.
        lx0, ly0 = to_local(p["x0"], p["y0"])
        lx1, ly1 = to_local(p["x1"], p["y1"])
        cx0, cy_top = cell_of(lx0, ly0)
        cx1, cy_bottom = cell_of(lx1, ly1)
        ax, ay = wire_center(p["ax"], p["ay"])
        portals.append({
            "id": 1,
            "x1": cx0 * 100, "y1": cy_bottom * 100,
            "x2": cx1 * 100 + 99, "y2": cy_top * 100 + 99,
            "targetMapId": "map1",
            "targetX": MAP1_ARRIVAL[0], "targetY": MAP1_ARRIVAL[1],
        })
        arrival_from_map1 = (ax, ay)
    (DATA / "portals.json").write_text(json.dumps({"portals": portals}, indent=2) + "\n")

    placements = []
    for npc in src["npcs"]:
        if npc["type"] not in NPC_IDS or not in_crop(npc["tx"], npc["ty"]):
            continue
        x, y = wire_center(npc["tx"], npc["ty"])
        placements.append({"id": len(placements) + 1, "npcId": NPC_IDS[npc["type"]], "positionX": x, "positionY": y})
    (DATA / "npcs.json").write_text(json.dumps({"placements": placements}, indent=2) + "\n")

    # Spawns: jede Originalgruppe der Zone 1:1, Mitglieder leicht versetzt um den Gruppenpunkt.
    offsets = [(0, 0), (1, 0), (0, 1), (-1, 0), (0, -1), (1, 1), (-1, -1), (1, -1), (-1, 1)]
    village_cells = (village["cx"], village["cy"], village["r"])
    spawns = []
    skipped = 0

    def add_spawn(mob_key, tx, ty, respawn_seconds):
        nonlocal skipped
        for dx, dy in offsets:
            lx, ly = to_local(tx + dx, ty + dy)
            if walkable(lx, ly):
                x, y = wire_center(tx + dx, ty + dy)
                spawns.append({"id": len(spawns) + 1, "mobId": MOB_IDS[mob_key], "positionX": x, "positionY": y,
                               "respawnSeconds": respawn_seconds})
                return
        skipped += 1

    for group in src["spawns"]["groups"]:
        if group["zone"] != SPAWN_ZONE or not in_crop(group["tx"], group["ty"]):
            continue
        dist2 = (group["tx"] - village_cells[0]) ** 2 + (group["ty"] - village_cells[1]) ** 2
        if dist2 <= village_cells[2] ** 2:
            continue  # Sicherheitszone um das Dorf
        respawn = max(10, group["respawnMs"] // 1000)
        for i, member in enumerate(group["members"]):
            ox, oy = offsets[i % len(offsets)]
            add_spawn(member["key"], group["tx"] + ox, group["ty"] + oy, respawn)

    # Lumiis-Steine: Original wuerfelt 3 aus 10 Punkten; hier feste Punkte (die ersten drei im Ausschnitt).
    for metin in src["spawns"]["metins"]:
        if metin["key"] not in MOB_IDS:
            continue
        points = [pt for pt in metin["points"] if in_crop(*pt)][: metin["count"]]
        respawn = sum(metin["respawnMs"]) // 2 // 1000
        for tx, ty in points:
            add_spawn(metin["key"], tx, ty, respawn)

    (DATA / "spawns.json").write_text(json.dumps({"spawns": spawns}, indent=1) + "\n")

    print(f"map2.png: {img.width}x{img.height}, collision {blocked}/{MAP_SIZE * MAP_SIZE} blocked")
    print(f"start {start_x}/{start_y}, portals {len(portals)}, npcs {len(placements)}, spawns {len(spawns)} (skipped {skipped})")
    print(f"Ankunft auf map2 vom map1-Portal: {arrival_from_map1}")


if __name__ == "__main__":
    main()
