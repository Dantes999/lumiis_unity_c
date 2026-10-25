# Feature-Gap: Lumiis-Referenzprojekte → lumiis_unity_c

Stand: 2026-10-25

Verglichen:
- `lumiis` (`/home/nutzer/Desktop/lumiis`, TypeScript/Phaser): technischer/UX-Maßstab
- `lumiis-2d` (`/home/nutzer/Desktop/lumiis-2d`, Node/Canvas): Content-/Feature-Maßstab
- `lumiis_unity_c`: aktueller Unity-/C#-Stand

Aufwand: S = wenige Tage, M = 1–2 Wochen, L = mehrere Wochen (inkl. Tests/Doku, 1 Entwickler).

## Kurzfazit

Der Serverkern ist solide: autoritativer 25-Hz-Loop, LiteNetLib, PostgreSQL/EF Core, Token-Auth,
AOI, Kollision, Monster-KI, Kampf, Items/Inventar/Shop, Level/XP/Stats, vier Skills, Paper-Doll
und breite Tests. HUD, Chat, Gruppe inkl. EXP-Teilen, Audio-Platzhalter, 546 Item-Icons,
32 Items, Multi-Map mit zwei Karten/Portalen, Aufwertung +0…+9 sowie ein Lumiis-Stein/Boss-MVP
sind umgesetzt. Offen sind vor allem echte Stein-Wellen, Quests/Trade, echter Kartenimport und
mehr Content.

## P0 – spielbarer Alpha-Test / M7

**Stand 2026-10-25:** HUD, Chat-MVP, Gruppen-MVP, Multi-Map/Portale und Ausrüstungs-Aufwertung
+0…+9 (M6c) sind umgesetzt; Protocol.Version 16. Offen: Party-Rollen/Persistenz und weitere
soziale Integrationstests. YangChanged beim Join ist bereits im Code.

| Feature | Warum | Beleg | Aufwand |
|---|---|---|---|
| Echte HUD-UI (HP/SP/EXP/Yang/Level, Ziel, Skills 1–4 mit Cooldowns, Buffs) | **umgesetzt** (`Hud.uxml`, `HudController`) | `lumiis/packages/client/src/ui/hud.ts` | – |
| Chat (Karte/Global/Flüstern, Systemzeilen, 400-ms-Limit) | **umgesetzt** (`ChatRequest/ChatMessage`, HUD-Chat, Rate-Limit) | `lumiis/packages/shared/src/protocol.ts` (`chat`) | – |
| Gruppe MVP (einladen/annehmen/verlassen, max. 8, Leiter, EXP-Teilen) | **umgesetzt** (Chat-Befehle, `PartyChangedPacket`, HUD-Panel, 50 % XP an andere Mitglieder) | `lumiis-2d/shared/party.js` | – |
| YangChangedPacket beim Join | Debug-Anzeige zeigt bis zum ersten Yang-Ereignis 0 | `GameServer.CompleteJoin` sendet kein YangChangedPacket | S |
| Soziale Integrationstests (zwei Clients/Bots: Chat, Flüstern, Gruppe, EXP) | Neue Pakete brauchen Roundtrip-/Loopback-Tests | `PacketRegistry`, `GameServerIntegrationTests` | S–M |

## P1 – Alpha-Umfang / M8-Betrieb

| Feature | Warum | Beleg | Aufwand |
|---|---|---|---|
| Lumiis-Steine + ein Boss (Wellen 75/50/25 %, Loot, Respawn, Client-View) | **umgesetzt**: `Lumiis des Kummers` (800 HP) und `Waldboss` (1200 HP) inkl. Client-Look; `data/metins.json` + `World.TryTriggerMetinWaves` spawnen 75/50/25-%-Wellen | `lumiis-2d/shared/metins.js`, `server/metin.js`, `server/stages/` | – |
| Aufwertung +0…+9 (DB-Stufen, Material/Schutzrolle, Pakete, UI) | **umgesetzt** (M6c: `data/upgrade.json`, `UpgradeCatalog`, Migration `AddUpgradeLevels`, `UpgradeItem*`-Pakete, Debug-Panel-UI; Zerstoerung ohne Bewahrungsrolle) | `docs/tasks/M6c-upgrade.md` | – |
| Audio + konsistente Kampf-/Loot-VFX | **Audio-Platzhalter umgesetzt** (`AudioService`, prozedurale Clips); VFX weiterhin nur Ringe/Linien/Trail | `lumiis/packages/client/src/audio.ts` | –/M |
| Item-Icons/Tooltips + kleines Content-Paket | **Icons + Content-Paket umgesetzt**: 546 Icons, 32 Items mit iconKey, 13 fehlende Archivdateien nachgeladen; Tooltips weiterhin knapp | `Resources/Icons`, `ItemIconCatalog`, `data/items.json` | –/S |
| Lesbare Kartenoptik / Feldkarte | **Multi-Map umgesetzt** (Dorf + generiertes Grünes Feld, Portale, HUD-Kartenname); Boden weiterhin Platzhalter-Checker, kein echtes Terrain | `docs/map-import-plan.md`, `data/maps/`, `MapCatalog.cs`, `GameClient.RebuildMapVisuals` | M–L |
| Rate-Limits (Verbindung/Chat), Auth-Brute-Force-Schutz, Backups | M8 Alpha-Betrieb | `ServerConfig`, `AuthEndpoints`; Referenz `lumiis/packages/server/src/net/gateway.ts`, `lumiis-2d/docs/SECURITY.md` | S–M |
| Last-/Flow-Tests mit Bots | Bot kann nur bewegen | `lumiis-2d/tests/perf.test.js`, `lumiis-2d/tools/flow/` | M |

