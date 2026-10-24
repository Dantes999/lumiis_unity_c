#!/usr/bin/env python3
"""Rendert eine simple Vorschau der Lumiis-Referenzkarte greenfield nach Resources/Maps.

Nur eine Farbkarte der Tile-IDs (kein Tile-Art-Import); dient dem Asset-Testlabor als
Kartenvorschau. Die echte Kollision/Server-Integration ist in docs/map-import-plan.md
beschrieben und noch nicht verdrahtet.
"""

from pathlib import Path
from PIL import Image

ROOT = Path(__file__).resolve().parents[1]
SOURCE = Path("/home/nutzer/Desktop/lumiis/packages/shared/maps/greenfield.json")
TARGET = ROOT / "client" / "Assets" / "Resources" / "Maps" / "greenfield.png"

PALETTE = {
    0: (74, 132, 62),    # grass
    1: (112, 160, 78),   # flowers
    2: (170, 145, 92),   # path
    3: (48, 92, 180),    # water
    4: (205, 185, 120),  # sand
    5: (32, 92, 42),     # tree
    6: (103, 103, 103),  # rock
    7: (145, 145, 145),  # stone
    8: (65, 65, 65),     # wall
    9: (160, 120, 74),   # bridge
    10: (225, 50, 215),  # portal
    11: (44, 112, 54),   # cactus
    12: (92, 62, 34),    # closed gate
    13: (175, 145, 92),  # open gate
}


def main() -> None:
    import json

    data = json.loads(SOURCE.read_text(encoding="utf-8"))
    width = data["width"]
    height = data["height"]
    tiles = data["tiles"]
    if len(tiles) != width * height:
        raise SystemExit("greenfield.json: Tile-Anzahl passt nicht zu width*height")

    image = Image.new("RGB", (width, height))
    image.putdata([PALETTE.get(tile, (255, 0, 255)) for tile in tiles])
    TARGET.parent.mkdir(parents=True, exist_ok=True)
    image.save(TARGET)
    print(f"geschrieben: {TARGET.relative_to(ROOT)} ({width}x{height})")


if __name__ == "__main__":
    main()
