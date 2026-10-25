import json
from pathlib import Path
from PIL import Image, ImageDraw
import struct

ROOT = Path(__file__).resolve().parents[1]
DATA = ROOT / "data" / "maps" / "map1"
RES = ROOT / "client" / "Assets" / "Resources" / "Maps"
TILE = 32
MAP_SIZE = 1000  # collision cells
ORIGIN = 400     # world cell of local tile (0,0), map is 128x128 tiles = 128x128 cells
CROP_W = 128
CROP_H = 128

# Tile ids (lumiis/packages/shared/src/map.ts)
GRASS, FLOWERS, PATH, WATER, SAND, TREE, ROCK, STONE, WALL, BRIDGE, PORTAL, CACTUS, GATE, GATE_OPEN = range(14)
SOLID = {WATER, TREE, ROCK, WALL, CACTUS, GATE}

C = {
    "grass": (47, 74, 51, 255),
    "grass_light": (53, 85, 58, 255),
    "path": (107, 90, 62, 255),
    "path_dark": (91, 74, 49, 255),
    "water": (28, 58, 92, 255),
    "water_light": (36, 80, 122, 255),
    "sand": (141, 124, 82, 255),
    "sand_dark": (124, 107, 69, 255),
    "trunk": (74, 53, 36, 255),
    "canopy": (31, 58, 38, 255),
    "canopy_light": (39, 73, 47, 255),
    "rock": (90, 95, 99, 255),
    "rock_light": (109, 115, 120, 255),
    "stone": (74, 74, 79, 255),
    "stone_line": (59, 59, 64, 255),
    "wall": (85, 80, 74, 255),
    "wall_dark": (70, 66, 61, 255),
    "bridge": (122, 90, 58, 255),
    "bridge_dark": (100, 72, 44, 255),
    "portal": (42, 35, 64, 255),
    "portal_glow": (106, 73, 176, 255),
    "portal_core": (169, 140, 240, 255),
    "cactus": (47, 107, 58, 255),
    "gate": (58, 58, 64, 255),
    "gate_bar": (34, 34, 42, 255),
    "gate_open": (31, 31, 36, 255),
}


def tile_image(t):
    im = Image.new("RGBA", (TILE, TILE), (0, 0, 0, 0))
    d = ImageDraw.Draw(im)

    def base(col):
        d.rectangle([0, 0, TILE - 1, TILE - 1], fill=col)

    def speckle(col, count, seed):
        state = seed
        for _ in range(count):
            state = (state * 1103515245 + 12345) & 0xFFFFFFFF
            x = int((state / 4294967296) * (TILE - 2))
            state = (state * 1103515245 + 12345) & 0xFFFFFFFF
            y = int((state / 4294967296) * (TILE - 2))
            d.rectangle([x, y, x + 1, y + 1], fill=col)

    if t == GRASS:
        base(C["grass"]); speckle(C["grass_light"], 14, 11)
    elif t == FLOWERS:
        base(C["grass"]); speckle(C["grass_light"], 10, 12)
        d.rectangle([8, 10, 9, 11], fill=(217, 200, 107, 255))
        d.rectangle([20, 19, 21, 20], fill=(217, 200, 107, 255))
        d.rectangle([16, 7, 17, 8], fill=(208, 122, 138, 255))
        d.rectangle([25, 13, 26, 14], fill=(208, 122, 138, 255))
    elif t == PATH:
        base(C["path"]); speckle(C["path_dark"], 14, 13)
    elif t == WATER:
        base(C["water"])
        d.rectangle([0, 8, 13, 9], fill=C["water_light"])
        d.rectangle([18, 20, 31, 21], fill=C["water_light"])
        d.rectangle([6, 26, 15, 27], fill=C["water_light"])
    elif t == SAND:
        base(C["sand"]); speckle(C["sand_dark"], 12, 14)
    elif t == TREE:
        base(C["grass"])
        d.rectangle([14, 19, 17, 29], fill=C["trunk"])
        d.ellipse([5, 3, 26, 24], fill=C["canopy"])
        d.ellipse([7, 2, 18, 13], fill=C["canopy_light"])
    elif t == ROCK:
        base(C["grass"])
        d.ellipse([7, 11, 24, 29], fill=C["rock"])
        d.ellipse([10, 8, 20, 18], fill=C["rock_light"])
    elif t == STONE:
        base(C["stone"])
        for a, b in [((0, 16), (31, 16)), ((0, 0), (31, 0)), ((16, 0), (16, 15)),
                     ((8, 16), (8, 31)), ((24, 16), (24, 31))]:
            d.line([a, b], fill=C["stone_line"], width=1)
    elif t == WALL:
        base(C["wall"])
        d.rectangle([0, 15, 31, 16], fill=C["wall_dark"])
        d.rectangle([15, 0, 16, 14], fill=C["wall_dark"])
        d.rectangle([7, 17, 8, 31], fill=C["wall_dark"])
        d.rectangle([23, 17, 24, 31], fill=C["wall_dark"])
    elif t == BRIDGE:
        base(C["bridge"])
        for y in range(2, TILE, 8):
            d.rectangle([0, y, 31, y + 1], fill=C["bridge_dark"])
    elif t == PORTAL:
        base(C["portal"])
        d.ellipse([6, 6, 25, 25], fill=C["portal_glow"])
        d.ellipse([12, 12, 19, 19], fill=C["portal_core"])
    elif t == CACTUS:
        base(C["grass"])
        d.rectangle([14, 7, 17, 27], fill=C["cactus"])
        d.rectangle([9, 13, 13, 15], fill=C["cactus"])
        d.rectangle([18, 18, 22, 20], fill=C["cactus"])
    elif t == GATE:
        base(C["gate"])
        for x in range(4, TILE, 8):
            d.rectangle([x, 0, x + 1, 31], fill=C["gate_bar"])
    elif t == GATE_OPEN:
        base(C["gate_open"])
    else:
        base((255, 0, 255, 255))
    return im