## P2 – Post-Alpha / Parität

| Feature | Beleg | Aufwand |
|---|---|---|
| Vier Klassen (Krieger/Ninja/Sura/Schamane), Geschlecht/Frisur, Starter-Sets | `lumiis/packages/shared/src/classes.ts`, `lumiis-2d/shared/classes.js` | L |
| Mehrkarten + Portale + Kanäle | **Portale/Kartenwechsel umgesetzt** (realms/Instanzen/Kanäle weiter offen) | `lumiis/packages/server/src/game/realm.ts`, `lumiis-2d/docs/PORTALE.md`, `docs/map-import-plan.md` | L |
| Map-Editor + Content-Pipeline | `lumiis/packages/client/src/editor/main.ts`, `lumiis-2d/client/editor-map.js` | L |
| Handel/privater Laden (erst nach Item-Instanz-IDs!) | `lumiis-2d/shared/trade.js`, `server/trade.js` | L |
| Quests/Dialoge/Quest-Log | `lumiis-2d/shared/quests.js`, `server/quests.js`, `client/questui.js` | L |
| Sockets/Geiststeine/Refine-Materialien/Lager | `lumiis-2d/shared/sockets.js`, `spirit.js`, `refine.js`, `warehouse.js` | L |
| PvP/Duell/Gilde | `lumiis-2d/shared/pvp.js`, `server/pvp.js`, `duel.js` | L |
| Mobile/Touch, Minimap, Settings, UI-Art | `lumiis/packages/client/src/mobile.ts`, `lumiis-2d/client/mobileui.js`, `minimap.js` | M–L |
| Größerer Item-/Monster-Katalog (L2D: 545 Items/344 Mobs) | 32 Items, 4 Mobs, 4 Skills; Ausbau offen | `lumiis-2d/shared/items.js`, `mobs.js` | M–L |

## Architektur-Risiken

1. **Kartenmaße global**: Multi-Map + `Character.MapId`/Portale sind umgesetzt, aber `GameConstants.MapWidth/Height` gelten weiterhin für alle Karten (1000×1000); die Referenzkarten wurden noch nicht importiert.
2. **Keine `ClassId`**: `Character` hat keine Klassenspalte; `GameServer.DefaultClassName` ist fest „Krieger“.
3. **Keine Item-Instanz-IDs**: Inventar ist nur `(SlotIndex, ItemId)`; Handel/Anti-Dupe erst nach Instanz-ID-Schicht.
4. **UI ist Debug-IMGUI**: für Alpha durch UI Toolkit ersetzen; Login/Escape/Labor sind gute Muster.
5. **Kein Audio-Service** und **keine Rate-Limits**.
6. **Neue Pakete** brauchen `PacketId` + `PacketFactory` + Roundtrip-Test; `Protocol.Version` derzeit 16.
7. **Aufwertung ist an Slot+ItemId gebunden** (noch keine Item-Instanz-IDs): Stufen leben in `CharacterInventoryItem.UpgradeLevel` und den vier `Equipped...UpgradeLevel`-Spalten, nicht an einer global eindeutigen Instanz. Für Handel/Anti-Dupe reicht das nicht.

## Nicht in M7 / Alpha

Quests/Lua, Reittiere, Kostüme, Drachenseelen, Gildenkrieg, weitere Reiche (Multi-Map/Portale
sind umgesetzt); zusätzlich Map-Editor, vier Klassen, Handel, Dungeons, PvP, Touch und
99-Level-Kurve.

## Nächste konkrete Schritte

1. Echte Lumiis-Karten importieren (Skalierung/Konverter aus `docs/map-import-plan.md`), terraintauglicher Boden.
2. Weitere Item-/Monster-Inhalte und echte Referenzkarten importieren.
3. Party-Rollen/Persistenz und weitere soziale Integrationstests.
4. Rate-Limits/Alpha-Betrieb, Lasttests mit Bots.
