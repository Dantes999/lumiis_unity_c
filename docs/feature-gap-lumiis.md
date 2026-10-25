# Feature-Gap: Lumiis-Referenzprojekte → lumiis_unity_c

Stand: 2026-10-24

Verglichen:
- `lumiis` (`/home/nutzer/Desktop/lumiis`, TypeScript/Phaser): technischer/UX-Maßstab
- `lumiis-2d` (`/home/nutzer/Desktop/lumiis-2d`, Node/Canvas): Content-/Feature-Maßstab
- `lumiis_unity_c`: aktueller Unity-/C#-Stand

Aufwand: S = wenige Tage, M = 1–2 Wochen, L = mehrere Wochen (inkl. Tests/Doku, 1 Entwickler).

## Kurzfazit

Der Serverkern ist solide: autoritativer 25-Hz-Loop, LiteNetLib, PostgreSQL/EF Core, Token-Auth,
AOI, Kollision, Monster-KI, Kampf, Items/Inventar/Shop, Level/XP/Stats, vier Skills, Paper-Doll
und breite Tests. Es fehlt aber ein **spielbares Alpha-HUD** und das **soziale Spiel**:
eine Klasse, eine Karte, zwei Monster, ein Händler, acht Items, vier Skills, kein Chat, keine
Gruppe, keine Quests, keine Lumiis-Steine, keine Bosse, kein Audio, keine echten Item-Icons
(seit 2026-10-24 importiert), keine echten Health-Bars. Die nächste Aufgabe laut
`CLAUDE.md` ist **M7 Sozial**.

## P0 – spielbarer Alpha-Test / M7

**Stand 2026-10-24:** HUD, Chat-MVP und Gruppen-MVP (Chat-Befehle, PartyChanged, HUD-Panel) sind umgesetzt; Protocol.Version 14. Offen: Party-Rollen/Persistenz und weitere soziale Integrationstests. YangChanged beim Join ist bereits im Code.

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
| Lumiis-Steine + ein Boss (Wellen 75/50/25 %, Loot, Respawn, Client-View) | explizit Alpha-Umfang; fehlt | `lumiis-2d/shared/metins.js`, `server/metin.js`, `server/stages/` | L |
| Aufwertung +0…+9 (DB-Stufen, Material/Schutzrolle, Pakete, UI) | Design liegt fertig vor; günstiger Alpha-Gewinn | `docs/tasks/M6c-upgrade.md` | M |
| Audio + konsistente Kampf-/Loot-VFX | **Audio-Platzhalter umgesetzt** (`AudioService`, prozedurale Clips); VFX weiterhin nur Ringe/Linien/Trail | `lumiis/packages/client/src/audio.ts` | –/M |
| Item-Icons/Tooltips + kleines Content-Paket | Icons sind jetzt importiert; Item-Katalog noch 8 Einträge | `Resources/Icons`, `ItemIconCatalog`, `data/items.json` | S–M |
| Lesbare Kartenoptik / Feldkarte | Boden ist Platzhalter-Checker; Alpha nennt Startdorf + Feld | `PlaceholderSprites.cs`, `data/maps/village/collision-source.png` | M–L |
| Rate-Limits (Verbindung/Chat), Auth-Brute-Force-Schutz, Backups | M8 Alpha-Betrieb | `ServerConfig`, `AuthEndpoints`; Referenz `lumiis/packages/server/src/net/gateway.ts`, `lumiis-2d/docs/SECURITY.md` | S–M |
| Last-/Flow-Tests mit Bots | Bot kann nur bewegen | `lumiis-2d/tests/perf.test.js`, `lumiis-2d/tools/flow/` | M |

## P2 – Post-Alpha / Parität

| Feature | Beleg | Aufwand |
|---|---|---|
| Vier Klassen (Krieger/Ninja/Sura/Schamane), Geschlecht/Frisur, Starter-Sets | `lumiis/packages/shared/src/classes.ts`, `lumiis-2d/shared/classes.js` | L |
| Mehrkarten + Portale + Kanäle | `lumiis/packages/server/src/game/realm.ts`, `lumiis-2d/docs/PORTALE.md` | L |
| Map-Editor + Content-Pipeline | `lumiis/packages/client/src/editor/main.ts`, `lumiis-2d/client/editor-map.js` | L |
| Handel/privater Laden (erst nach Item-Instanz-IDs!) | `lumiis-2d/shared/trade.js`, `server/trade.js` | L |
| Quests/Dialoge/Quest-Log | `lumiis-2d/shared/quests.js`, `server/quests.js`, `client/questui.js` | L |
| Sockets/Geiststeine/Refine-Materialien/Lager | `lumiis-2d/shared/sockets.js`, `spirit.js`, `refine.js`, `warehouse.js` | L |
| PvP/Duell/Gilde | `lumiis-2d/shared/pvp.js`, `server/pvp.js`, `duel.js` | L |
| Mobile/Touch, Minimap, Settings, UI-Art | `lumiis/packages/client/src/mobile.ts`, `lumiis-2d/client/mobileui.js`, `minimap.js` | M–L |
| Größerer Item-/Monster-Katalog (L2D: 545 Items/344 Mobs) | `lumiis-2d/shared/items.js`, `mobs.js` | M–L |

## Architektur-Risiken

1. **Eine Karte ist hartkodiert**: `Program.cs` lädt nur `data/maps/village`; `GameConstants.MapWidth/Height` sind global; `Character` hat keine `MapId`.
2. **Keine `ClassId`**: `Character` hat keine Klassenspalte; `GameServer.DefaultClassName` ist fest „Krieger“.
3. **Keine Item-Instanz-IDs**: Inventar ist nur `(SlotIndex, ItemId)`; Handel/Anti-Dupe erst nach Instanz-ID-Schicht.
4. **UI ist Debug-IMGUI**: für Alpha durch UI Toolkit ersetzen; Login/Escape/Labor sind gute Muster.
5. **Kein Audio-Service** und **keine Rate-Limits**.
6. **Neue Pakete** brauchen `PacketId` + `PacketFactory` + Roundtrip-Test; `Protocol.Version` derzeit 12.

## Nicht in M7 / Alpha

Quests/Lua, Reittiere, Kostüme, Drachenseelen, Gildenkrieg, weitere Reiche; zusätzlich
Map-Editor, Mehrkarten/Portale, vier Klassen, Handel, Dungeons, PvP, Touch und 99-Level-Kurve.

## Nächste konkrete Schritte

1. HUD als UI-Toolkit-Wurzel (HP/SP/EXP/Yang/Level/Skills/Buffs) + F1 nur noch Debug.
2. Chat-Pakete + Chat-Panel + 400-ms-Limit + Tests.
3. Party-MVP (max. 8, Leiter, EXP-Teilen) + Party-Panel + Tests.
4. YangChangedPacket beim Join.
5. Danach P1: Lumiis-Stein/Boss, M6c-Upgrade, Audio/VFX, Rate-Limits.
