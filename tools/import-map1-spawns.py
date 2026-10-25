"""Setzt die Map1-Monster (Yongan-Ausschnitt) aus den Originaldaten von lumiis-2d.

Aufruf (Repo-Root): python3 tools/import-map1-spawns.py

Quelle: tools/map1-yongan-spawns.json (Gruppen, Lumiis-Stein-Punkte und Monsterwerte von lumiis-2d@4376dae,
nur der Map1-Ausschnitt). Schreibt bzw. aktualisiert:
- data/mobs.json: Mob-Typen 20-24 (Wilder Hund, Wolf, Alphawolf, Wildschwein, Lumiis des Kummers)
- data/metins.json: Wellen des Lumiis des Kummers (Mob 24)
- data/maps/map1/spawns.json: jede Originalgruppe 1:1 (ausser in der Sicherheitszone um das Dorf), feste
  Lumiis-Stein-Punkte und die Trainingspuppen am Dorfplatz

Level, HP, Angriff, Verteidigung, Erfahrung, Angriffstempo und aggressiv/passiv kommen 1:1 aus dem Original.
Laufgeschwindigkeit, Reichweiten, Yang und Beute sind an unser Wire-System angepasst (wie bei Map2).
Im Original-Yongan gibt es keinen Boss.
"""
import json
import struct
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
SRC = ROOT / "tools" / "map1-yongan-spawns.json"
MOBS = ROOT / "data" / "mobs.json"
METINS = ROOT / "data" / "metins.json"
MAP1 = ROOT / "data" / "maps" / "map1"

CROP_X0, CROP_Y0, CROP = 250, 210, 128  # siehe tools/map1-yongan-crop.json
ORIGIN = 400                             # siehe tools/import-map1.py

MOB_IDS = {"wild_dog": 20, "wolf": 21, "alpha_wolf": 22, "wild_boar": 23, "metin_sorrow": 24}
DUMMY_MOB_ID = 6
DUMMIES = [(45400, 45500), (45700, 45500), (46000, 45500)]  # Westrand des Dorfplatzes
# Beute: wir haben (noch) keine Originalbeute-Tabellen, daher die bisherigen Listen wiederverwendet.
DROPS = {
    "animal": [{"itemId": 8, "chancePercent": 30}, {"itemId": 6, "chancePercent": 10},
               {"itemId": 40, "chancePercent": 20}, {"itemId": 41, "chancePercent": 10}],
    "boar": [{"itemId": 4, "chancePercent": 20}, {"itemId": 7, "chancePercent": 15},
             {"itemId": 42, "chancePercent": 15}, {"itemId": 43, "chancePercent": 20}, {"itemId": 44, "chancePercent": 8}],
    "stone": [{"itemId": 2, "chancePercent": 100}, {"itemId": 5, "chancePercent": 25},
              {"itemId": 45, "chancePercent": 25}, {"itemId": 49, "chancePercent": 20}, {"itemId": 50, "chancePercent": 15}],
}
STONES_PLACED = 5  # Original: 5 gleichzeitig, zufaellig verteilt; hier die ersten 5 Punkte im Ausschnitt fest


def cell_of(tx, ty):
    return ORIGIN + (tx - CROP_X0), ORIGIN + (CROP - 1) - (ty - CROP_Y0)