def house(d, p):
    x, y, w, h = p["tx"] * TILE, p["ty"] * TILE, p["w"] * TILE, p["h"] * TILE
    roof_h = max(14, h // 3)
    wall = (112, 84, 58, 255)
    wall_dark = (78, 57, 39, 255)
    roof = (70, 42, 34, 255)
    roof_hi = (104, 62, 48, 255)
    trim = (45, 33, 26, 255)
    d.rectangle([x + 2, y + roof_h, x + w - 2, y + h - 2], fill=wall)
    d.rectangle([x + 2, y + roof_h, x + w - 2, y + roof_h + 4], fill=wall_dark)
    d.polygon([(x, y + roof_h), (x + w // 2, y), (x + w, y + roof_h)], fill=roof)
    d.line([(x, y + roof_h), (x + w // 2, y)], fill=roof_hi, width=3)
    d.line([(x + w // 2, y), (x + w, y + roof_h)], fill=roof_hi, width=3)
    d.rectangle([x + 2, y + h - 2, x + w - 2, y + h - 1], fill=trim)
    door_w = max(10, w // 7)
    d.rectangle([x + w // 2 - door_w // 2, y + h - 2 - door_w * 2, x + w // 2 + door_w // 2, y + h - 3], fill=trim)
    d.rectangle([x + w // 5, y + roof_h + 8, x + w // 5 + 6, y + roof_h + 14], fill=(194, 160, 92, 255))
    d.rectangle([x + w - w // 5 - 6, y + roof_h + 8, x + w - w // 5, y + roof_h + 14], fill=(194, 160, 92, 255))


def lantern(d, p):
    x, y = p["tx"] * TILE + TILE // 2, p["ty"] * TILE + TILE // 2
    d.line([(x, y - 18), (x, y + 12)], fill=(46, 36, 28, 255), width=3)
    d.ellipse([x - 5, y - 23, x + 5, y - 13], fill=(246, 204, 96, 255))
    d.ellipse([x - 2, y - 21, x + 2, y - 15], fill=(255, 246, 190, 255))


def main():
    crop = json.loads((ROOT / "tools" / "map1-yongan-crop.json").read_text())
    w, h = crop["w"], crop["h"]
    assert len(crop["tiles"]) == w * h

    tiles = crop["tiles"]
    # A closed tree border keeps the player inside the imported field crop.
    for x in range(w):
        for y in (0, 1, h - 2, h - 1):
            tiles[y * w + x] = TREE
    for y in range(h):
        for x in (0, 1, w - 2, w - 1):
            tiles[y * w + x] = TREE

    DATA.mkdir(parents=True, exist_ok=True)
    RES.mkdir(parents=True, exist_ok=True)

    # Render the map at 32 px per tile -> 4096 x 4096 for 128 x 128 tiles.
    img = Image.new("RGBA", (w * TILE, h * TILE), (0, 0, 0, 0))
    palette = {t: tile_image(t) for t in range(14)}
    for y in range(h):
        for x in range(w):
            img.paste(palette[tiles[y * w + x]], (x * TILE, y * TILE))

    d = ImageDraw.Draw(img)
    for p in crop["props"]:
        if p.get("type") == "house":
            house(d, p)
        elif p.get("type") == "lantern":
            lantern(d, p)
        elif p.get("type") == "well":
            x, y = p["tx"] * TILE, p["ty"] * TILE
            d.ellipse([x + 4, y + 8, x + 60, y + 56], fill=(62, 60, 58, 255))
            d.ellipse([x + 12, y + 15, x + 52, y + 47], fill=(101, 145, 157, 255))
            d.rectangle([x + 26, y + 26, x + 38, y + 40], fill=(52, 44, 38, 255))

    img.save(RES / "map1.png")

    # Collision: 1000 x 1000 cells, crop placed at ORIGIN and y-flipped.
    packed = bytearray((MAP_SIZE * MAP_SIZE + 7) // 8)
    blocked = 0
    for cell_y in range(MAP_SIZE):
        for cell_x in range(MAP_SIZE):
            blocked_cell = True
            lx = cell_x - ORIGIN
            ly_top = ORIGIN + (h - 1) - cell_y
            if 0 <= lx < w and 0 <= ly_top < h:
                blocked_cell = tiles[ly_top * w + lx] in SOLID
            if blocked_cell:
                idx = cell_y * MAP_SIZE + cell_x
                packed[idx // 8] |= 1 << (idx % 8)
                blocked += 1
    with open(DATA / "collision.bin", "wb") as f:
        f.write(b"LCM1")
        f.write(struct.pack("<ii", MAP_SIZE, MAP_SIZE))
        f.write(packed)

    # Keep the source crop for later re-generation/inspection.
    (DATA / "tiles.json").write_text(json.dumps(crop, ensure_ascii=False, indent=1) + "\n")

    print(f"map1.png: {img.width}x{img.height}, collision {blocked}/{MAP_SIZE * MAP_SIZE} blocked")


if __name__ == "__main__":
    main()
