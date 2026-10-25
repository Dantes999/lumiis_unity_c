#!/usr/bin/env python3
"""Erzeugt die zweite Testkarte `greenfield` deterministisch.

Kein Import der Lumiis-Referenzkarte (die liegt nur als gerenderte Vorschau vor, siehe
docs/map-import-plan.md): eine eigene, einfache 1000x1000-Karte mit Rand, See und ein paar
Felsgruppen. Startposition, Portalzellen und alle Spawn-Punkte bleiben garantiert begehbar.

Schreibt:
  data/maps/greenfield/collision-source.png (rot = blockiert, siehe tools/bake-collision.py)
  data/maps/greenfield/collision.bin         (LCM1, 1 Bit pro Zelle, LSB-first)
"""
from pathlib import Path
import struct

from PIL import Image, ImageDraw

SIZE = 1000
BLOCKED = (220, 20, 20)

# Start, Portale und Spawn-Punkte, die garantiert frei bleiben muessen.
KEEP_FREE = [
    (500, 500), (500, 600), (500, 300), (530, 500),
    (200, 200), (700, 250), (250, 700), (750, 700),
    (600, 500), (400, 600), (600, 700),
]


def main() -> None:
    image = Image.new("RGB", (SIZE, SIZE), (255, 255, 255))
    draw = ImageDraw.Draw(image)

    # Rand (3 Zellen).
    draw.rectangle([0, 0, SIZE - 1, 2], fill=BLOCKED)
    draw.rectangle([0, SIZE - 3, SIZE - 1, SIZE - 1], fill=BLOCKED)
    draw.rectangle([0, 0, 2, SIZE - 1], fill=BLOCKED)
    draw.rectangle([SIZE - 3, 0, SIZE - 1, SIZE - 1], fill=BLOCKED)

    # See.
    draw.ellipse([300, 300, 430, 430], fill=BLOCKED)

    # Felsgruppen (Kreise).
    for cx, cy, r in [(180, 520, 26), (830, 300, 22), (700, 780, 30),
                      (260, 180, 18), (820, 620, 24), (380, 820, 20),
                      (120, 760, 16), (900, 880, 28)]:
        draw.ellipse([cx - r, cy - r, cx + r, cy + r], fill=BLOCKED)

    # Mauer-Reste.
    draw.rectangle([560, 640, 700, 646], fill=BLOCKED)
    draw.rectangle([560, 640, 566, 740], fill=BLOCKED)

    # Schutzbereiche wieder freimalen (Start/Portale/Spawns).
    for cx, cy in KEEP_FREE:
        draw.rectangle([cx - 3, cy - 3, cx + 3, cy + 3], fill=(255, 255, 255))

    out_dir = Path(__file__).resolve().parents[1] / "data" / "maps" / "greenfield"
    out_dir.mkdir(parents=True, exist_ok=True)
    image.save(out_dir / "collision-source.png")

    pixels = image.load()
    packed = bytearray((SIZE * SIZE + 7) // 8)
    blocked_count = 0
    for y in range(SIZE):
        for x in range(SIZE):
            r, g, b = pixels[x, y]
            if r > 200 and g < 100 and b < 100:
                index = y * SIZE + x
                packed[index // 8] |= 1 << (index % 8)
                blocked_count += 1

    with open(out_dir / "collision.bin", "wb") as handle:
        handle.write(b"LCM1")
        handle.write(struct.pack("<ii", SIZE, SIZE))
        handle.write(packed)

    print(f"{out_dir / 'collision.bin'}: {blocked_count} blockierte Zellen "
          f"({blocked_count * 100.0 / (SIZE * SIZE):.2f}%)")


if __name__ == "__main__":
    main()