def load_collision():
    data = (MAP1 / "collision.bin").read_bytes()
    width, height = struct.unpack("<ii", data[4:12])

    def blocked(cx, cy):
        if not (0 <= cx < width and 0 <= cy < height):
            return True
        i = cy * width + cx
        return (data[12 + i // 8] >> (i % 8)) & 1 == 1
    return blocked


def mob_entry(key, src):
    stone = key.startswith("metin_")
    level = src["level"]
    entry = {
        "id": MOB_IDS[key],
        "name": src["name"],
        "speedWire": 1 if stone else round(src["speed"] * 140),
        "wanderRadiusWire": 1 if stone else 400,
        "wanderIntervalMinMs": 60000 if stone else 3000,
        "wanderIntervalMaxMs": 60000 if stone else 7000,
        "maxHp": src["hp"],
        "attack": src["atk"],
        "defense": src["def"],
        "yangMin": level * (40 if stone else 3),
        "yangMax": level * (80 if stone else 6),
        "meleeRangeWire": 180 if stone else 150,
        "attackCooldownMs": 1400 if stone else src["cooldown"],
        "aggressive": src["aggressive"],
        "aggroRangeWire": 500 if src["aggressive"] else 0,
        "leashRangeWire": 200 if stone else 900,
        "level": level,
        "baseXp": src["exp"],
        "drops": DROPS["stone" if stone else "boar" if key == "wild_boar" else "animal"],
    }
    if stone:
        entry["fightsBack"] = False  # Steine greifen im Original nicht an (atk 0)
    return entry


def main():
    src = json.loads(SRC.read_text())
    blocked = load_collision()

    mobs_doc = json.loads(MOBS.read_text())
    new_ids = set(MOB_IDS.values())
    mobs = [m for m in mobs_doc["mobs"] if m["id"] not in new_ids]
    mobs += [mob_entry(k, src["mobs"][k]) for k in MOB_IDS]
    mobs.sort(key=lambda m: m["id"])
    mobs_doc["mobs"] = mobs
    MOBS.write_text(json.dumps(mobs_doc, indent=2, ensure_ascii=False) + "\n")

    metin_doc = json.loads(METINS.read_text())
    sorrow = src["metins"][0]
    waves = []
    for hp_percent, members in zip((75, 50, 25), sorrow["waves"]):
        for key in dict.fromkeys(members):
            waves.append({"hpPercent": hp_percent, "mobId": MOB_IDS[key], "count": members.count(key)})
    metin_doc["metins"] = [m for m in metin_doc["metins"] if m["mobId"] != MOB_IDS["metin_sorrow"]]
    metin_doc["metins"].append({"mobId": MOB_IDS["metin_sorrow"], "waves": waves})
    METINS.write_text(json.dumps(metin_doc, indent=2, ensure_ascii=False) + "\n")

    safe = src["safe"]

    def in_safe(tx, ty):
        return any(s["x0"] <= tx <= s["x1"] and s["y0"] <= ty <= s["y1"] for s in safe)

    offsets = [(0, 0), (1, 0), (0, 1), (-1, 0), (0, -1), (1, 1), (-1, -1), (1, -1), (-1, 1)]
    spawns = []
    skipped = 0

    def add(mob_id, tx, ty, respawn):
        nonlocal skipped
        ring = [(dx, dy) for r in range(4) for dy in range(-r, r + 1) for dx in range(-r, r + 1)
                if max(abs(dx), abs(dy)) == r]
        for dx, dy in ring:
            cx, cy = cell_of(tx + dx, ty + dy)
            if not blocked(cx, cy):
                spawns.append({"id": len(spawns) + 1, "mobId": mob_id, "positionX": cx * 100 + 50,
                               "positionY": cy * 100 + 50, "respawnSeconds": respawn})
                return
        skipped += 1

    for group in src["groups"]:
        if in_safe(group["tx"], group["ty"]):
            continue
        respawn = max(10, group["respawnMs"] // 1000)
        for i, key in enumerate(group["members"]):
            ox, oy = offsets[i % len(offsets)]
            add(MOB_IDS[key], group["tx"] + ox, group["ty"] + oy, respawn)

    stone_respawn = sum(sorrow["respawnMs"]) // 2 // 1000
    for tx, ty in [p for p in sorrow["pointsInCrop"] if not in_safe(*p)][:STONES_PLACED]:
        add(MOB_IDS["metin_sorrow"], tx, ty, stone_respawn)

    for x, y in DUMMIES:
        spawns.append({"id": len(spawns) + 1, "mobId": DUMMY_MOB_ID, "positionX": x, "positionY": y, "respawnSeconds": 5})

    (MAP1 / "spawns.json").write_text(json.dumps({"spawns": spawns}, indent=2) + "\n")
    counts = {}
    for s in spawns:
        counts[s["mobId"]] = counts.get(s["mobId"], 0) + 1
    print(f"{len(spawns)} Spawns geschrieben (je Mob: {counts}), {skipped} uebersprungen (kein freies Tile)")


if __name__ == "__main__":
    main()
