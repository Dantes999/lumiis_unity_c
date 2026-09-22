#!/usr/bin/env python3
"""Backt eine Kollisions-Bitmaske aus einem rot bemalten Kartenquellbild (siehe CLAUDE.md,
Abschnitt "Kartenkollision"). Rote Pixel gelten als blockiert, alles andere als begehbar.

Schwellwert (R > RED_THRESHOLD, G < GB_THRESHOLD, B < GB_THRESHOLD) statt exaktem Rot-Vergleich:
tolerant genug fuer leichtes Antialiasing am Pinselrand beim Bemalen, aber klar genug, um nicht
versehentlich Gras/Wege/Wasser mitzunehmen.

Ausgabeformat (collision.bin, siehe server/src/Lumiis.Data/Maps/CollisionMap.cs fuer den Loader):
  4 Bytes Magic "LCM1" (ASCII)
  4 Bytes Breite  (int32, Little-Endian)
  4 Bytes Hoehe   (int32, Little-Endian)
  dann eine 1-Bit-pro-Zelle-Bitmaske, Little-Endian (Bit 0 = erstes Byte, niedrigstwertiges Bit
  zuerst), zeilenweise (Index = y * Breite + x), 1 = blockiert. Ein Pixel im Quellbild entspricht
  exakt einer Weltenheit/Zelle (siehe GameConstants: MapWidth/Height sind in 1/100-Einheiten).

Aufruf: python3 tools/bake-collision.py data/maps/village/collision-source.png
Schreibt collision.bin ins selbe Verzeichnis wie das Quellbild.
"""
import struct
import sys
from pathlib import Path

from PIL import Image

MAGIC = b"LCM1"
RED_THRESHOLD = 200
GB_THRESHOLD = 100


def bake(source_path: Path) -> Path:
    image = Image.open(source_path).convert("RGB")
    width, height = image.size
    pixels = image.load()

    cell_count = width * height
    packed = bytearray((cell_count + 7) // 8)
    blocked_count = 0

    for y in range(height):
        for x in range(width):
            r, g, b = pixels[x, y]
            if r > RED_THRESHOLD and g < GB_THRESHOLD and b < GB_THRESHOLD:
                blocked_count += 1
                index = y * width + x
                packed[index // 8] |= 1 << (index % 8)

    header = MAGIC + struct.pack("<ii", width, height)
    output_path = source_path.parent / "collision.bin"
    output_path.write_bytes(header + bytes(packed))

    percent = (100.0 * blocked_count / cell_count) if cell_count else 0.0
    print(f"{source_path} -> {output_path}")
    print(f"{width}x{height} Zellen, {blocked_count} blockiert ({percent:.1f}%)")
    return output_path


def main() -> int:
    if len(sys.argv) != 2:
        print("Aufruf: python3 tools/bake-collision.py <quellbild.png>", file=sys.stderr)
        return 1

    source_path = Path(sys.argv[1])
    if not source_path.is_file():
        print(f"Datei nicht gefunden: {source_path}", file=sys.stderr)
        return 1

    bake(source_path)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
