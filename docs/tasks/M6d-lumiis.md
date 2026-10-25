# M6d - Lumiis-Steine und Boss (MVP+)

Stand: 2026-10-25

Status: Daten-/Katalog-MVP umgesetzt. Die folgende Wellen-Integration ist der naechste Schritt.

## Bereits umgesetzt

- `data/mobs.json`: `Lumiis des Kummers` (Mob 3, langsam/stationaer, 800 HP, hohe Defense,
  garantiertes Material) und `Waldboss` (Mob 4, 1200 HP, aggressiv, Leine 900).
- `data/maps/village/spawns.json`: Spawns 16 (Stein) und 17 (Boss) mit Respawn 120/180 s.
- Client: eigener Stein-/Boss-Look ueber `MonsterShapeFor`/`MonsterColorFor`/`MonsterScaleFor`
  (Stein = blaues Quadrat, Boss = grosses dunkelrotes Dreieck).
- `data/metins.json` + `MetinCatalog`: Wellen bei 75/50/25 % Rest-HP, Validierung gegen `mobs.json`
  inklusive Tests.
- Noch NICHT verdrahtet: `World`/`GameServer` lesen `metins.json` noch nicht; es erscheinen daher
  noch keine Wellen.

## Integration (naechster Schritt)

1. `Program.cs`: `MetinCatalog.LoadFromFile(data/metins.json)`, `ValidateAgainst(mobCatalog)`.
2. `GameServer`/`World`: optionalen `MetinCatalog` und `MobCatalog` an `World` durchreichen.
3. `Entity`: `MetinWavesTriggeredMask` (int) ergaenzen; bei neuem Spawn 0.
4. In `World.ResolveHit` nach `ApplyDamage`:
   - Ziel ist Monster, nicht tot, `metinCatalog.TryGet(target.TypeId, out metin)`.
   - Fuer jede noch nicht ausgeloeste Welle mit `target.CurrentHp * 100 <= target.MaxHp * wave.HpPercent`:
     Maske setzen, `count` Monster ueber `SpawnMonster` in der Naehe des Steins erzeugen
     (`FindNearbyWalkablePoint(target.IntX, target.IntY, 150, 350)`), mit den Werten aus
     `MobCatalog` (kein SpawnPoint, kein Respawn).
   - Wellen-Monster erscheinen beim naechsten `UpdateVisibility` automatisch per `EntitySpawn`.
5. Optional: System-Chat an Spieler im AOI ("Der Lumiis-Stein erzittert ..."), dafuer entweder
   Rueckgabeliste aus `ResolveHit` erweitern oder `World`-Event-Liste im Tick.
6. Tests: Schwellen werden genau einmal ausgeloest; ein Tod vor 25 % loest keine Nachwellen aus;
   Stein-Respawn setzt Maske zurueck; Wellen-MobIds muessen existieren; Integrationstest mit
   zwei Angriffen und sichtbaren neuen Entities.

## Bewusst offen

- Keine Wellen fuer den Boss (nur Stein 3 konfiguriert).
- Keine Stein-spezifische Unverwundbarkeit/Phasen-Animation.
- Kein eigener Loot-Beutel; Drops laufen ueber die normalen `MobDrop`-Tabellen.
- Keine Karten-spezifische Steinplatzierung (folgt mit Multi-Map).
