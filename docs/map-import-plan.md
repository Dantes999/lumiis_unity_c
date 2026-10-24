# Karten-Import aus den Lumiis-Referenzprojekten

Stand: 2026-10-24  
Status: Plan / Werkzeug-Grundlage; noch keine Änderung an `data/maps/village`.

> Umgesetzt ist bereits eine **visuelle Vorschau** im Asset-Testlabor:
> `tools/render-lumiis-map-preview.py` rendert `greenfield` nach
> `Resources/Maps/greenfield.png`; der Button "Karten-Vorschau (Greenfield)"
> zeigt sie als Weltbild. Die folgende Server-/Multi-Map-Integration ist noch offen.

## Befund

Referenzkarten in `lumiis/packages/shared/maps`:

| Karte | Größe | Tiles | Spawn | Portale | Mob-Spawns |
|---|---:|---:|---|---:|---:|
| greenfield | 80×80 | 6.400 | (20,58) | 1 → testmap | 13 |
| testmap | 64×64 | 4.096 | (32,32) | 2 | 12 |
| grove | 24×24 | 576 | (12,16) | 1 → testmap | 3 |

Tile-IDs: 0 Gras, 1 Blumen, 2 Weg, 3 Wasser (blockiert), 4 Sand, 5 Baum (blockiert),
6 Fels (blockiert), 7 Stein, 8 Mauer (blockiert), 9 Brücke, 10 Portal, 11 Kaktus
(blockiert), 12 geschlossenes Tor (blockiert), 13 offenes Tor. `lumiis-2d/maps/sohan.json`
ist nur ein Overlay; die Basis wird in `lumiis-2d/shared/maps.js` prozedural erzeugt.

Unity aktuell:
- `GameConstants.MapWidth/Height = 100000` = 1000×1000 Zellen.
- `collision.bin`: Magic `LCM1`, int32 Breite/Höhe, 1 Bit pro Zelle, LSB-first,
  row-major, 1 = blockiert.
- `data/maps/village/collision.bin`, `spawns.json`, `npcs.json`.
- `Program.cs` lädt nur `village`; `World`/`GameServer` kennen eine Karte;
  `Character` hat keine `MapId`; `JoinAcceptedPacket` keine Karten-ID.
- Client zeichnet einen festen 1000×1000-Checkerboden.

## Konvertierung

1. Quellkarten auf **1000×1000 Zellen** skalieren: zentriertes Nearest-Neighbour-Sampling,
   Y-Flip (Quelle y=0 oben → Unity y=0 unten).
2. Kollision: Tile-IDs `{3,5,6,8,11,12}` blockieren; Portale (10) und offene Tore (13) begehbar.
3. Spawns/Portale in Wire-Koordinaten (`cell * 100 + 50`) umrechnen; blockierte
   Spawns auf die nächste begehbare Zelle verschieben (wichtig für `grove`).
4. Portale als Zielzellen-Rechteck + Ankunftspunkt speichern.
5. Ausgabe deterministisch nach `build/map-import/<id>/`:
   `map.json`, `collision.bin`, `tiles.bin`, `spawns.json`, `npcs.json`, `portals.json`.
6. `index.json` mit `startMapId`, Kartenliste und Default-Spawns.

Beispielhafte Zielwerte:
- greenfield Default `(25650, 26850)`; Portal → testmap Rechteck ca. `(450,200)-(462,212)`.
- testmap Default `(50750, 49250)`; Portale nach grove/greenfield.
- grove Default `(52050, 31250)`; zwei ROCK-Spawns werden verschoben.

## Server-Umbau (mehrere Karten)

1. `MapDefinition`, `PortalDefinition`, `MapCatalog` in `Lumiis.Data/Maps`.
2. `MapCatalog` lädt `data/maps/index.json` fail-fast; validiert IDs, Größen,
   Spawns, Portale und Default-Spawns. `village` bleibt Legacy-Fallback.
3. `GameServer` hält `Dictionary<string, World>` und `EntityId -> MapId`;
   `ConnectionState.MapId`; `SendToVisible` nur innerhalb derselben Karte.
4. `Character.MapId` + EF-Migration; `PositionSaveRequest`/`CharacterSaveRequest`
   um MapId erweitern; beim Kartenwechsel sofort speichern.
5. Neues `MapChangedPacket` (Server→Client), `Protocol.Version` erhöhen;
   `JoinAcceptedPacket` um MapId/Kartenmaße erweitern.
6. Client: Bodenmaße aus Paket, bei Kartenwechsel `CleanupEntities`/`World.Reset`,
   Boden neu bauen, Kamera zentrieren, auf neue Spawns warten.
7. Portale: Rechteckprüfung pro Tick, Ziel-Spawn außerhalb des Portalrechtecks,
   kurze Sperre gegen Mehrfachauslösung, Cross-Map-Sichtbarkeitstests.

## Tests

- Konverter: LCM1-Header, Y-Flip, blockierte Tile-IDs, Portalrechtecke,
  Spawn-Korrektur, Determinismus.
- `MapCatalog`: doppelte IDs, falsche Maße, blockierter Default-Spawn,
  fehlendes Portalziel, blockierte Ankunft.
- `World`: Join auf Karte, Kartenmaße, Portal-Erkennung, Respawn auf Karten-Default.
- Integration: greenfield → testmap → grove → zurück, Reconnect auf Karte,
  keine Cross-Map-Entities, Village-Regression.

## Reihenfolge

1. Konverter + Tests + Preview (schreibt nur `build/map-import`).
2. MapCatalog/Index mit `village` + greenfield.
3. Multi-World-Routing + `Character.MapId`.
4. Portale + `MapChangedPacket`.
5. Client-Boden/Reset.
6. testmap/grove, später Sohan (erst Basiskarte exportieren).

## Sohan

`sohan.json` allein ergibt keine Karte. Zuerst in `lumiis-2d` die generierte
640×640-Basiskarte (`buildMap('sohan')`) exportieren, Overlay anwenden, dann
konvertieren. Portale zu nicht vorhandenen Karten zunächst weglassen.
