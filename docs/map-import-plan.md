# Karten-Import aus den Lumiis-Referenzprojekten

Stand: 2026-10-25  
Status: **Server-/Client-Multi-Map umgesetzt** (Protocol.Version 16). Die Lumiis-Referenzkarten
wurden bewusst noch nicht 1:1 skaliert importiert; `greenfield` ist eine eigene, deterministisch
generierte Testkarte. Die frühere reine Bildvorschau im Asset-Testlabor bleibt zusätzlich bestehen.

> Ursprünglicher Plan (Konvertierung der 80×80-Referenzkarte) ist unten weiter dokumentiert. Der
> tatsächlich umgesetzte Stand weicht in einem Punkt ab: statt Nearest-Neighbour-Skalierung der
> Lumiis-Tiles wurde für die zweite Karte eine einfache, reproduzierbare Kollisionskarte erzeugt
> (`tools/generate-greenfield-map.py`). Das ist für Multi-Map-/Portal-Tests ausreichend und kann
> später durch einen echten Import ersetzt werden, ohne Server-/Client-Code zu ändern.

## Umgesetzt (2026-10-25)

### Datenmodell

- `server/src/Lumiis.Data/Maps/MapDefinition.cs`: `MapDefinition` (Id, Name, `CollisionMap`,
  Startposition, Portale, validierte Monster-/NPC-Platzierungen) und `PortalDefinition`
  (Wire-Rechteck + Zielkarte + Ankunft).
- `server/src/Lumiis.Data/Maps/MapCatalog.cs`: lädt `data/maps/index.json` und je Kartenordner
  `collision.bin`, optional `spawns.json`, `npcs.json`, `portals.json`, `map.json`. Fail-fast:
  doppelte Karten-/Portal-Ids, fehlende Kollision, blockierte Startposition, unbekanntes
  Portalziel, blockierte Ankunft, Ankunft in einem Portal der Zielkarte (Endlosschleife),
  ungültige Rechtecke.
- `data/maps/index.json` listet `map1` (Default), `village` (Legacy/Test) und `greenfield`; `startMapId` ist `map1`.
- `data/maps/village/map.json` (Name „Dorf“, Start 50000/50000) und `portals.json`
  (Portal 1: Rechteck 52000..52800 / 49500..50500 → `greenfield` 50000/65000).
- `data/maps/greenfield/`: eigene Kollision (`collision-source.png` + `collision.bin`, Generator
  `tools/generate-greenfield-map.py`), Name „Gruenes Feld“, Start 50000/50000, sechs Monster-
  Spawns, leere NPC-Liste, Rückportal nach `village` (Rechteck 49500..50500 / 59500..60500 →
  `village` 53000/50000).

### Server

- `Character.MapId` (DB-Spalte, Default `village`, Migration `AddCharacterMapId`); bestehende
  Charaktere landen automatisch in `village`, unbekannte gespeicherte Ids fallen beim Join auf die
  Startkarte zurück (Warnung im Log, kein Join-Abbruch).
- `GameServer` hält `Dictionary<string, World>` (eine World je Karte) und einen gemeinsamen
  EntityId-Zähler. `ConnectionState.World` bestimmt AOI/Sichtbarkeit; `SendToVisible`,
  `UpdateVisibility`, Bewegung/Kollision, Monster-KI, Spawns/Respawns und NPCs laufen pro Karte.
- Portale: `World.CheckPortal` (Rechteck, 1 s Sperre, tote Spieler lösen nicht aus);
  `GameServer.TransitionEntityToMap` entfernt die Entity aus der alten World, schickt Despawn an
  die alten Beobachter, setzt sie in die neue World, leert `VisibleEntityIds`, sendet
  `MapChangedPacket` und persistiert Position+MapId sofort. Fremde Entities der neuen Karte
  erscheinen über den normalen AOI-Diff.
- Persistenz: `PositionSaveRequest` und `CharacterSaveRequest` tragen `MapId`; 30-s-Save,
  Disconnect-Save und Portal-Sofortsave schreiben sie.
- Respawn/Start: Spieler-Respawn und „im Tod ausgeloggt“ nutzen die Startposition der jeweiligen
  Karte, nicht mehr die globale Kartenmitte. Eine beim Join blockierte/aus einer anderen Karte
  stammende gespeicherte Position fällt defensiv auf den Kartenstart zurück.
- Chat-Kanal „Karte“ broadcastet nur noch an Spieler derselben Karte (Global weiterhin an alle);
  Gruppe/Whisper bleiben kartenübergreifend.
- Dev-Tool `DebugChangeMapRequestPacket` (nur mit `--allow-debug-commands`) versetzt den eigenen
  Charakter auf die Startposition einer Karte - Debug-API ohne Portal.

### Client

- `JoinAcceptedPacket` trägt `MapId`/`MapName`/`MapWidth`/`MapHeight`; neues
  `MapChangedPacket` (1022) trägt dieselben Felder plus Zielposition.
- `ClientWorld` hält MapId/Name/Maße, `HandleMapChanged` leert alle Entities (Fortschritt und
  aktive Buffs bleiben erhalten) und legt die eigene Figur an der Zielposition neu an.
- `GameClient` baut bei Join/Wechsel Boden, Kartenränder und Portalmarkierungen neu
  (`RebuildMapVisuals`); Portale/Kartenliste kommen aus `MapCatalogClient`
  (StreamingAssets/`lumiis-data/maps`, vom Shared-Build mitkopiert). Kartenname im HUD
  (`hud-map-name`), Debug-Panel-Zeile „Karte wechseln“.
- Eine clientseitige Kollisionsprüfung gab es vorher nicht und gibt es weiterhin nicht - der
  Server bleibt allein autoritativ. Es musste daher nichts „pro Karte“ umgestellt werden.

### Tests

- `MapCatalogTests` (13): Roundtrip/Validierung, doppelte IDs, blockierter Start, unbekanntes
  Portalziel, blockierte Ankunft, Ankunft im Zielportal, invertiertes/außerhalb liegendes Rechteck,
  echte `data/maps`-Daten (village + greenfield).
- `WorldPortalTests` (7): Portalerkennung/-Cooldown/Tod, World-Transfer mit gleicher EntityId,
  Kartenstart als Respawn-Ziel, getrennte Entities/Kollision pro World.
- `PacketCodecTests`: `JoinAccepted` (Map-Felder), `MapChanged`, `DebugChangeMapRequest`.
- `ClientWorldTests`: Map-Info aus JoinAccepted, `HandleMapChanged` (Entities weg, lokale Identität
  und Fortschritt bleiben), `ClearEntities` behält Karte/Fortschritt.
- Integration (`GameServerIntegrationTests`, 5 neue Tests mit echten `data/maps`): Monster/NPCs
  pro Karte getrennt; Join auf village samt Kartenmetadaten und Village-AOI; Portalwechsel
  (MapChanged, Despawn beim Beobachter, sofortige Persistenz, Disconnect/Rejoin auf greenfield);
  unbekannte MapId → village; Debug-Kartenwechsel nur mit Flag.

## Offene Punkte / Limitations

- **Kein echter Lumiis-Kartenimport**: `greenfield` ist eine generierte Testkarte, keine
  skalierte 80×80-Referenz. `tiles.bin`/Terrain-Rendering gibt es weiterhin nicht; der Client
  zeichnet den bekannten Platzhalter-Checkerboden (jetzt mit den Maßen aus dem Paket).
- Karten haben weiterhin global `GameConstants.MapWidth/Height` (1000×1000 Zellen). Kleinere
  Karten pro Map wären ein eigener Schritt (Client nutzt bereits die Paketmaße).
- **testmap/grove/sohan** aus der Referenz sind nicht angelegt; Portale zu nicht existierenden
  Karten würden `MapCatalog` beim Start abbrechen lassen (bewusst).
- Group/Party ist kartenübergreifend; Entfernung/„nur gleiche Karte“ ist nicht Teil des MVP.
- Kein Client-seitiges Kollisionsrendering (unverändert), keine Minimap, keine Kartengrenzen-
  Darstellung der Blockierzonen.
- Portale sind rein optische Rechtecke aus StreamingAssets; der Server prüft unabhängig davon.

## Ursprünglicher Befund (weiterhin gültig als Referenz)

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

## Konvertierung (weiterhin der Plan für einen echten Import)

1. Quellkarten auf **1000×1000 Zellen** skalieren: zentriertes Nearest-Neighbour-Sampling,
   Y-Flip (Quelle y=0 oben → Unity y=0 unten).
2. Kollision: Tile-IDs `{3,5,6,8,11,12}` blockieren; Portale (10) und offene Tore (13) begehbar.
3. Spawns/Portale in Wire-Koordinaten (`cell * 100 + 50`) umrechnen; blockierte
   Spawns auf die nächste begehbare Zelle verschieben (wichtig für `grove`).
4. Portale als Zielzellen-Rechteck + Ankunftspunkt speichern.
5. Ausgabe deterministisch nach `build/map-import/<id>/`:
   `map.json`, `collision.bin`, `tiles.bin`, `spawns.json`, `npcs.json`, `portals.json`.
6. `index.json` mit `startMapId`, Kartenliste und Default-Spawns.

## Sohan

`sohan.json` allein ergibt keine Karte. Zuerst in `lumiis-2d` die generierte
640×640-Basiskarte (`buildMap('sohan')`) exportieren, Overlay anwenden, dann
konvertieren. Portale zu nicht vorhandenen Karten zunächst weglassen.
