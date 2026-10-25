# Lumiis – Projektbriefing für Claude Code

Unity-2D-Online-RPG. Mechaniken 1:1 wie Metin2 "unter der Haube", eigene Optik (Pixelart/Comic). Am Anfang wenige Spieler. "Lumiis" ist auch der eigene Name für Metin-Steine im Spiel. Getrennter, autoritativer Server.

## Verbindliche Architektur
- Server: C# / .NET 10, ein Prozess, EIN Game-Loop-Thread, 25 Ticks/s. DB-Zugriffe (ab M2b im Spielserver) asynchron, nie den Loop blockieren.
- Netzwerk: LiteNetLib (reliable ordered). Server ist autoritativ. Der Client schickt nur Absichten (MoveTo, Attack, ...) und rendert Serverzustand. Keine Client-Prediction im MVP.
- server/src/Lumiis.Shared: netstandard2.1, LangVersion 9.0, KEINE Abhängigkeiten. Enthält Pakete, Reader/Writer, Enums, Konstanten, Kampfformeln. Wird später als DLL nach client/Assets/Plugins kopiert. Deshalb dort: keine records/init, keine file-scoped namespaces, keine Reflection, kein veränderlicher statischer Zustand, keine Floats auf der Leitung.
- Koordinaten auf der Leitung: int in 1/100 Welteinheit.
- Spiellogik (World, Entities, Movement, Combat) kennt weder LiteNetLib noch Sockets und ist ohne Netzwerk testbar. Transport-Adapter sind dünn.
- Spieldaten (Mobs, Items, Skills, Drops, Maps) als JSON in /data (Prinzip mob_proto/item_proto), beim Serverstart validiert. Nichts hartkodieren, was Spieldaten sind.
- DB ab M2a: PostgreSQL + EF Core (docker compose), siehe Abschnitt "Datenbank & Auth-Dienst". Zugriff nur über server/src/Lumiis.Data (Entities, DbContext). Sowohl Lumiis.Auth (Account/Charakter-Verwaltung) als auch seit M2b der Spielserver (Lumiis.Server, Charakter-Lookup beim Join, Positions-Speicherung) greifen darüber zu - der Spielserver macht das ausschließlich asynchron auf Threadpool-Threads, nie auf dem Game-Loop-Thread (siehe Konventionen).
- Client: Unity 6.3 LTS (6000.3.24f1), 2D Top-Down, 4 Richtungen, code-first (Bootstrap-Szene erzeugt alles per Code, Platzhalter-Grafik wird generiert), UI per UI Toolkit (UXML/USS). Kein Inspector-Verkabeln, keine veränderlichen statischen Zustände (Fast Enter Play Mode / CoreCLR).
- Tests: xUnit (server/tests) plus Konsolen-Bot (Lumiis.Bot).

## Repo
client/ (Unity-Projekt), server/ (Solution mit Shared, Server, Bot, Data, Auth, Tests), data/, docs/, tools/.

## Konventionen
- Bezeichner englisch, Kommentare dürfen deutsch sein. Kommunikation mit dem User: Deutsch.
- Server, Bot und Tests dürfen modernes C# nutzen. Nur Shared ist auf C# 9 begrenzt.
- Jedes neue Paket: PacketId + Eintrag in PacketFactory + Round-trip-Test. Server->Client-IDs ab 1000. Inkompatible Protokolländerung: Protocol.Version erhöhen.
- Client-Eingaben nie vertrauen: alles validieren, kaputte Pakete führen zum Trennen der Verbindung, der Server darf nie abstürzen.
- Zwei Rechner (Linux Mint, Windows) über Git: Dateinamen exakt (Linux ist case-sensitive), Unity-Code/Assets in PascalCase, Dateien in /data klein mit Bindestrich, Zeilenenden LF.
- Fristen in Wandzeit (ms) mit injizierbarer Uhr, Ticks nur für die Simulation: Verbindungen ohne gültigen Join (`ServerConfig.JoinTimeoutMs`, Default 5000) oder nach JoinRejected (`ServerConfig.RejectGraceMs`, Default 1000) werden erst nach dieser wandzeitbasierten Frist getrennt (weitere eingehende Pakete werden währenddessen ignoriert), damit ein reliable-Resend des JoinRejected noch ankommt; `GameServer` nimmt dafür einen injizierbaren `Func<long> nowMs` (Default: monotone Stopwatch-Zeit), Tests nutzen dort eine Fake-Uhr für deterministische Zeitsprünge. `NetManager.UpdateTime` ist fest auf 15 ms gesetzt, `PollEvents()` verschickt keine Pakete. Clients werten jeden Disconnect ohne vorheriges JoinAccepted als generischen Fehlschlag.
- World und Entities werden nur vom Game-Loop-Thread angefasst. Hintergrundarbeit (z. B. DB) übergibt Ergebnisse per Queue an den Loop.
- Keine Original-Assets, Referenzbilder oder Namen aus Metin2 oder anderen Spielen. Herkunft jedes Assets in docs/art-provenance.md.
- Der Client berechnet nie Spielzustand. Eigene Charakterdaten kommen im JoinAccepted (später als eigenes Paket), öffentliche Daten über EntitySpawn.
- Unity-Ordner nie blind mit git add -A stagen. Vor jedem Commit git status --short und git diff --cached --stat prüfen. ProjectSettings/, Packages/ und Assets/Settings/ nur gezielt stagen. Dateien mit Konto-, Cloud-, Lizenz- oder Signaturdaten nie committen, dem User melden.
- Ignore-Regeln pro Teilbaum (server/, client/), keine generischen Muster im Root. Vor jedem Push ./tools/check-repo.sh ausführen.

## Befehle
- Build/Test: cd server && dotnet build && dotnet test (läuft ohne laufendes Postgres - die Lumiis.Auth-Tests nutzen EF-Core-InMemory, siehe Status M2a)
- Server: dotnet run --project server/src/Lumiis.Server (--data-dir überschreibt den Basispfad für Spieldaten, Default "data" relativ zum Arbeitsverzeichnis - lädt data/maps/index.json inkl. aller Karten, siehe Abschnitte "Kartenkollision"/"Multi-Map und Portale"; --allow-debug-commands NUR für lokale Entwicklung, siehe Abschnitt "Dev-Tools")
- Bot: dotnet run --project server/src/Lumiis.Bot -- --count 3
- Umgebungsvariablen laden: source tools/dev-env.sh (mit "source", NICHT ./ - sonst gelten die Variablen nur im Subprozess des Skripts). Davor nötig für docker compose, dotnet ef, dotnet run --project server/src/Lumiis.Auth UND (seit M2b) server/src/Lumiis.Server.
- Postgres starten: docker compose up -d (Repo-Root, nach source tools/dev-env.sh)
- Migration anwenden: cd server/src/Lumiis.Auth && dotnet ef database update --project ../Lumiis.Data (nach source tools/dev-env.sh; Migrationen liegen in Lumiis.Data, Lumiis.Auth referenziert Microsoft.EntityFrameworkCore.Design zusätzlich selbst, damit es als Startup-Projekt für dotnet ef taugt)
- Auth-Dienst: dotnet run --project server/src/Lumiis.Auth (nach source tools/dev-env.sh; Port 5080, siehe Abschnitt "Datenbank & Auth-Dienst")
- Unity-PlayMode-Tests: tools/test-client-playmode.sh (siehe Abschnitt "Teststrategie"; Unity-Editor muss geschlossen sein, Projekt-Lockfile)

## Teststrategie
Zwei Ebenen, seit der Client-Testinfrastruktur-Aufgabe (Anlass: mehrere M2c-Login-Laufzeitfehler bestanden den
Batchmode-Compile-Check, waren aber nur im Play-Modus sichtbar):
- `cd server && dotnet test` reicht für alles, was keine tatsächliche Unity-Laufzeitumgebung braucht: Server-
  Logik UND client-seitige Zustands-/Entscheidungslogik ohne UnityEngine-Referenz (`ClientWorld.cs`,
  `LoginFlow.cs`/`IAuthClient.cs`) - per Compile-Include in `Lumiis.Tests.csproj` direkt mitgetestet, siehe
  Abschnitt "Client". Das ist der Regelfall und läuft ohne Unity-Lizenz/-Editor.
- `tools/test-client-playmode.sh` (Unity-Batchmode, `-runTests -testPlatform PlayMode`) ist zusätzlich nötig,
  wenn sich UnityEngine-spezifisches Laufzeitverhalten ändert, das `dotnet test` grundsätzlich nicht sehen
  kann: UIDocument/UI-Toolkit-Timing (`rootVisualElement`, Sichtbarkeit/Layout), MonoBehaviour-Lebenszyklus
  (`Awake`/`Start`/Coroutines), tatsächliche Szenenobjekte. Tests liegen in
  `client/Assets/Lumiis/Tests/PlayMode/` (eigenes Assembly `Lumiis.Tests.PlayMode`, referenziert
  `Lumiis.Client.asmdef`). Bekannte Lücke: deckt nur PlayMode ab, kein EditMode/Build-Player-Testlauf.
- Konvention: neue Login-/Charakter-Logik zuerst in `LoginFlow` (UnityEngine-frei, per `dotnet test` prüfbar)
  implementieren und testen - `LoginController` bleibt bewusst dünne UI-Verdrahtung (Button-Klicks ->
  LoginFlow-Methoden, LoginFlow.State/-ErrorMessage -> UI-Anzeige), keine Entscheidungslogik dort. Gilt
  analog für zukünftige, ähnlich gelagerte Unity-Controller.

## Datenbank & Auth-Dienst
Pflicht-Umgebungsvariablen (siehe .env.example, .env selbst ist git-ignoriert), nur EIN Variablensatz, gelesen von docker-compose.yml UND von jedem .NET-Prozess, der sie braucht - kein eigener POSTGRES_*-Satz mehr, keine zwei Werte, die auseinanderlaufen können. Seit M2b gilt das nicht mehr nur für Lumiis.Auth, sondern genauso für Lumiis.Server (derselbe Fail-Fast-Mechanismus, dieselbe klare Abbruchmeldung):
- LUMIIS_DB_NAME, LUMIIS_DB_USER, LUMIIS_DB_PASSWORD (kein Default, Lumiis.Auth, Lumiis.Server UND die Design-Time-Factory für dotnet ef brechen ohne abweichenden Fallback mit klarer Meldung ab, wenn eine fehlt - siehe server/src/Lumiis.Data/LumiisDbContextFactory.cs). LUMIIS_DB_HOST/PORT haben Defaults (localhost/5432).
- LUMIIS_TOKEN_SECRET (mind. 32 Byte, kein Default, gleicher Abbruch-Mechanismus) - Signier-Geheimnis für Session-Token (server/src/Lumiis.Data/TokenService.cs), von Lumiis.Auth (Ausstellen) UND Lumiis.Server (lokale Prüfung beim Join, kein HTTP-Aufruf zu Auth) genutzt.

Setup: `cp .env.example .env`, eigene Werte eintragen, dann **immer** `source tools/dev-env.sh` VOR docker compose/dotnet ef/dotnet run ausführen (lädt .env in die aktuelle Shell) - sonst brechen diese Befehle mit einer Umgebungsvariablen-Fehlermeldung ab (oder, bei `dotnet ef`, mit einer irreführenden Postgres-Authentifizierungs-Fehlermeldung, wenn zufällig noch ein alter Container mit anderem Passwort läuft). Danach `docker compose up -d`, dann die Migration anwenden (Befehl oben). `dotnet run --project server/src/Lumiis.Auth` migriert in Development automatisch beim Start (nur wenn ASPNETCORE_ENVIRONMENT=Development), sonst nur eine Warnung.

Lumiis.Auth (server/src/Lumiis.Auth, ASP.NET Core Minimal API, nur HTTP, Port 5080 per appsettings.json/launchSettings.json, überschreibbar über die Standard-ASPNETCORE_URLS/--urls-Mechanismen):
- POST /register { username, password } -> 201 { accountId } | 409 | 400
- POST /login { username, password } -> 200 { token, expiresAtUnix, characters: [Charakter] } | 401 (keine Unterscheidung Name/Passwort in der Antwort)
- GET /characters, Header "Authorization: Bearer <token>" -> 200 { characters: [Charakter] } | 401
- POST /characters, Header "Authorization: Bearer <token>", Body { name, bodyId?, hairStyleId?, hairColorId? } -> 201 Charakter | 401 | 400 | 409 (fehlende Aussehenswerte: `LookCatalog.CreateDefaultAppearance()`; angegebene werden gegen looks.json geprüft, sonst 400 "Ungültiges Aussehen.")
- Charakter = { id, name, level, mapId, appearance: { bodyId, hairStyleId, hairColorId, armorId, helmetId, weaponId, shieldId } (LookIds, über `Lumiis.Data.CharacterAppearance` genauso aufgelöst wie im Spielserver), equipment: { armorItemId, helmetItemId, weaponItemId, shieldItemId, ...UpgradeLevel } } - für die Vorschau in der Charakterauswahl. Lumiis.Auth lädt dafür zusätzlich items.json.

Fehlerantworten immer als knappes JSON { error: "..." }, nie ein Stacktrace nach außen. Session-Token: eigenes, minimales Format (kein JWT-Standard), signiert per HMACSHA256, 30 Minuten gültig, trägt nur die AccountId (siehe server/src/Lumiis.Data/TokenService.cs).

Spielserver-Join (Lumiis.Server, seit M2b, Protocol.Version 3): JoinRequestPacket trägt Token + CharacterId
statt Name. Ablauf: Token sofort synchron per TokenService.Validate geprüft (InvalidToken/TokenExpired als
unterschiedliche JoinRejectReason) -> bei gültigem Token läuft der Charakter-Lookup (CharacterId + AccountId
aus dem Token, Eigentümerschaft geprüft) asynchron auf einem Threadpool-Thread, Ergebnis kommt über eine
Queue zurück und wird einmal pro Tick eingesammelt (sobald das Token gültig ist, gilt statt der 5-s-Join-Frist
`ServerConfig.JoinLookupTimeoutMs` = 30 s, und `Program.cs` wärmt EF Core/Npgsql beim Start mit einer Leerabfrage
vor - sonst lief der erste Login nach Serverstart in den Timeout; World bleibt ausschließlich dem Game-Loop-Thread
vorbehalten) -> nicht gefunden/falscher Account: CharacterNotFound; Charakter bereits online (eigenes
Dictionary CharacterId->EntityId in World): CharacterAlreadyOnline. Erfolgreicher Join: Appearance und
Startposition kommen aus der geladenen Character-Zeile (null-Position = Kartenmitte). Position wird alle 30s
(fix, keine Konfiguration) sowie sofort bei Disconnect über einen bounded Channel + Hintergrund-Task
gespeichert (server/src/Lumiis.Server/GameServer.cs).

Lumiis.Bot (server/src/Lumiis.Bot) macht seit M2b denselben Weg wie ein echter Client: pro Bot POST
/register (bei 409 stattdessen POST /login), GET /characters, bei Bedarf POST /characters - Passwort intern
fest als "botpass-<name>-2026" (nur Test-Tool). Option --auth-url (Default http://localhost:5080).

## Kartenkollision
Bewegung wird seit M3a serverseitig gegen eine pro Karte hinterlegte Blockierkarte geprüft (nicht mehr nur an
die Kartengrenzen geklemmt). Seit dem Multi-Map-Umbau (2026-10-25, Protocol.Version 19) gibt es mehrere
Karten; `Lumiis.Server/Program.cs` lädt sie über `MapCatalog` aus `data/maps/index.json` (nicht mehr
hartkodiert nur `village`). Jede `World` hält ihre eigene `CollisionMap`.

Pipeline-Ablauf (1 Pixel im Quellbild = 1 Weltenheit = 1 Zelle):
1. `data/maps/village/collision-source.png` bemalen - rote Flächen (R > 200, G/B < 100, siehe
   `tools/bake-collision.py`) werden blockiert, alles andere begehbar.
2. `python3 tools/bake-collision.py data/maps/village/collision-source.png` ausführen - schreibt
   `collision.bin` (gepackte 1-Bit-pro-Zelle-Bitmaske, Little-Endian, Header mit Magic "LCM1" + Breite/Höhe;
   siehe `server/src/Lumiis.Data/Maps/CollisionMap.cs` für den Loader) ins selbe Verzeichnis. Gibt eine
   Statistik aus (blockierte Zellen absolut/Prozent).
3. Beide Dateien committen (Quellbild UND collision.bin - der Server backt nicht selbst, `collision.bin` muss
   im Repo aktuell sein).

`server/src/Lumiis.Data/Maps/CollisionMap.cs` lädt/validiert `collision.bin` (Header-Prüfung: Magic-Bytes,
Maße gegen `GameConstants.MapWidth/Height / 100` - Abbruch mit klarer Meldung bei Mismatch/kaputter Datei,
kein Absturz). `World.SetDestination` klemmt ein Bewegungsziel wie bisher auf die Kartengrenzen und schreitet
danach die gerade Linie zum geklemmten Ziel zellenweise ab (Bresenham), stoppt an der letzten begehbaren
Zelle vor einer blockierten - `CollisionMap` wird injiziert, `World` lädt selbst keine Dateien.

Für die zweite Karte `greenfield` erzeugt `tools/generate-greenfield-map.py` Quellbild und
`collision.bin` deterministisch (Rand, See, Felsgruppen; Start/Portale/Spawns bleiben frei).

**`data/maps/village/collision-source.png` ist aktuell ein Platzhalter** (einfacher Rand + ein Rechteck +
eine Ellipse, per Pillow-Skript erzeugt, keine echte Kartengrafik) - der User ersetzt es später durch eine
echte, handbemalte Version, sobald Kartengrafik existiert. Danach `tools/bake-collision.py` erneut ausführen
und beide Dateien neu committen.

## Multi-Map und Portale (Protocol.Version 19)
Seit 2026-10-25 gibt es echte, begehbare Karten statt einer einzigen Welt. Details/Plan:
`docs/map-import-plan.md`.

- Daten: `data/maps/index.json` (`startMapId=map1` + Kartenliste), je Kartenordner `collision.bin`,
  optional `spawns.json`, `npcs.json`, `portals.json`, `map.json` (Name/Startposition/Visualdaten).
  **Map1** ist das aus dem Lumiis-Referenzlayout `yongan` geschnittene Startareal (128×128 Tiles,
  Dorfplatz + Feldrand, siehe unten); `village` bleibt als Legacy-/Testkarte erhalten, `greenfield`
  ist die generierte zweite Karte. Geladen/validiert von `MapCatalog` (`Lumiis.Data.Maps`) fail-fast
  (doppelte IDs, blockierter Start, unbekanntes Portalziel, blockierte Ankunft, Ankunft im Zielportal,
  invertierte/außerhalb liegende Rechtecke). Karten haben weiterhin global
  `GameConstants.MapWidth/Height` (1000×1000 Zellen).
- Server: `GameServer` hält `Dictionary<string, World>` (eine World je Karte) und einen gemeinsamen
  EntityId-Zähler; `ConnectionState.World` bestimmt AOI/Sichtbarkeit. Bewegung/Kollision, Monster-KI,
  Spawns/Respawns und NPCs laufen pro Karte; `SendToVisible`/`UpdateVisibility` liefern nur an
  Verbindungen derselben Karte. Chat-Kanal „Karte“ ist kartenlokal, Global/Whisper kartenübergreifend.
- Portale: `World.CheckPortal` prüft das Wire-Rechteck pro Tick (1 s Sperre, tote Spieler lösen nicht
  aus); `GameServer.TransitionEntityToMap` entfernt die Entity aus der alten World, schickt Despawn an
  alte Beobachter, setzt sie in die neue World, leert `VisibleEntityIds`, sendet `MapChangedPacket` und
  persistiert Position+MapId sofort. Neue Entities der Zielkarte kommen über den normalen AOI-Diff.
- Persistenz: `Character.MapId` (DB, Default `map1`, Migrationen `AddCharacterMapId` + `Map1Default`);
  bestehende `village`-Charaktere werden per Migration auf `map1` gehoben und ihre alte Dorfposition
  zurückgesetzt;
  `PositionSaveRequest`/`CharacterSaveRequest` tragen die MapId. Unbekannte gespeicherte MapIds fallen
  beim Join auf die Startkarte zurück (Warnung, kein Join-Abbruch). Respawn/„im Tod ausgeloggt“ nutzt
  die Startposition der Karte; eine beim Join blockierte gespeicherte Position fällt auf den Kartenstart
  zurück.
- Pakete: `JoinAccepted` trägt zusätzlich `MapId`/`MapName`/`MapWidth`/`MapHeight`; neu
  `MapChangedPacket` (1022, Server→Client, EntityId/Position/Karte/Maße) und
  `DebugChangeMapRequestPacket` (17, Client→Server, nur mit `--allow-debug-commands`).
- Client: `ClientWorld` hält MapId/Name/Maße; `HandleMapChanged` leert alle Entities, behält
  Fortschritt/Buffs und legt die eigene Figur an der Zielposition neu an. `GameClient.RebuildMapVisuals`
  baut Boden/Portalrechtecke neu; Kartenname/Portale/Visualdaten kommen aus `MapCatalogClient`
  (StreamingAssets/`lumiis-data/maps`, vom Shared-Build mitkopiert, rein für die Anzeige). Für `map1`
  wird `Resources/Maps/map1.png` (Sprite, 32 px/Tile, 128×128 Weltenheiten) an `visualOriginX/Y` gesetzt;
  ohne Kartenbild bleibt ein ruhiger Grasboden als Fallback. HUD zeigt Kartenname + Minimap, das
  Debug-Panel hat „Karte wechseln“ (Dev-Tool). Eine clientseitige Kollisionsprüfung gibt es weiterhin
  nicht.
- Bewusste Limitations: keine Referenzkarten importiert (kein Terrain/`tiles.bin`), keine kleineren
  Kartenmaße pro Map, kein Client-Kollisionsrendering, Gruppe bleibt kartenübergreifend, testmap/grove/
  sohan fehlen (Portale dorthin würden den Start abbrechen).

## Map1, Steuerung und Spiel-UI (Release-Optik)

- `data/maps/map1/`: Startareal aus `yongan` (Lumiis-2D-Referenzkarte, Crop 128×128). Die Kacheln
  (`tiles.json`) werden mit `tools/import-map1.py` zu `Resources/Maps/map1.png` (4096×4096, 32 px/Tile)
  und einer 1000×1000-`collision.bin` gebacken. Dorfplatz, Häuser, Wege, Wasser und Baumrand stammen aus
  dem Referenzlayout; der Start liegt außerhalb des Dorfes auf einer offenen Wiese.
- Spawns (seit 2026-10-25 Originaldaten): `tools/import-map1-spawns.py` setzt aus `tools/map1-yongan-spawns.json`
  (lumiis-2d@4376dae, nur der Map1-Ausschnitt) alle 47 Originalgruppen außerhalb der Dorf-Sicherheitszone:
  Wilder Hund (Mob 20, Lv 1), Wolf (21, Lv 3), Alphawolf (22, Lv 5, aggressiv), Wildschwein (23, Lv 7) und
  5 feste Lumiis des Kummers (24, Lv 5, wehrt sich nicht, Wellen aus Wölfen/Hund wie im Original). Level/HP/
  Angriff/Verteidigung/EXP/Angriffstempo/aggressiv 1:1 aus dem Original, Laufgeschwindigkeit (×140 Wire),
  Reichweiten, Yang und Beute an unser System angepasst. Kein Boss (gibt es im Original auf Yongan nicht).
  Start und Respawn liegen jetzt auf dem Dorfplatz. Dorfhändler und **Schmied** sitzen im Dorfplatz.
  Sheets: `dog`/`wolf`/`metin_sorrow` aus dem Lumiis-Asset-Archiv (`MonsterArt`, Steine als Einzelbild).
- Drops: Produktionsserver nutzt echte **Boden-Drops** (`EntityKind.GroundItem`): Monster lassen Items mit
  Icon/Name auf dem Boden fallen, Aufheben per Klick (`PickupItemRequestPacket` 20) prüft Distanz
  (`GameConstants.PickupRangeWire`), Besitz und Inventarplatz. Besitz: 10 s nur Killer oder dessen Gruppe
  (`GroundItemOwnershipMs`, sonst `PickupNotOwner`), nach 5 min verschwindet der Drop
  (`GroundItemLifetimeMs`, `World.RemoveExpiredGroundItems` + EntityDespawn). Mehrere Drops eines Kills
  liegen leicht versetzt. Boden-Drops und NPCs sind nie angreifbar (`World.IsCombatKind`, auch nicht per
  Kreisschlag). Tests ohne `useGroundDrops` behalten das bisherige Auto-Loot-Verhalten.
- NPC-Dienste: `data/npcs.json` hat `service` (`Merchant`/`Blacksmith`), wird als `NpcService` in
  `EntitySpawnPacket` übertragen. Aufwertung verlangt auf dem Produktionsserver einen Schmied in
  `NpcInteractRangeWire`; ohne ihn kommt `NoBlacksmith`.

- Bewegung: WASD oder Pfeiltasten senden alle 100 ms ein neues MoveTo-Ziel; beim Loslassen geht sofort
  `StopMovePacket` (19) an den Server, der die Bewegung mit `EntityStop` beendet. Linksklick auf den
  Boden bleibt als Komfort erhalten.
- Blickrichtung: folgt dem Mauszeiger (nicht der Laufrichtung) - weglaufen und nach hinten schlagen
  funktioniert. `MeleeArcView` zeigt bei jedem Schlag einen sichtbaren Bogen in Zielrichtung und
  Reichweite; ein roter Ring markiert das getroffene Ziel.
- Kampf: Leertaste schlägt, gedrückt halten wiederholt den Schlag im Server-Cooldown-Takt. Der lokale
  Kombo-Zähler wechselt links/rechts/links und neigt die Figur sichtbar; Auto-Target wählt das nächste
  Monster in Reichweite. Rechtsklick auf ein Monster bleibt als manueller Angriff erhalten. Leertaste
  löst auch ohne Ziel Animation/Sound/Kombo aus.
- Charakterskin: `LumiisSpriteProvider` lädt die echten 48×48-Sheets aus `Resources/Characters` für
  Body/Head/Hair/Armor (neue `CharacterSlot.Head`-Ebene); Lauf-/Angriffs frames werden über
  `CharacterView.SetAnimationFrame` angesteuert. Helm/Schild fallen bis zum Import eigener Sheets auf
  den Platzhalter zurück.
- Login: siehe Abschnitt "Spiel-UI (klassischer MMO-Stil)".
- Monster: `alpha_wolf`/`boar`/`dummy` sind als 40×32-Sheets (5 Frames, unten/links/rechts/oben) aus dem
  Lumiis-Referenzprojekt importiert; `MonsterView` animiert Laufrichtung und zeigt bei Schaden einen
  kleinen HP-Balken. Hungrige Wildhunde nutzen dasselbe Wolf-Sheet mit rötlicher Tönung.
- Gruppen-Technik: zusätzlich zu den Chat-Befehlen gibt es `PartyInviteRequest` (21), `PartyRespondRequest`
  (22), `PartyLeaveRequest` (23) und `PartySnapshotRequest` (24); der Server schickt
  `PartyInviteReceived` (1025) an den Eingeladenen. Die UI kann damit ohne Chat arbeiten.
- HUD: siehe Abschnitt "Spiel-UI (klassischer MMO-Stil)". `F1` bleibt das optionale Debug-Overlay; der
  Reichweitenring im regulären Spiel wurde entfernt. Leertaste schwingt auch ohne Monster (lokale
  Animation/Sound/Kombo), der Server bekommt nur bei einem echten Ziel ein `AttackRequest`.

## Spiel-UI (klassischer MMO-Stil)
Seit 2026-10-25 sind Login, Charakterauswahl, Systemmenü und HUD im Stil klassischer MMO-Oberflächen um
2010 neu gebaut (rein clientseitig, keine Protokolländerung):
- Skin: Pixelart-Rahmen/Knöpfe/Slots/Kugeln/Minimap-Ring unter `Resources/UI/Skin/`, reproduzierbar mit
  `python3 tools/generate-ui-skin.py` (deterministisch, eigene Formen, .meta mit Point-Filter). Alle
  Stile in `Resources/UI/lumiis.uss` (Bausteine `.m2-window`, `.m2-titlebar`, `.m2-button`, `.m2-input`,
  `.m2-inset`); Buttons werden global über `.unity-button` gestaltet.
- Skalierung: `UiSkin.CreatePanelSettings` - alle UIDocuments (Login sortingOrder 100, HUD 0) nutzen
  "Scale With Screen Size" mit Referenz 1280x720. `UiSkin.StretchToFill` spannt jeden `CloneTree()`-
  Container auf die volle Fläche - ohne das war der TemplateContainer 0 px hoch (alle Kinder absolut
  positioniert) und der Login-Bildschirm rutschte halb aus dem Bild (Ursache des "verschobenen" Logins).
- Login: Logo, zentriertes Anmeldefenster (Kennung/Passwort, Enter meldet an), Verbindungs-Foldout,
  "Beenden" unten rechts. Charakterauswahl: Auswahlkarten (Klick wählt, Doppelklick oder "Spiel
  starten" verbindet), Pixelart-Vorschau aus den Charakter-Sheets (`UiSkin.BuildCharacterPortrait`).
- HUD (`Hud.uxml`/`HudController`): Taskleiste unten mit HP-/SP-Kugeln, Schnellleiste (1-4 Skills, 5 =
  erster Heiltrank im Inventar), vier EXP-Kugeln und Menüknöpfen (Charakter/Inventar/System); runde
  Minimap (Ausschnitt um die Figur, Koordinaten) oben rechts, Ziel oben mittig, Chat unten links.
- Fenster (ziehbar an der Titelleiste, Escape schließt das oberste vor dem Systemmenü): Inventar (I,
  Ausrüstung um eine Silhouette, 20 Plätze, Tooltips, Rechtsklick legt ab; Schleifstein anklicken und dann
  ein Ausrüstungsteil = Aufwerten, optional mit Bewahrungsrolle), Charakter (C, Werte, Status- und
  Fertigkeitspunkte verteilen), Shop (öffnet sich per Rechtsklick auf einen Händler; Klick kauft,
  Rechtsklick im Inventar verkauft). Damit sind Inventar/Shop/Punkteverteilung auch in Release-Builds
  bedienbar, nicht mehr nur im Debug-Panel.
- Chat wieder aktiv: Enter öffnet die Eingabezeile, Enter sendet und schließt, Escape schließt. Das Feld
  ist nur während der Eingabe fokussierbar, WASD/Leertaste landen also nie versehentlich darin; beim
  Öffnen wird eine laufende Tastaturbewegung gestoppt.
- Lesbarkeit: kleinste Schrift 11 px, Schatten statt 1-px-Kontur. `UiSkin.ComputeScale` + `UiScaler`
  (an jedem UIDocument): Faktor wie "Scale With Screen Size" zu 1280x720, aber nie unter 1 - kleine Fenster
  schrumpfen die Schrift nicht; nur unter 1000x600 wird bis zur Passform verkleinert.
- Schmied (`NpcService.Blacksmith`, Rechtsklick): eigenes Fenster statt Shop - Ausrüstungsteil wählen,
  Stufe/Bonus/Erfolgschance (`UpgradeCatalogClient`, `upgrade.json` wird dafür nach StreamingAssets
  kopiert, nur Anzeige), Materialzähler, Bewahrungsrolle, Warnung bei Zerstörungsrisiko, Ergebniszeile,
  darunter das Sortiment. `NoBlacksmith` hat einen deutschen Text.
- Gruppe ohne Chat: Rechtsklick auf einen Spieler öffnet ein Kontextmenü (In Gruppe einladen/Flüstern,
  `GameClient.PlayerContextRequested`), `PartyInviteReceived` zeigt ein Annehmen/Ablehnen-Popup, das
  Gruppenpanel zeigt HP-Balken je Mitglied, "Verlassen" nutzt `PartyLeave`, beim Betreten des Spiels
  `RequestPartySnapshot`.
- Gruppe (Protocol 20): Offline-Mitglieder ausgegraut "(offline)", Rollenname je Zeile; der Leiter öffnet
  per Rechtsklick auf eine Zeile Entfernen/Zum Leiter machen/Rolle (PartyKick/-Promote/-SetRoleRequest).
- Charakterauswahl-Vorschau aus dem echten `CharacterInfo.Appearance` (`UiSkin.BuildCharacterPortrait`, Sheets
  über `LumiisSpriteProvider.SheetResourceFor`); beim Erstellen Frisur/Haarfarbe (Körper nur bei >1 Eintrag)
  wählbar, geht als `CharacterLookChoice` an POST /characters.
- Boden-Items: Namensschild mit "+N" und Farbe je Stufe (blau ab +1, gold ab +7, orange +9), Hervorhebung
  unter dem Mauszeiger, Linksklick hebt auf.
- Screenshot-Hilfen: `GameClient.DebugShowSmithPreview()` / `DebugShowPartyInvite(name)`.
- Spielgefühl statt Debug (`HudWorldOverlay`, UI Toolkit im HUD-Dokument): Namensschilder über Spielern
  (eigener gelb), NPCs (grün) und Monstern (rötlich, Ziel hervorgehoben) auch ohne F1 - das IMGUI-Namens-
  Overlay ist entfernt. Schwebende Schadenszahlen (gelb = eigener Treffer, rot = erlittener Schaden,
  "Verfehlt"/"Ausgewichen") aus `GameClient.DamageShown`/`DodgeShown`. Todesbildschirm mit Countdown
  (`RespawnRemainingSeconds`, fix 3 s wie der Server); im Tod sperrt der Client Laufen, Angriff, Skills,
  Heiltrank und Aufheben (rein kosmetisch, der Server muss trotzdem selbst ablehnen). Screenshot-Hilfen
  `DebugShowCombatPreview()`/`DebugSimulateDeath()`.
- Klick-Abschirmung: Elemente mit Klasse `ui-block` fangen Mausklicks ab
  (`HudController.IsPointerOverUi` -> `GameClient.UiPointerHitTest`), ein Klick auf HUD/Fenster bewegt
  oder greift also nicht an.

## Sichtbarkeitsbereich (AOI)
Seit M3b bekommt eine Verbindung nur noch Spawns, Bewegungen und Despawns von Entities innerhalb von
`GameConstants.AoiRadiusWire` (1500 Wire-Einheiten = 15 Weltenheiten, spätere Justierung erwartet) um die
eigene Entity - nicht mehr die ganze Karte. Protokoll unverändert (EntitySpawn/EntityMove/EntityStop/
EntityDespawn-Format bleibt gleich, nur wer welches Paket bekommt).

`World.ComputeVisibility(radiusWire)` ist reine Distanzberechnung (quadrierte Distanz, `long`-Arithmetik
gegen Overflow, kein Netzwerkbezug, ohne Unity/LiteNetLib testbar) - für jedes Entity-Paar einmal geprüft
(Sichtbarkeit ist symmetrisch), also O(n²/2) pro Aufruf, einmal pro Tick in `GameServer.UpdateVisibility()`.
Bewusst kein Grid/Quadtree: bei unserer Spielergrößenordnung (`ServerConfig.MaxPlayers`, aktuell 100) ist
O(n²) pro 40-ms-Tick (25 Hz) vernachlässigbar - eine räumliche Indexstruktur wäre verfrühte Optimierung ohne
gemessenes Problem. Wird das je zum Engpass (deutlich mehr Spieler oder M3c-Monster mit demselben
Mechanismus), ist ein Grid die naheliegende nächste Stufe.

Jede Verbindung merkt sich ihren letzten bekannten Sichtbarkeits-Stand (`ConnectionState.VisibleEntityIds`).
Pro Tick wird das gegen den frischen `ComputeVisibility`-Aufruf gedifft: neu sichtbar → EntitySpawn (plus
EntityMove ab der aktuellen Position, falls die Entity gerade in Bewegung ist); nicht mehr sichtbar →
EntityDespawn. Die bestehenden Move/Stop-Broadcasts (`GameServer.SendToVisible`) gehen nur noch an
Verbindungen, die die jeweilige Entity laut diesem Stand sehen - plus immer an die Entity selbst (der Client
rendert seine eigene Figur genauso über EntityMove/EntityStop wie jede andere, keine Client-Prediction).
Der Grenzfall "Distanz exakt gleich dem Radius" zählt als sichtbar (inklusiv).

## Monster
Seit M3c existieren Monster als Entities auf der Karte - dieselbe `Lumiis.Server.Game.Entity`-Klasse wie
Spieler (Feld `Kind`, siehe unten), keine Unterklasse: Bewegungslogik (`MoveTo`/`Tick`) ist zwischen beiden
100 % identisch, nur die Wander-KI-Entscheidung unterscheidet sich per einfachem Kind-Check. Kampf/Sterben
seit M4a, automatischer Respawn an SpawnPoints seit M4b - siehe Abschnitt "Kampf".

Datenformat:
- `data/mobs.json`: Mob-Typen (Id, Name, SpeedWire, WanderRadiusWire, WanderIntervalMinMs/MaxMs). Aktuell vier Typen: zwei Platzhalter ("Wildhund", "Waldschrat") sowie seit dem Lumiis-MVP
  "Lumiis des Kummers" (quasi stationärer Stein, hohe HP/Defense, garantiert ein Material) und "Waldboss"
  (aggressiver Boss mit 1200 HP). Echte Wellen-Phasen (75/50/25 %) fehlen noch. Geladen/validiert
  über `MobCatalog` (`Lumiis.Data.Mobs`, analog zu `LookCatalog`).
- `data/maps/village/spawns.json`: Spawn-Punkte (Id, MobId, PositionX/Y wire, RespawnSeconds). `RespawnSeconds`
  wird seit M4b ausgewertet (siehe Abschnitt "Kampf") - stirbt das dort verankerte Monster, respawnt nach
  dieser Zeit eine neue Entity an genau dieser Position. Geladen/validiert über `SpawnCatalog` - jede MobId
  muss in `MobCatalog` existieren, jede Position muss laut `CollisionMap` begehbar sein, beides Startabbruch
  mit Angabe des betroffenen Spawn-Punkts bei Verstoß. Monster werden einmalig beim Serverstart angelegt
  (`GameServer`-Konstruktor), EntityId aus demselben Zähler wie Spieler (`World._nextEntityId`).

Wander-KI-Prinzip (`World.RunWanderAi`, Teil von `World.Tick`, läuft nach der Spieler-Bewegung): pro fälligem
Monster (`Entity.NextWanderAtMs`, injizierbare Uhr) ein zufälliger Punkt innerhalb `WanderRadiusWire` um den
Anker (Spawn-Position, Quadrat statt Kreis - dieselbe einfache Herangehensweise wie `BotTargetPicker` für
Spieler-Bots), auf Kartengrenzen geklemmt. Ist die Zielzelle laut `CollisionMap` blockiert, wird dieser Zyklus
übersprungen (kein Retry-Loop) - die nächste Fälligkeit wird trotzdem neu bestimmt. Die eigentliche Bewegung
läuft über denselben `World.SetDestination`-Mechanismus wie Spieler-MoveTo (inklusive Kollisions-Klemmung
entlang der Linie aus M3a), keine eigene Bewegungslogik. `Entity.MovementSpeed` (bei Spielern
`GameConstants.PlayerSpeed`, bei Monstern `SpeedWire` aus dem Mob-Typ) sorgt dafür, dass beide Kinds trotz
geteiltem Mechanismus ihre eigene Geschwindigkeit behalten.

AOI/Netzwerk: Monster nutzen denselben Sichtbarkeitsmechanismus wie Spieler (siehe oben) ohne jede
Sonderbehandlung - `ComputeVisibility`/`SendToVisible` sind rein positions-/id-basiert. `EntitySpawnPacket`
trägt seit M3c `Kind` (`EntityKind.Player`/`Monster`) und `MobId` (bei Spielern ungenutzt/0); `Appearance`
bleibt im Paket für beide Kinds (bei Monstern mit Default-Werten befüllt, keine Bedeutung).

Client: Monster bekommen eine eigene, sehr einfache `MonsterView` statt `CharacterView` (kein Paper-Doll, keine
Blickrichtung, keine Animation) - ein `SpriteRenderer` mit einer je MobId unterschiedlichen Platzhalterform
(Kreis für Wildhund, Dreieck für Waldschrat, unterschiedliche Farbe je MobId; reuse der
`PlaceholderSprites`-Infrastruktur aus M1c). Der Client kennt `mobs.json` bewusst nicht - der Mob-Name kommt
schon aufgelöst vom Server (`Entity.Name`), Form/Farbe werden rein anhand der MobId zugeordnet.

## Kampf
Seit M4a kann ein Spieler ein sichtbares Monster angreifen (Rechtsklick), seit M4c greifen Monster
umgekehrt auch Spieler an (Aggro/Gegenwehr, siehe unten). Schaden = `max(1, Angreifer.Attack - Ziel.Defense)`,
serverautoritativ in `World.TryAttack` (kein Netzwerkbezug, wie AOI/Wander-KI ohne LiteNetLib/Sockets
testbar) - seit M4c für beide Angriffsrichtungen verallgemeinert, siehe unten. Prüfreihenfolge: Ziel
existiert -> Ziel lebt -> Angreifer und Ziel sind unterschiedlichen Kinds (blockt Spieler-vs-Spieler PvP UND
Monster-vs-Monster) -> Cooldown des Angreifers -> Reichweite (quadrierte Distanz wie `ComputeVisibility`,
gegen `MeleeRangeWire` des Angreifers - immer kleiner als `AoiRadiusWire`, der Angreifer ist deshalb
automatisch unter den AOI-Beobachtern des Ziels, kein Sonderfall in `GameServer.SendToVisible`).

Datenformat: `data/classes.json` (Spielerklassen - aktuell nur "Krieger": MaxHp/Attack/Defense/
MeleeRangeWire/AttackCooldownMs, geladen/validiert über `ClassCatalog`, `Lumiis.Data.Classes`, analog zu
MobCatalog/LookCatalog) und `data/mobs.json` (um MaxHp/Attack/Defense/YangMin/YangMax pro Mob-Typ erweitert,
seit M4c zusätzlich MeleeRangeWire/AttackCooldownMs analog zu classes.json sowie Aggressive/AggroRangeWire/
LeashRangeWire, siehe unten). Leben/Angriff/Verteidigung sind reine Server-Konstanten aus diesen Dateien,
keine DB-Spalten - `Character` bekommt in M4a nur `Yang` (long, Default 0) dazu. Alpha-Umfang kennt nur eine
Klasse, die Klasse wird deshalb über einen festen Namen aufgelöst (`GameServer.DefaultClassName`) statt über
eine eigene `ClassId`-Spalte - sobald es mehrere Klassen gibt, braucht `Character` eine echte Spalte dafür.

Monster-Aggro/Verfolgung/Leine (M4c): jedes Monster hat höchstens ein aktives Ziel (`Entity.TargetEntityId`,
`int?`, `null` = kein Ziel - dasselbe Nullable-Feld-Muster wie `SpawnPointId`/`CharacterId`, kein paralleler
Zustandscontainer). Aggressive Mobs (`MobDefinition.Aggressive`, z. B. Wildhund) suchen pro Tick ohne Ziel den
nächstgelegenen lebenden Spieler innerhalb `AggroRangeWire` (`World.FindNearestLivingPlayerWithinRange`,
quadrierte Distanz wie AOI/TryAttack) und erfassen ihn automatisch. Passive Mobs (z. B. Waldschrat,
`AggroRangeWire` unbenutzt) greifen nie von sich aus an, erfassen aber reaktiv den Angreifer als Ziel, sobald
sie getroffen werden (`World.TryAttack`, gilt unabhängig davon, ob der Mob aggressiv ist). Solange ein Ziel
aktiv ist, pausiert die normale Wander-Zufallsbewegung (`World.RunMonsterAi`, ersetzt die reine
Wander-Prüfung aus M3c): außerhalb `MeleeRangeWire` läuft das Monster **jeden Tick neu** auf die aktuelle
Position des Zielspielers zu (`SetDestination`, gleiche Kollisions-Klemmung wie überall - das erzeugt
bewusst ein `EntityMovePacket` pro Tick während der Verfolgung, ein bekannter Bandbreiten-Kompromiss fürs
MVP, siehe "Bekannte Lücken"); innerhalb `MeleeRangeWire` greift es an, sobald sein Cooldown bereit ist
(derselbe `TryAttack`-Mechanismus wie beim Spieler-Angriff). Entfernt sich der Zielspieler weiter als
`LeashRangeWire` vom `WanderAnchor` des Monsters (nicht vom Spieler - Distanz zählt vom AKTUELLEN
Monster-Standort), gibt das Monster auf, bewegt sich einmalig zurück zum Anker und fällt danach in den
normalen Wander-Zustand zurück; verschwindet das Ziel (Tod oder Verbindungsabbruch), passiert dasselbe ohne
Rückweg-Bewegung. Ein Wander-Anker existiert für JEDES Monster (auch Dev-Tools-Debug-Spawns - `WanderAnchorX/Y`
wird immer aus der Spawn-Position gesetzt, unabhängig von `SpawnPointId`), die Leine funktioniert also auch
für sie. Netzwerkseitig kein Sonderfall: `GameServer.BroadcastAttackResult` verschickt für einen
monster-initiierten Treffer dieselben Pakete (EntityDamaged/EntityDied/Despawn-bei-Monstertod) wie für einen
spieler-initiierten - nur ohne Yang-Handling (das braucht die Verbindung des Angreifers, die es bei einem
Monster nicht gibt, und Yang wird ohnehin nur beim Tod eines MONSTERS ausgezahlt).

Monstertod: sofortiges Entfernen aus `World` (wie ein normaler Despawn) und eine zufällige Yang-Auszahlung
(zwischen `YangMin`/`YangMax`, injizierbare Zufallsquelle) an den tötenden Spieler. Hatte das Monster eine
`SpawnPointId` (regulärer Katalog-Spawn aus spawns.json, siehe Abschnitt "Monster" - NICHT bei per Dev-Tools
gespawnten Monstern, deren `SpawnPointId` immer `null` ist), terminiert `World.ApplyDamage` seit M4b
zusätzlich einen Respawn an genau diesem SpawnPoint: `World.Tick` erzeugt nach `SpawnPoint.RespawnSeconds`
Sekunden über den normalen `SpawnMonster`-Weg eine **neue** Entity mit **neuer** EntityId (nicht dieselbe
wiederbelebt - anders als beim Spieler, siehe unten, weil der Client für die alte längst `EntityDespawn`
bekommen hat), exakt an der SpawnPoint-Position, mit vollem Leben, wieder an denselben SpawnPoint gebunden
(der Respawn-Zyklus läuft also unbegrenzt weiter). Kein eigener Netzwerk-Sonderfall: die neue Entity wird vom
nächsten `UpdateVisibility()`-Diff wie jede andere neu erschienene Entity per `EntitySpawnPacket` gemeldet.
Debug-gespawnte Monster (`SpawnPointId == null`) respawnen dagegen nie. Spielertod: kein Despawn -
`RespawnAtMs` wird gesetzt (`nowMs + 3000`, fix), `CurrentHp` bleibt 0
bis `World.Tick` die Fälligkeit erkennt und den Spieler an die Kartenmitte mit vollem Leben zurücksetzt
(`Entity.Teleport`, kein neues Paket - der Client bekommt dafür ein `EntityStopPacket` an der neuen Position,
das bedeutet ohnehin "hart an dieser Position, keine Bewegung", exakt die Teleport-Semantik). Diese
Sterbe-/Respawn-Logik wurde in M4a bewusst generisch (nicht spieler-vs-monster-spezifisch) implementiert und
direkt getestet, obwohl PvP-Spielertod damals wegen des reinen Spieler-Angriffs-Checks unerreichbar war -
genau dafür vorbereitet: seit M4c ist Monster-vs-Spieler der erste echte, im normalen Spielbetrieb
erreichbare Weg dorthin, ohne dass an der Respawn-Logik selbst etwas geändert werden musste.

Yang wird asynchron gespeichert wie die Position in M2b (eigener bounded Channel + Hintergrund-Task,
`GameServer`), aber mit einem eigenen Auslöser: sofort bei jeder Änderung statt periodisch alle 30s, da
Yang-Änderungen seltene Einzelereignisse sind (Monster-Kills), nicht kontinuierlich wie Bewegung.

Pakete seit M4a (Protocol.Version seit Dev-Tools bei 6, siehe Abschnitt "Dev-Tools" - M4c selbst hat das
Protokoll nicht geändert, siehe Abschnitt-0b-Review): `AttackRequestPacket` (Client->Server, TargetEntityId),
`EntityDamagedPacket`/`EntityDiedPacket`/`YangChangedPacket` (Server->Client, YangChanged nur an den
Empfänger) sowie `AttackFailedPacket` (nur an den Angreifer, Reason-Enum TargetNotFound/TargetAlreadyDead/
OutOfRange/OnCooldown/TargetIsPlayer - Letzterer trotz des Namens weiterhin korrekt: er wird nur bei
Spieler-vs-Spieler-Versuchen ausgelöst, siehe oben). `AttackerEntityId`/`TargetEntityId` in
`EntityDamagedPacket`/`EntityDiedPacket` sind bewusst kind-neutrale `int`-EntityIds ohne eigene
Kind-Unterscheidung - funktionieren seit M4c unverändert für beide Angriffsrichtungen.

Bewusst NICHT in M4 (M4a+M4b+M4c zusammen, M4 damit komplett fertig): XP/Level, PvP (Spieler-vs-Spieler
bleibt abgelehnt), echte Health-Bars (Debug-Overlay-Text reicht, siehe `GameClient.DrawCombatLine` - eigenes
Leben, anvisiertes Ziel, Yang-Betrag, deutsche Kurzmeldungen), Rate-Limit gegen Angriffsspam (gleiche
bestehende Lücke wie bei MoveTo, siehe "Bekannte Lücken") sowie Drosselung der Verfolgungs-EntityMove-Pakete
(M4c erzeugt bewusst jeden Tick eines pro aktiv verfolgendem Monster, siehe oben - für die Alpha-Spielergröße
("am Anfang wenige Spieler", siehe Projektbriefing) akzeptiert, spätere Optimierung möglich).

## Items und Inventar
Seit M5a sind Ausrüstungs-Slots (Rüstung/Helm/Waffe/Schild) echte Gegenstände mit Kampfwert-Boni statt reiner
Look-Referenzen, dazu ein 20-Felder-Inventar, Anlegen/Ablegen und ein Heiltrank als Verbrauchsgegenstand.
Monster droppen Items beim Tod direkt ins Inventar (kein Boden-Loot). KEIN Shop (M5b), KEIN Stapeln, KEIN
Upgrade-System, KEINE echte Inventar-Grafik (Debug-Panel-Stil, siehe "Client").

Datenformat: `data/items.json` (Id, Name, ItemType-Enum Armor/Helmet/Weapon/Shield/Consumable - bei
Ausrüstungs-Items LookId [muss im passenden looks.json-Slot existieren]+AttackBonus+DefenseBonus, bei
Consumable HealAmount), geladen/validiert über `ItemCatalog` (`Lumiis.Data.Items`, analog zu MobCatalog/
ClassCatalog, braucht `LookCatalog` zur Validierung - Lumiis.Server lädt looks.json deshalb seit M5a
zusätzlich, obwohl es Appearance selbst nicht mehr direkt daraus auflöst, siehe unten). `data/mobs.json` um
eine `drops`-Liste je Mob-Typ erweitert (ItemId+ChancePercent 0-100, jeder Eintrag unabhängig gewürfelt,
mehrere Drops pro Tod möglich, keine Querverweis-Validierung gegen ItemCatalog beim Laden).

Slot-Modell: `Character.EquippedArmorItemId/HelmetItemId/WeaponItemId/ShieldItemId` (nullable int, Item-IDs
statt Look-IDs - vor M5a waren das LookIds direkt) plus eine `CharacterInventoryItem`-Tabelle (CharacterId,
SlotIndex 0-19, ItemId, Unique-Index auf CharacterId+SlotIndex). Zur Laufzeit hält `Entity` dasselbe als
`Inventory` (`int[GameConstants.InventorySize]`, 0=leer) und vier `EquippedXItemId`-Felder (0=leer, mutable) -
`World.TryEquip/TryUnequip/TryUseConsumable/TryAddItemToInventory` sind die einzigen Schreibpfade, alle
tauschen/verschieben und rufen danach `World.RecalculateCombatStats` auf (Attack/Defense = Entity.BaseAttack/
BaseDefense aus classes.json + Summe aller vier Ausrüstungs-Boni - `TryAttack` selbst liest unverändert nur
Attack/Defense, kein Unterschied zwischen Basis- und ausgerüstetem Wert dort). Neue Charaktere (Lumiis.Auth)
bekommen kein automatisches Rüstung/Helm/Schild mehr, nur eine feste Startwaffe (Item-Id 1, "Holzschwert" -
`AuthEndpoints.StartingWeaponItemId`), Inventar bleibt leer.

Netzwerk: `InventorySyncPacket` (Server->Client, nur an den Empfänger, voller Resync - belegte Slots als
[SlotIndex,ItemId]-Liste plus die vier Equipped-Ids, kein Delta) wird bei Join UND nach jeder
Inventaränderung (Equip/Unequip/UseConsumable/Drop) verschickt. `EquipItemRequestPacket`/
`UnequipItemRequestPacket`/`UseConsumableRequestPacket` (Client->Server) rufen die passende World-Methode
auf; Equip/Unequip lösen zusätzlich ein neues `AppearanceChangedPacket` (EntityId+Appearance) an alle
AOI-Beobachter aus (eigenes Paket statt EntityDespawn+EntitySpawn, um clientseitigen Animations-/
Blickrichtungszustand nicht zurückzusetzen). Fehlschläge: `InventoryActionFailedPacket` (Reason-Enum
InvalidSlot/EmptySlot/WrongItemType/NotConsumable/InventoryFull/TargetAtFullHp) nur an den Anfragenden -
InventoryFull wird zusätzlich als reine Info verschickt, wenn ein Monster-Drop mangels Platz verloren geht
(der Kill selbst bleibt gültig). `GameServer.ToAppearance` löst Armor/Helmet/Weapon/Shield seit M5a über
`ItemCatalog` aus den EquippedXItemId-Werten zu LookIds auf (Appearance-Wire-Format selbst unverändert -
ArmorId usw. sind weiterhin LookIds auf der Leitung, nur die serverseitige Quelle hat sich geändert).
Protocol.Version 8.

Persistenz: eigener `InventorySaveRequest`-Channel (wie Position/Yang, aber immer ein voller Snapshot statt
Delta - Consumer ersetzt alle `CharacterInventoryItem`-Zeilen plus die vier Equipped-Spalten in einer
Transaktion), ausgelöst sofort nach jeder Equip/Unequip/UseConsumable-Aktion und nach jedem erfolgreichen
Monster-Drop (analog zu Yang: seltene Einzelereignisse, kein periodischer Zyklus nötig).

Client: kein eigenes Inventar-UI, sondern eine Erweiterung des bestehenden Debug-Test-Panels (nur Editor/
Development-Build) - Bereich "Ausrüstung" (vier Slots + Ablegen-Knopf) und "Inventar" (20 Zeilen, Klick auf
einen belegten Slot legt Ausrüstung an oder benutzt ein Consumable, je nach ItemType). Neue
`ItemCatalogClient` (analog zu `LookCatalogClient`) lädt `items.json` aus StreamingAssets nur für
Anzeigenamen/ItemType - Lumiis.Shared.csproj kopiert items.json seit M5a im selben Build-Schritt wie
looks.json dorthin.

Bewusst NICHT in M5a: Shop (M5b), Boden-Loot (Drops gehen immer direkt ins Inventar, kein Fallback bei vollem
Inventar außer Verlust), Stapeln (jeder Inventarplatz trägt genau ein Item), Upgrade-/Sockelsystem, echte
Inventar-Grafik (Debug-Panel-Stil reicht, wie bei den Health-Bars in M4a).

## Shop und NPCs
Seit M5b gibt es NPCs (`EntityKind.Npc`, neben Player/Monster) als eigene, statische Entity-Art - aktuell nur
Händler. Ein Spieler öffnet per Rechtsklick den Shop eines NPCs, kauft Items aus dessen festem Sortiment und
verkauft eigene Inventar-Items für Yang. NPCs sind unangreifbar (`TryAttack` lehnt Kind=Npc-Ziele mit
`AttackFailedReason.TargetNotAttackable` ab - geprüft VOR dem Gleiche-Kinds-Check, der Player-vs-Npc sonst
nicht blocken würde) und statisch: dieselben `World.Tick`-Schleifen, die Monster wandern/kämpfen/respawnen
lassen und Positionen speichern, prüfen bereits explizit auf `== EntityKind.Monster`/`== EntityKind.Player`
(nie auf `!= EntityKind.Player` als Monster-Ersatz) bzw. `CharacterId.HasValue` - ein NPC fällt bei jeder
dieser Prüfungen automatisch durch, keine der Schleifen musste für M5b geändert werden.

Datenformat: `data/npcs.json` (NPC-Typen: Id, Name, Appearance [Look-IDs aus looks.json], ShopItemIds - jede
davon muss in `data/items.json` existieren UND einen `BuyPrice > 0` haben) und
`data/maps/village/npcs.json` (Platzierungen: Id, NpcId, PositionX/Y in Wire) - geladen/validiert über
`NpcCatalog`/`NpcSpawnCatalog` (`Lumiis.Data.Npcs`, analog zu MobCatalog/SpawnCatalog), Startabbruch mit
klarer Meldung bei Verstoß. `data/items.json` um `BuyPrice`/`SellPrice` (long, 0 = nicht kauf- bzw.
verkäuflich) pro Item erweitert. Aktuell ein Platzhalter-Händler "Dorfhaendler" südlich der Kartenmitte.

Entity-Modell: `Entity.CreateNpc`/`World.SpawnNpc` (analog zu CreateMonster/SpawnMonster) - keine Bewegung,
keine SpawnPointId, keine CharacterId, Kampfwerte auf den Default-Konstanten (nie gelesen, da NPCs
unangreifbar sind). `Entity.ShopItemIds` trägt das Sortiment (wie `Appearance` außerhalb des Konstruktors
gesetzt, bewusst NICHT über die Konstruktor-Kette wie `Drops` bei Monstern - nur bei Kind=Npc relevant).
`EntitySpawnPacket.MobId` wurde dafür zu `TypeId` umbenannt (ebenso `Entity.MobId`/`ClientEntity.MobId`,
reiner Wire-Rename ohne Formatänderung) - die Bedeutung hängt seither vom Kind ab: MobId bei Monster, NpcId
bei Npc, ungenutzt/0 bei Player.

Reichweite: `GameConstants.NpcInteractRangeWire` (300 Wire-Einheiten) - `World.CheckShopAccess` (gemeinsame
Vorprüfung für TryOpenShop/TryBuy/TrySell: NPC existiert und ist Kind=Npc, hat ein Sortiment, Distanz
≤ NpcInteractRangeWire per quadrierter Distanz wie AOI/TryAttack) wird von JEDER der drei Aktionen erneut
durchlaufen, nicht nur beim Öffnen - ein Spieler könnte sich zwischen zwei Requests wegbewegt haben.

Yang: `World.CreditYang` ist die einzige Stelle im Projekt, die `Entity.Yang` erhöht (Monster-Drops,
Verkauf, `DebugGiveYangRequestPacket`) - deckelt bei `GameConstants.MaxYang` (2 Mrd.), gibt bei
Überschreitung `false` zurück (Yang bleibt unverändert, kein Teilbetrag). `TrySell` bricht bei
Überschreitung mit `ShopActionFailedReason.YangCapReached` komplett ab (Inventarplatz bleibt belegt, kein
Teilverkauf); ein Monster-Kill, dessen Yang-Gutschrift am Deckel scheitert, bleibt trotzdem ein gültiger Kill
(gleiche Linie wie bei einem Drop, der mangels Inventarplatz verloren geht).

Netzwerk: `OpenShopRequestPacket`/`BuyItemRequestPacket`/`SellItemRequestPacket` (Client->Server).
Erfolgreiches Öffnen: `ShopContentPacket` (NpcEntityId + Liste aus (ItemId, BuyPrice), direkt aus
`Entity.ShopItemIds` + `ItemCatalog` aufgelöst - kein erneuter NpcCatalog-Zugriff zur Laufzeit nötig) nur an
den Anfragenden. Erfolgreicher Kauf/Verkauf: nutzen die bestehenden `InventorySyncPacket`/`YangChangedPacket`
(kein neues Erfolgspaket). Fehlschlag: `ShopActionFailedPacket` (Reason-Enum NpcNotFound/NotAShop/
OutOfRange/ItemNotInStock/NotEnoughYang/InventoryFull/InvalidSlot/EmptySlot/ItemNotSellable/
YangCapReached) nur an den Anfragenden. Protocol.Version 10 (9: dieses HP-Bugfix-EntityHpChangedPacket,
10: M5b).

Persistenz: Kauf/Verkauf ändern Yang UND Inventar/Ausrüstung zusammen - eigener, dritter
`CharacterSaveRequest`-Channel (wie Position/Yang/Inventar, aber Yang PLUS voller Inventar-Snapshot in einem
Request) schreibt beides in einem einzigen `SaveChangesAsync`-Aufruf (EF Core fasst das automatisch in einer
Transaktion zusammen), damit ein Absturz zwischen zwei separaten Writes nie einen inkonsistenten
Zwischenstand (Yang neu/Inventar alt oder umgekehrt) hinterlassen kann. Die bestehenden Yang-only-/
Inventory-only-Channels (Monster-Kill-Yang bzw. Equip/Unequip/Consumable/Drop) bleiben unverändert, da dort
jeweils nur eine Seite betroffen ist.

Client: kein eigenes Shop-UI, sondern ein weiterer Bereich im bestehenden Debug-Test-Panel (nur Editor/
Development-Build, wie Inventar/Ausrüstung aus M5a) - Titel mit NPC-Name, Sortiment mit Kaufen-Knopf,
eigene Inventar-Slots mit Verkaufspreis (nur `SellPrice > 0`, lokal aus `ItemCatalogClient`/items.json - kein
Paket trägt Verkaufspreise), Yang-Stand, Schließen-Knopf (plus automatisches Schließen bei zu großer
Distanz - reine Komfortfunktion, der Server prüft ohnehin jede Aktion erneut). NPCs benutzen dieselbe
`CharacterView` wie Spieler (Rechtsklick-Bounds-Check über alle Ebenen-Renderer kombiniert, da Paper Doll
anders als `MonsterView` keinen einzelnen Renderer hat), Name in Grün statt Weiß, "NPC" in der
Debug-Entity-Liste. Neuer Debug-Panel-Knopf "+1000 Yang" sendet `DebugGiveYangRequestPacket` (nur mit
`--allow-debug-commands`, siehe Abschnitt "Dev-Tools").

Bewusst NICHT in M5b: Rückkauf, Mengenkauf, Dialogsystem, Quests, echte Shop-Grafik (Debug-Panel-Stil reicht,
wie bei Inventar/Ausrüstung), weitere NPC-Typen (nur Händler).

## Fortschritt: Level, XP, Werte
Seit M6a hat ein Charakter ein Level (1-25), sammelt Erfahrung (XP) durch Monster-Kills, verteilt beim
Levelaufstieg Statuspunkte auf vier Werte (ST/HT/DX/IQ) und Fertigkeitspunkte (nur gesammelt, noch nicht
ausgegeben - Skills folgen erst in M6b), hat SP als zweite Ressource neben HP, kann Angriffen ausweichen und
regeneriert HP/SP natürlich außerhalb des Kampfs. Beschluss "Weg 2": das Leben überdauert das Ausloggen (kein
Volllade-Heilen mehr beim Login wie bis M5).

Formeln (alle Faktoren als Zahlen in `data/classes.json`, ein Eintrag pro Klasse - Alpha kennt nur "Krieger"):
- `MaxHp = BaseHp + Ht*HpPerHt + (Level-1)*HpPerLevel`
- `MaxSp = BaseSp + Iq*SpPerIq + (Level-1)*SpPerLevel`
- `Attack = BaseAttack + St*AttackPerSt + Waffen-Bonus` (Waffen-Bonus wie bisher aus M5a, `ItemDefinition.AttackBonus`)
- `Defense = BaseDefense + (Ht / HtPerDefense, ganzzahlig abgerundet) + Rüstungs-Boni` (M5a)
- `Ausweichen (DodgeBp, 100 = 1 Prozent) = min(DodgeCapBp, Dx*DodgeBpPerDx)`
- St/Ht/Dx/Iq sind jeweils `BaseXx` (Klassen-Startwert, `classes.json`) + `SpentXx` (verteilte Punkte, DB-Spalte).

Aktuelle Platzhalterwerte (Krieger, `data/classes.json`): BaseHp 68, BaseSp 12, BaseAttack 3, BaseDefense 3,
BaseSt 6, BaseHt 4, BaseDx 3, BaseIq 3, HpPerHt 8, HpPerLevel 10, SpPerIq 6, SpPerLevel 4, AttackPerSt 2,
HtPerDefense 2, DodgeBpPerDx 50, DodgeCapBp 4000, StatPointsPerLevel 3, SkillPointsPerLevel 1 - so hergeleitet,
dass ein frischer Level-1-Krieger exakt die bisherigen Werte hat (MaxHp 100, Attack 15, Defense 5, siehe M4a).
Regeneration (ebenfalls `classes.json`): RegenDelayMs 5000 (Pause seit dem letzten Kampfereignis, bevor
Regeneration einsetzt), RegenIntervalMs 1000, HpRegenPercent 1, SpRegenPercent 2 (jeweils vom Maximum,
mindestens 1 Punkt pro Intervall, gedeckelt am Maximum).

XP: `data/progression.json` (neu) - `MaxLevel` 25, `xpToNext`-Tabelle (Level 1..24, Formel `(8+L)*10*L`) und
eine Levelunterschieds-Tabelle (Spielerlevel minus Monsterlevel → Prozent der Basis-XP, 15 Stufen von
`<=-4: 120%` bis `>=+10: 10%`). `data/mobs.json` bekam `level`/`baseXp` (Regel `baseXp = 10*level`) - z. B.
Wildhund Level 3/30 XP, Waldschrat Level 6/60 XP. XP-Gewinn = `BaseXp * Prozent(Levelunterschied) / 100`,
ganzzahlig abgerundet, mindestens 1 solange Prozent > 0, keine XP mehr ab MaxLevel. `World.AwardXp` wendet
mehrere Levelaufstiege in einer Schleife an (falls die XP reichen), pro Aufstieg gibt es
StatPointsPerLevel/SkillPointsPerLevel Punkte und volle Heilung (HP+SP).

Zentrale Logik: `World.RecomputeDerivedStats(entity)` (`Lumiis.Server.Game`) ist die EINZIGE Stelle, die
MaxHp/MaxSp/Attack/Defense/DodgeBp berechnet - aufgerufen bei Join, Levelaufstieg, Punkteverteilung und
Equip/Unequip. Ohne `Entity.ClassDef` (bestehende netzwerk-/kampfferne Tests) ein No-op für die
Level-Formel-Werte, der Ausrüstungs-Bonus auf Attack/Defense wird trotzdem immer angewendet (bisheriges
Verhalten bleibt für diese Tests exakt erhalten). `Entity` hält dafür eine Referenz auf `ClassDefinition`
(`Lumiis.Data.Classes`) statt alle ~20 Formelkonstanten zu duplizieren - World kennt Lumiis.Data ohnehin schon
(CollisionMap/ItemCatalog).

Ausweichen: `World.TryAttack` würfelt (injizierbare Zufallsquelle, Wert 0..9999 kleiner als `DodgeBp` des
Ziels) NACH Cooldown-Verbrauch und reaktiver Monster-Zielerfassung (M4c) - beide bleiben bei einem Ausweicher
unverändert, nur der Schaden entfällt. `EntityDodgedPacket` (an alle AOI-Beobachter des Ziels) ersetzt
`EntityDamagedPacket` für diesen Treffer komplett. Monster haben immer `DodgeBp` 0 (kein `ClassDef`, nie neu
berechnet).

Regeneration: `World.RunRegeneration`, Teil von `World.Tick` - pro Spieler höchstens einmal pro
`RegenIntervalMs` geprüft (`Entity.NextRegenAtMs`, dasselbe Fälligkeits-Muster wie `NextWanderAtMs` bei
Monstern), nur wenn seit `Entity.LastCombatAtMs` (gesetzt bei Schaden nehmen, Schaden austeilen oder
Ausweichen - jeweils nur bei einem tatsächlichen Treffer/Ausweicher, nicht bei einem Fehlschlag der
Gegenseite) mindestens `RegenDelayMs` vergangen sind. Tote regenerieren nicht. `CharacterStatsPacket` wird
dafür nur bei tatsächlicher HP/SP-Änderung verschickt (höchstens einmal pro `RegenIntervalMs`).

Login-Regeln (`World.TryJoin`, "Weg 2"): `Character.CurrentHp`/`CurrentSp` (beide `int?`) - `null` bedeutet
voll (frischer oder nie im Tod ausgeloggter Charakter), ein Wert `<= 0` bedeutet "im Tod ausgeloggt" und wird
beim Join wiederbelebt (HP UND SP voll, Position Kartenmitte - "wiederbelebt" wird ausschließlich an HP
festgemacht, dieselbe Definition wie `Entity.IsDead`), ein Wert über dem aktuell berechneten Maximum wird
darauf begrenzt (z. B. nach einem nachträglichen Stat-Reset). Spieler-Respawn (M4a) setzt weiterhin HP UND SP
voll, Levelaufstieg heilt ebenfalls voll.

Netzwerk: `CharacterStatsPacket` (Server→Client, nur an den Besitzer, immer der VOLLSTÄNDIGE Stand - Level,
Xp, XpToNext, St/Ht/Dx/Iq, UnspentStatPoints/UnspentSkillPoints, MaxHp/CurrentHp, MaxSp/CurrentSp, Attack,
Defense, DodgeBp) wird beim Join direkt nach JoinAccepted/InventorySync/YangChanged verschickt (EntityHpChanged
bleibt dazwischen, bestehende Reihenfolge) und danach erneut bei XP-Gewinn/Levelaufstieg, Punkteverteilung,
Equip/Unequip, Heiltrank, Respawn und Regeneration. `AllocateStatPointRequestPacket` (Client→Server, `StatKind`
St/Ht/Dx/Iq) - eine ungültige Anfrage (keine Punkte, unbekannter Wert) wird nur geloggt und verworfen, der
Server antwortet trotzdem mit dem aktuellen `CharacterStatsPacket` (keine Trennung, kein eigenes
Fehlerpaket). `DebugSetLevelRequestPacket` (Dev-Tools, siehe unten). Protocol.Version 11.

Persistenz: Level/Xp/SpentSt/SpentHt/SpentDx/SpentIq/UnspentStatPoints/UnspentSkillPoints/CurrentHp/CurrentSp
gehen in den bestehenden kombinierten `CharacterSaveRequest`-Channel (M5b, bisher Yang+Inventar) - seit M6a
zusätzlich mit zwei neuen Auslösern: periodisch alle 30s (dieselbe Fälligkeit wie die Positions-Speicherung)
und bei Disconnect. Sofortiges Speichern weiterhin nur bei Levelaufstieg und Punkteverteilung (plus
Dev-Tools-`DebugSetLevel`) - Schaden/Regeneration lösen bewusst KEIN sofortiges Speichern aus (der
periodische/Disconnect-Auslöser holt diese HP/SP-Änderungen ein).

Client: `ClientWorld.CharacterStats` (neue `CharacterState`-Klasse, UnityEngine-frei) hält Level/Xp/XpToNext/
Werte/SP/Angriff/Verteidigung/Ausweichen - eigene HP laufen weiterhin über die bestehende gemeinsame `ApplyHp`
(kein zweiter Speicherort für CurrentHp/MaxHp). Debug-Panel-Bereich "Charakter" (nur Editor/Development-Build):
Level, "XP x/y" (bei MaxLevel "MAX"), HP, SP, ST/HT/DX/IQ mit "+"-Knopf (nur aktiv bei
UnspentStatPoints > 0), Status-/Fertigkeitspunkte, Angriff/Verteidigung/Ausweichen in Prozent, dazu ein
Level-Cycler + "Level setzen"-Knopf (Dev-Tools). Overlay-Meldungen: "Level Up! Level N" (am steigenden Level
erkannt), "Ausgewichen!"/"Verfehlt!" (`EntityDodgedPacket`, je nachdem ob die eigene Entity Ziel oder Angreifer war).

Bewusst NICHT in M6a: Skills (M6b - Fertigkeitspunkte werden nur gesammelt, nicht ausgegeben), Upgrade-System
(M6c), Lumiis-Steine (M6d), Stat-Reset für Spieler (nur Dev-Tools-`DebugSetLevel` kann das zum Testen), keine
Monster-Regeneration, keine neuen Monstertypen/höheren Level für Monster (nur Level/BaseXp an den bestehenden
zwei Typen ergänzt).

## Skills
Seit M6b gibt es vier Skills, auslösbar über die Tasten 1-4 (echte Spielsteuerung, kein Debug-Feature),
investierbar über die seit M6a gesammelten Fertigkeitspunkte. Alle Zahlenwerte in `data/skills.json` sind
Platzhalter (siehe das `_hinweis`-Feld dort), spätere Justierung erwartet:
- Wuchtschlag (SkillId 1, SingleStrike): Einzelzielschaden mit stufenabhängigem Multiplikator auf den
  normalen Schaden (Stufe 1 ~180%, Stufe 17 ~450%), Reichweite wie ein normaler Angriff (MeleeRangeWire).
  Braucht ein Ziel.
- Kreisschlag (SkillId 2, AreaAttack): trifft alle gültigen Ziele (anderes Kind als der Nutzer, lebendig,
  kein NPC) innerhalb eines festen RadiusWire (200 Wire-Einheiten, konstant über alle Stufen) um den
  Nutzer, niedrigerer Multiplikator als Wuchtschlag (Stufe 1 ~100%, Stufe 17 ~250%), jedes Ziel weicht
  unabhängig aus. Kein Ziel nötig.
- Kampfrausch (SkillId 3, AttackBuff): erhöht Attack vorübergehend um einen stufenabhängigen Prozentsatz
  (Stufe 1 ~10%, Stufe 17 ~50%), feste Dauer 15s (konstant über alle Stufen). Kein Ziel nötig.
- Hast (SkillId 4, AttackSpeedBuff): reduziert den NORMALEN Angriffs-Cooldown vorübergehend um einen
  stufenabhängigen Prozentsatz (Stufe 1 ~2%, Stufe 17 EXAKT 20%, wie vom User vorgegeben), feste Dauer
  15s. Kein Ziel nötig, wirkt nicht auf die Skill-eigenen Cooldowns.

Jeder Skill hat Stufen 1-17 (0 = nicht gelernt), investierbar per Fertigkeitspunkt (`World.
TryAllocateSkillPoint`, analog zu `TryAllocateStatPoint` aus M6a). Jeder Skill hat einen EIGENEN Cooldown
(`Entity.SkillCooldownReadyAtMs`, vier unabhängige Tracker pro Charakter), getrennt vom normalen
Angriffs-Cooldown (`Entity.AttackReadyAtMs`) - Wuchtschlag im eigenen Cooldown blockiert weder den
normalen Angriff noch die anderen drei Skills.

`World.ResolveHit` (aus `TryAttack` extrahiert) ist die gemeinsame Treffer-Auflösung für normale Angriffe
(Multiplikator 100%) UND Wuchtschlag/Kreisschlag (stufenabhängiger Multiplikator) - Ausweichwurf, reaktive
Zielerfassung, Tod-Behandlung inkl. XP/Yang/Drops laufen für Skill-Treffer exakt gleich wie bei einem
normalen Angriff. Buffs (`Entity.ActiveBuffs`, ein Dictionary je `BuffType` mit Stärke + ExpiresAtMs)
ersetzen sich bei erneutem Cast (kein Stapeln) und werden von `World.Tick` bei Ablauf entfernt
(`RecomputeDerivedStats` läuft bei Buff-Start UND -Ende, damit der Wert sofort korrekt ist). Kampfrausch
wirkt über `RecomputeDerivedStats` auf Attack (nach der bisherigen Ausrüstungs-Rechnung, gerundet), Hast
über `World.EffectiveAttackCooldownMs` auf den nächsten normalen Angriff. Buffs und Skill-Cooldowns werden
NICHT persistiert (sterben mit der Entity beim Disconnect, keine Datenbank-Spalte) - nur die gelernten
Skillstufen selbst (`CharacterSkillLevel`, analog `CharacterInventoryItem`) überleben Disconnect/Rejoin,
über denselben kombinierten `CharacterSaveRequest`-Channel wie Statuspunkte/Inventar/Yang.

Pakete (Protocol.Version 12): `UseSkillRequestPacket` (Client→Server, SkillId+TargetEntityId, 0 = kein
Ziel), `UseSkillFailedPacket` (nur an den Anfragenden, Reason-Enum NotLearned/NotEnoughSp/OnCooldown/
TargetRequired/TargetNotFound/TargetAlreadyDead/OutOfRange/TargetIsPlayer/TargetNotAttackable),
`ActiveBuffsChangedPacket` (nur an den Besitzer, Liste aus (BuffType, RemainingMs) - RemainingMs statt
Zeitstempel, Projekt-Konvention keine Client-Uhrensynchronisation, leere Liste = keine aktiven Buffs),
`AllocateSkillPointRequestPacket` (analog `AllocateStatPointRequestPacket`). `CharacterStatsPacket` trägt
seit M6b zusätzlich alle gelernten Skillstufen (Skills-Liste, nur Einträge mit Stufe > 0). Erfolgreiche
Schadens-Skills verschicken dieselben Pakete wie ein normaler Angriff (EntityDamaged/EntityDied/
EntityDodged, je Ziel), erfolgreiche Buff-Skills ein `ActiveBuffsChangedPacket` plus `CharacterStatsPacket`
(Attack kann sich geändert haben) an den Nutzer.

Client: echte Tasten 1-4 (NICHT hinter `#if UNITY_EDITOR || DEVELOPMENT_BUILD`) senden
`UseSkillRequestPacket` - Wuchtschlag nutzt das aktuell per Rechtsklick anvisierte Ziel, die anderen drei
brauchen keins. Neue `SkillCatalogClient` (lädt `skills.json` aus StreamingAssets, analog
`ItemCatalogClient`) nur für Anzeigenamen und CooldownMs je Stufe. Overlay (immer sichtbar, nicht nur
Editor/Development-Build) zeigt die Tastenbelegung, lokal optimistisch gezählte Skill-Cooldowns (gesetzt
beim Senden, der Server bleibt über `UseSkillFailedPacket(OnCooldown)` die einzige Autorität) und
server-autoritative Buff-Restzeiten (`ActiveBuffsChangedPacket`, lokal mit `Time.deltaTime`
heruntergezählt). Debug-Test-Panel bekommt einen Bereich "Skills" (Stufe 0-17 je Skill, "+"-Knopf, analog
zum Statuspunkte-Bereich aus M6a).

Bewusst NICHT in M6b: Skillbücher, Meister-System nach Stufe 17 (später), Partikeleffekte/Animationen,
Skill-Icons (Text/Name reicht, wie bei Health-Bars/Items/Shop).

## Ausruestungs-Aufwertung
Seit M6c lässt sich **angelegte** Ausrüstung (Waffe/Rüstung/Helm/Schild) mit einem Schleifstein von +0 bis
+9 aufwerten. Inventar-Items werden bewusst NICHT direkt aufgewertet, es gibt keinen neuen NPC. Jede
Ausrüstungs-Item-Instanz trägt ihre eigene Stufe (0-9), die beim Anlegen/Ablegen mitwandert.

Daten (`data/upgrade.json`, neu): `maxUpgradeLevel` 9, `successChancePercent` mit genau 9 Einträgen für die
Übergänge +0->+1 bis +8->+9 (Platzhalter, absteigend 90/80/70/60/50/40/30/20/10), `whetstoneItemId` (9,
Schleifstein) und `protectionScrollItemId` (10, Bewahrungsrolle). `UpgradeCatalog` validiert Länge/Bereich
der Tabelle und dass beide IDs in items.json existieren und den ItemType `UpgradeMaterial` bzw.
`ProtectionScroll` haben - sonst Startabbruch. `data/items.json` hat die beiden neuen Verbrauchsgüter
(iconKey `magic_stone`/`blessing_scroll`, SellPrice 0, BuyPrice 50/500) und je Ausrüstungs-Item
`upgradeBonusPerLevel` (Waffe -> Attack, Armor/Helmet/Shield -> Defense; stärkere Items haben höhere
Werte). Der Dorfhändler verkauft beide neuen Items.

Ablauf (`World.TryUpgrade`, kein Netzwerkbezug, `Random` injizierbar): ALLE Vorprüfungen laufen VOR jedem
Verbrauch - Slot belegt, Stufe &lt; 9, Schleifstein vorhanden, bei angefordertem Schutz zusätzlich
Bewahrungsrolle vorhanden. Fehlt etwas, kommt nur ein `UpgradeActionFailedPacket` (SlotEmpty/
AlreadyMaxLevel/NoMaterial/NoProtectionScroll), es wird nichts verbraucht. Danach wird gegen
`successChancePercent[aktuelle Stufe]` gewürfelt. Der Schleifstein wird IMMER verbraucht, die
Bewahrungsrolle bei Verwendung ebenfalls IMMER (auch bei Erfolg). **Zerstoerungsregel:** Fehlschlag MIT
Bewahrungsrolle lässt Item und Stufe unverändert; Fehlschlag OHNE Bewahrungsrolle zerstört die komplette
Item-Instanz (Ausrüstungs-Slot wird geleert, Stufe 0, Attack/Defense fallen zurück). Erfolg erhöht die
Stufe um 1; die Boni werden in `World.RecomputeDerivedStats` als `UpgradeBonusPerLevel * Stufe` addiert.

Persistenz: `CharacterInventoryItem.UpgradeLevel` (Default 0) und die vier Spalten
`Character.EquippedArmor/Helmet/Weapon/ShieldUpgradeLevel` (Default 0), Migration `AddUpgradeLevels`.
`TryEquip`/`TryUnequip` verschieben ItemId UND Stufe gemeinsam zwischen Inventar und Ausrüstung. Der
kombinierte Save-Kanal (`CharacterSaveRequest`, voller Snapshot wie Inventar/Yang/Fortschritt/Skills)
enthält jetzt auch die Stufen und wird SOFORT nach jedem abgeschlossenen Aufwertungsversuch ausgelöst.
`CompleteJoin` lädt die Stufen beim Join wieder in die Entity.

Netzwerk (Protocol.Version 19): `UpgradeItemRequestPacket` (Client->Server: EquipmentSlot,
UseProtectionScroll), `UpgradeItemResultPacket` (Server->Client: EquipmentSlot, Success, Destroyed,
NewUpgradeLevel) und `UpgradeActionFailedPacket` (Reason-Enum). Ein abgeschlossener Versuch (auch
Fehlschlag/Zerstörung) liefert zusätzlich `InventorySyncPacket` und `CharacterStatsPacket` an den
Anfragenden, bei Zerstörung außerdem `AppearanceChangedPacket` an alle AOI-Beobachter (die sichtbare
Ausrüstung verschwindet); der Server schickt eine deutsche System-Chat-Zeile. `InventorySyncPacket`
enthält seit M6c pro Inventarplatz `(SlotIndex, ItemId, UpgradeLevel)` sowie vier
`Equipped...UpgradeLevel`-Felder. `EntitySpawnPacket`/Appearance bleiben unverändert - eine Aufwertung
ändert nur Zahlenwerte, nie das Aussehen.

Client: Debug-Panel-Bereich "Ausrüstung" zeigt pro Slot "+N", einen "Aufwerten"-Knopf (nur bei belegtem
Slot und Stufe &lt; 9) und eine Checkbox "Bewahrungsrolle" (clientseitig nur aktiv, wenn laut lokalem
Inventar-Cache eine Rolle vorhanden ist - der Server validiert final). Inventar-Slots zeigen ebenfalls
"+N". Overlay-/Systemmeldungen: Erfolg ("Schwert ist jetzt +3!"), Fehlschlag mit Rolle ("... durch die
Bewahrungsrolle gerettet."), Fehlschlag ohne Rolle ("... wurde zerstört!"), plus deutsche Texte je
`UpgradeActionFailedReason`. `ItemCatalogClient` kennt `upgradeBonusPerLevel` und die neuen ItemTypes als
Strings.

Bewusst NICHT in M6c: Aufwertung von Inventar-Items, Stapeln mehrerer Schleifsteine (jeder Inventarplatz
trägt genau ein Item), Sockel/Lumiis-Steine (M6d), Item-Instanz-IDs (weiterhin nur Slot+ItemId),
grafisches Aufwertungsmenü (Debug-Panel-Stil), automatische Migration bestehender Postgres-Datenbanken
(mit `dotnet ef database update` anzuwenden).

## Kampf-Visualisierung
Seit der Aufgabe `combat-visuals.md` wird Kampf sichtbar gemacht - REIN CLIENTSEITIG UND KOSMETISCH,
keine Protokolländerung, keine neue Serverwahrheit (der Server bleibt für jede Kampfaktion die
einzige Autorität, siehe "Verbindliche Architektur" - alle unten genannten Effekte können jederzeit
"falsch" liegen, z. B. bei Paketverlust oder einem abgelehnten Skill, das ist bewusst so und wird
vom Server ohnehin unabhängig davon korrekt entschieden):
- Reichweitenring: dünner, halbtransparenter Ring in `MeleeRangeWire` (neuer `ClassCatalogClient`,
  lädt nur `meleeRangeWire` aus `classes.json`) um den lokalen Spieler, immer sichtbar solange
  verbunden, unabhängig von Zielauswahl.
- Radius-Aufblitzen: kurzzeitiger (~350ms) größerer Ring in `RadiusWire` (`SkillCatalogClient` um
  dieses Feld erweitert) beim Auslösen von Kreisschlag - optimistisch beim SENDEN ausgelöst, nicht
  reaktiv am eingehenden Paket: `EntityDamagedPacket`/`EntityDodgedPacket` tragen keine
  Skill-Kennzeichnung, ein Kreisschlag ohne Ziel im Radius erzeugt ohnehin kein Paket - reaktiv wäre
  also strukturell unzuverlässig.
- Trefferlinie: kurzzeitige (~200ms) Linie zwischen Angreifer- und Zielposition bei jedem
  `EntityDamagedPacket` (rot) und `EntityDodgedPacket` (blau-grau) - unabhängig davon, ob der lokale
  Spieler beteiligt ist, solange beide EntityIds aktuell bekannte Positionen in `ClientWorld.Entities`
  haben. Mehrere gleichzeitige Linien (z. B. Kreisschlag gegen mehrere Ziele) sind unabhängig
  voneinander (je ein eigenes GameObject).
- Blickrichtung beim Angreifen: dreht die angreifende Figur sofort lokal zum Ziel
  (`ClientWorld.SetFacingOverride`, per `FacingMath.FromDelta`) - optimistisch beim Senden für den
  lokalen Spieler (`AttackRequestPacket`, zielgerichtetes `UseSkillRequestPacket`/Wuchtschlag),
  reaktiv für jeden anderen Angreifer beim Verarbeiten von `EntityDamagedPacket`/`EntityDodgedPacket`
  (dieselbe Stelle wie die Trefferlinie, da beide Positionen dort ohnehin schon bekannt sind). Kein
  eigener Zustand/Timer: der nächste echte `HandleEntityMove`-Aufruf überschreibt die Blickrichtung
  wieder ganz normal bewegungsbasiert - "kein dauerhafter Override" ist damit strukturell garantiert.
- Buff-Ring: solange Kampfrausch aktiv ist, ein rot-oranger Ring um den lokalen Spieler; solange
  Hast aktiv ist, ein gelber Ring mit größerem Durchmesser (konzentrisch, überschneiden sich dadurch
  nie, auch wenn beide gleichzeitig aktiv sind). Voller Resync bei jedem `ActiveBuffsChangedPacket`
  (kein Delta, kein eigener Timer) - verschwindet exakt, wenn das Paket den Buff nicht mehr listet.
  `ActiveBuffsChangedPacket` geht laut Protokoll nur an den Besitzer (siehe Abschnitt "Skills") -
  Buff-Ringe sind deshalb strukturell auf den lokalen Spieler beschränkt, keine Protokolländerung
  nötig oder möglich.

Gemeinsame Basis: eine Einheits-Ring-Sprite (`PlaceholderSprites.CreateRing` mit `pixelsPerUnit ==
pixelSize`, Durchmesser exakt 1 Weltenheit bei `transform.localScale` 1) wird von `RangeRingView`
(Reichweitenring UND Buff-Ringe - nur Farbe/Sortierung/Durchmesser unterscheiden sich je Aufrufer)
und `RadiusFlashView` (Kreisschlag) geteilt - Größe nur über `localScale` gesteuert, keine mehrfach
gebackenen Texturen. `HitLineView` nutzt `LineRenderer`. Alle vier neuen View-Komponenten sind
unparented (kein Kind der `CharacterView`) und rühren die bestehende SortingGroup/Rang-Sortierung
der Charaktere nicht an (siehe Abschnitt "Client"). `classes.json` wird seit dieser Aufgabe
zusätzlich zu `looks.json`/`items.json`/`skills.json` nach
`client/Assets/StreamingAssets/lumiis-data/` kopiert (neuer `ClassCatalogClient` - Alpha kennt nur
eine Klasse, der Client nimmt deshalb bewusst den ersten Katalog-Eintrag statt einer echten
Zuordnung, analog zur serverseitigen `GameServer.DefaultClassName`-Vereinfachung).

## Paper-Doll-Socketing, Waffen-VFX und Rüstungs-Palette
Der Client rendert Waffen und Rüstungen seit dem Rendering-Umbau modular (weiterhin code-first, keine
Protokolländerung, keine Serverwahrheit):
- `data/items.json`: Waffen tragen zusätzlich `spriteKey` (PNG-Name unter
  `client/Assets/Resources/Sprites/Items/`, ohne Endung), `pivotOffset` (`x`/`y` in Canvas-Pixeln relativ
  zum Griff) und `upgradeLevel` (0-9, Vorlagen-/Fallback-Wert fürs Testfeld; die echte
  Instanzstufe kommt seit M6c aus dem Inventar-Sync und wird in `GameClient` für die eigene Waffe
  genutzt, siehe Abschnitt "Ausruestungs-Aufwertung"). `ItemCatalog` (Server, nur Validierung der Stufe)
  und `ItemCatalogClient` (Client, inkl. `FirstOfType` fürs Testfeld) lesen die Felder.
- `data/looks.json`: Rüstungs-Looks tragen `colors` (`primaryColor`/`secondaryColor`/`accentColor` als
  Hex). `LookCatalogClient.TryGetArmorColors` liefert sie; `CharacterView.ApplyArmorColors` schreibt sie
  per `MaterialPropertyBlock` auf `_Color_ArmorPrimary`, `_Color_ArmorSecondary`, `_Color_ArmorTrim`
  (kein Material-Instanzieren, Draw Calls bleiben intakt). Die Rüstung nutzt dafür das gemeinsame
  `Resources/Materials/CharacterArmor.mat` (Shader `Lumiis/CharacterArmor`, unlit/URP-2D-kompatibel);
  ohne dieses Material dient `primaryColor` als sichtbarer `SpriteRenderer.color`-Fallback.
- Hierarchie: `CharacterView -> Body (SpriteRenderer) -> WeaponSocket -> Weapon (SpriteRenderer)`.
  `CharacterView.SetWeapon(Sprite, Vector2 pivotOffset, int upgradeLevel)` setzt die Item-Waffe,
  `ClearWeapon()` fällt auf den Appearance-Platzhalter zurück. `ISpriteProvider.LoadWeaponSprite`
  (synchron, `CachingSpriteProvider` cached nach `spriteKey`) lädt das PNG aus
  `Resources/Sprites/Items/`; fehlt die Datei, bleibt der Platzhalter sichtbar.
- `CharacterAnimator` nutzt `WeaponSocketFrameConfig` (ScriptableObject, `CreateAssetMenu`, für den
  Platzhalterbetrieb per `CreateDefault()` erzeugt): je State/Frame und `WeaponFacing`
  (Down/Up/Left/Right) werden `handOffset` (relativ zum Hand-Anker aus looks.json), `handRotation`
  (Grad, bei Left gespiegelt) und `weaponSortingOrder` (relativ zur Körper-Ebene, Defaults folgen
  `docs/sprite-spec.md`: Up -2, Down +3, Side +2) gespeichert. Bei jedem Frame-/Facing-Wechsel setzt
  `CharacterView.ApplyWeaponSocketFrame` automatisch `WeaponSocket.localPosition`,
  `localEulerAngles` und `WeaponRenderer.sortingOrder`.
- Waffen-VFX: zwei Code-erzeugte `ParticleSystem`-Kinder am Socket (`Plus7Glow` gelb/weiß,
  `Plus9Glow` dichter/blau) werden ab Stufe 7 bzw. 9 aktiviert. Der `TrailRenderer` an der Waffe
  emittiert nur während der mittleren Schlagframes; `GameClient` startet den Schlagimpuls optimistisch
  beim Senden von Angriff/Skill und reaktiv bei `EntityDamaged`/`EntityDodged` für den Angreifer.
- Debug/Testboard: Button "Waffen-Testfeld +0..+9" (F2-Bereich) baut eine Spalte je Stufe mit
  periodischem Schlag; zusätzlich zeigt das Panel "Waffe +N" einen lokalen Stufen-Override für die
  eigene Figur (nur Editor/Development-Build, rein kosmetisch). Rüstungsfarben lassen sich über den
  bestehenden Rüstungs-Cycler und das Rüstungs-Raster prüfen.

## Asset-Testlabor (F10, nur Editor/Development)
Für die Asset-Prüfung ohne Server-/Monster-Gewimmel gibt es eine eigene UI-Toolkit-Oberfläche
(`AssetTestLab.uxml`, geöffnet mit **F10** oder dem Button "Asset-Testlabor" im Debug-Panel):
- Beim Öffnen wird die Szene aufgeräumt: Server-Figuren, Monster, NPCs, Reichweiten-/Buff-Ringe und die
  alten F2/F9-Testboards werden nur deaktiviert (nicht zerstört) und beim Schließen wieder aktiviert.
  Das IMGUI-Debug-Overlay ist währenddessen aus; F1 zeigt es außerhalb des Labors wieder an (Default ist
  jetzt aus, damit die Szene beim Spielen sauber bleibt).
- Eine lokale Vorschaufigur (eigene `CharacterView`/`CharacterAnimator`, kein Server-Entity) steht an der
  Spielerposition, umgeben von drei Trainingspuppen (Holzposten/Stroh, eigene Laufzeitform aus
  `PlaceholderSprites`).
- Die UI bietet: Waffenwahl aus `items.json` (inkl. Anzeige, ob das erwartete PNG unter
  `Resources/Sprites/Items/` existiert oder der Platzhalter greift), Waffenstufe +0..+9, Rüstung/Helm/
  Schild/Frisur/Haarfarbe, Blickrichtung (Down/Up/Left/Right), Idle/Lauf-Demo/Angriff sowie
  "Trainingspuppen neu platzieren"/"Figur zurücksetzen".
- Die drei Waffen-Platzhalter `weapon_2_wood.png`, `weapon_2_iron.png`, `weapon_3_axe.png` werden
  reproduzierbar mit `tools/generate-placeholder-weapons.py` erzeugt (eigene Formen, Pivot am Griff,
  PPU 64); Herkunft in `docs/art-provenance.md`. Fehlt eine Datei, fällt die Vorschau weiterhin auf
  `PlaceholderSpriteProvider` zurück.
- Screenshot ohne Unity-Neustart: **F12** im Play-Modus oder "Screenshot aufnehmen" in der Labor-UI.
  Für externe/automatisierte Aufnahmen aus einem bereits laufenden Play-Modus:
  `tools/request-screenshot.sh /tmp/mein-bild.png` (schreibt eine Request-Datei, `ScreenshotRequestWatcher`
  nimmt sie beim nächsten Frame auf). Nur der komplett kalte Lauf
  `tools/capture-client-screenshots.sh` startet Unity einmalig im GUI-Modus und beendet es danach.
- Lumiis-Referenz-Assets: `tools/import-lumiis-assets.py` importiert 533 Gegenstands-Icons aus
  `~/Desktop/lumiis-assets-archiv/assets/icons` nach `Resources/Icons` (34 Kategorien, Manifest
  `icon-manifest.json`, Windows-taugliche Dateinamen) und 53 Charakter-Sheets aus
  `~/Desktop/lumiis/packages/client/public/assets` nach `Resources/Characters`. `ItemIconCatalog`
  zeigt Icons in Inventar/Ausrüstung/Shop; die Labor-UI hat eine paginierte Icon-Galerie (64/Seite).
- "Charakter-Galerie" im Labor zeigt acht echte Lumiis-Figuren (Krieger/Ninja/Sura/Schamane, m/w)
  aus den Sheets (48×48-Zellen, 9 Spalten, 3 Ansichten, Fußpunkt 24/44). Der Screenshot-Lauf legt
  zusätzlich `/tmp/lumiis-asset-lab-characters.png` ab.
- "Karten-Vorschau (Greenfield)" zeigt die aus den Lumiis-Tile-IDs gerenderte 80×80-Karte als
  Weltbild (`Resources/Maps/greenfield.png`, `tools/render-lumiis-map-preview.py`); das ist eine
  Vorschau, noch keine Multi-Map-Serverintegration (siehe `docs/map-import-plan.md`).
- Feature-Gap und Kartenplanung: `docs/feature-gap-lumiis.md` (priorisierte Lücken gegenüber
  `lumiis`/`lumiis-2d`), `docs/icon-integration-plan.md`, `docs/map-import-plan.md`. Mehrkarten/
  Portale bleiben ein eigener Server-Umbau; der Plan beschreibt Konvertierung und MapCatalog.

## M7-HUD, Chat und Gruppe
Seit 2026-10-24 gibt es ein dauerhaftes UI-Toolkit-HUD (`Hud.uxml`, `HudController`), den Chat-MVP und
eine Gruppen-MVP (Protocol.Version 14):
- HUD links oben: Name/Level, HP-/SP-/EXP-Balken und Yang. Mitte oben: Zielname + HP, darunter die
  letzte Kampf-/Level-/Ausweich-Meldung. Unten rechts: vier Skill-Slots (1-4) mit Name, gelernter Stufe
  und lokaler Cooldown-Anzeige, darunter aktive Buffs (Kampfrausch/Hast mit Restzeit). Das HUD liest
  ausschließlich den bestehenden `GameClient`/`ClientWorld`-Zustand; das alte IMGUI-Overlay bleibt als
  F1-Debugansicht.
- Chat links unten: Scrollback (max. 60 Zeilen), Kanäle Karte/Global/Flüstern, Whisper-Zielfeld,
  Enter öffnet/sendet, Escape verlässt das Eingabefeld. Während das Chatfeld fokussiert ist, sind
  Bewegungsklick und Skills 1-4 gesperrt (`GameClient.SetChatInputFocused`), damit Tippen keine
  Spielaktionen auslöst.
- Server: `ChatRequestPacket` (Client->Server, ID 16) und `ChatMessagePacket` (Server->Client, ID 1020),
  `ChatChannel` Map/Global/Whisper/System. SenderName kommt immer aus der Entity, nie aus dem Paket;
  Text wird getrimmt und auf 200 Zeichen begrenzt; pro Verbindung gilt ein 400-ms-Rate-Limit (still
  verworfen, kein Disconnect). Karte und Global broadcasten aktuell an alle verbundenen Spieler (eine
  Karte); Whisper geht an Ziel und Absender. Systemmeldungen kommen nur vom Server.
- `JoinAccepted`/Entity-Spawns bleiben unverändert; Chat ist nicht persistent. Chat kam mit
  Protocol.Version 13, aktuell ist 14 (Gruppe). Roundtrip-Tests für beide Chat-Pakete und ein
  LiteNetLib-Integrationstest (Karte + Whisper) sind grün.
- Gruppe (seit Protocol.Version 20): Chat-Befehle `/invite Name`, `/accept`, `/decline`, `/leave`,
  `/party`, dazu für den Leiter `/kick Name`, `/promote Name`, `/role Name angreifer|verteidiger|keine`
  (gleichwertig per Paket: `PartyKickRequest` 25, `PartyPromoteRequest` 26, `PartySetRoleRequest` 27).
  Logik ohne Netzwerk in `Lumiis.Server.Game.PartyManager`, Mitglieder über die **CharacterId**: wer
  ausloggt, bleibt offline in der Gruppe (`EntityId` 0 im Paket, letzter Level-/HP-Stand) und ist beim
  nächsten Join ohne neue Einladung wieder drin; nach `offlineRemoveAfterSeconds` fliegt er raus. Leiter
  ist immer ein Online-Mitglied (geht er offline/verlässt die Gruppe, übernimmt das erste Online-
  Mitglied). Gruppen mit weniger als zwei Mitgliedern oder ohne Online-Mitglied lösen sich auf. Nur im
  Speicher, überlebt keinen Serverneustart. Regeln in `data/party.json` (`PartyCatalog`,
  `Lumiis.Data.Parties`): maxMembers 8, xpSharePercent 50, Offline-Frist 600 s, Rollen Angreifer (+5 %
  Angriff) und Verteidiger (+5 % Verteidigung) - wirken, solange mindestens zwei Mitglieder online sind,
  über `Entity.PartyAttack/DefenseBonusPercent` in `World.RecomputeDerivedStats`. `PartyChangedPacket`
  (1021) bleibt ein voller Resync; `Members` unverändert, neu die parallele Liste `MemberStatus`
  (Online, Rolle). EXP-Teilen: andere Online-Mitglieder erhalten xpSharePercent des Kill-XP (mind. 1);
  Boden-Drop-Besitz gilt für die ganze Gruppe.
- Audio (P1-Platzhalter): `AudioService` erzeugt Schwung-, Treffer-, Kill-, Schaden-, Loot-, Level-Up-
  und Klick-Clips prozedural (keine Audiodateien) und spielt sie bei Angriff/Skill, EntityDamaged,
  EntityDied, Inventar-Zuwachs und Levelaufstieg. Kein Musiksystem, noch kein Mute-UI.

## Escape-Menü
Seit der Aufgabe `escape-menu.md` gibt es ein kleines Escape-Menü mit drei Aktionen - rein clientseitig,
keine Protokolländerung:
- Charakter wechseln: trennt die Spielverbindung, behält das Token, geht zurück zur Charakterauswahl und
  lädt die Liste per `GET /characters` neu. Bei zwischenzeitlich abgelaufenem Token (401): automatischer
  vollständiger Logout mit Meldung ("Sitzung abgelaufen, bitte erneut anmelden.").
- Ausloggen: trennt die Verbindung und verwirft das Token - wie ein unfreiwilliger Disconnect, zurück zum
  Login-Bildschirm (Benutzername bleibt vorausgefüllt, bestehendes Verhalten).
- Beenden: `Application.Quit()`, im Editor stattdessen `UnityEditor.EditorApplication.isPlaying = false`
  (damit Play im Editor sauber stoppt, statt den ganzen Editor zu beenden).

Mechanik: `LoginFlow.SwitchCharacter()`/`Logout()` setzen den Zustand VOR dem eigentlichen
`NetClient.Stop()`-Aufruf. `LoginFlow.OnDisconnected` behandelt sonst jeden Disconnect aus `InGame`
unbedingt als unfreiwillig (Token verwerfen) - durch die vorgezogene Zustandsänderung sieht es beim
anschließenden Disconnect nur noch den bereits erledigten Wechsel (bestehender No-Op-Zweig), das Token
bleibt bei "Charakter wechseln" also tatsächlich erhalten. `NetClient.Stop()` (vormals privates
`StopClient()`, nur für `OnDestroy`/`OnApplicationQuit`) ist jetzt öffentlich und über einen
`IsRunning`-Guard nachweislich sicher mehrfach aufrufbar.

`EscapeMenu.uxml` (`client/Assets/Resources/UI/`) nutzt dieselbe `Resources.Load<VisualTreeAsset>`+
`CloneTree`-Mechanik wie Login/CharacterSelect - als DRITTE Wurzel im selben `UIDocument` (nach den beiden
bestehenden, wichtig für `LoginControllerPlayModeTests`, die per `root[0]`/`root[1]` genau darauf
zugreifen). Escape öffnet/schließt es, aber nur während `LoginFlowState.InGame`; `ApplyLoginFlowState()`
schließt es zusätzlich bei JEDEM Zustandswechsel (z. B. nach Charakter wechseln/Ausloggen), damit es nie
offen stehen bleibt. Solange es sichtbar ist, sperrt `GameClient.SetGameplayInputLocked(true)`
Bewegungsklick, Angriff/Rechtsklick und Skills 1-4 (`HandleClickInput`/`HandleSkillHotkeys`) - Zoom und
F1-Overlay-Umschaltung bleiben aktiv (keine Gameplay-Aktionen im engeren Sinn).

Standalone-Builds starten seit dieser Aufgabe im Fenstermodus statt Vollbild (`ProjectSettings.asset`:
`fullscreenMode: 3` = Windowed, `defaultScreenWidth`/`defaultScreenHeight` 1280x720, `resizableWindow: 1`)
- betrifft nur echte Player-Builds (`client/Builds/<plattform>/`, siehe `tools/build-client.sh`), der
Editor-Play-Modus läuft ohnehin schon in einem Fenster und ist unverändert.

## Client
Setup nach dem Klonen (auch auf dem zweiten Rechner):
1. `cd server && dotnet build` - baut Lumiis.Shared und kopiert dabei Lumiis.Shared.dll sowie die netstandard2.1-Variante von LiteNetLib.dll nach `client/Assets/Plugins/Lumiis/` (git-ignoriert, entsteht bei jedem Build neu).
2. Danach `client/` mit Unity Hub öffnen (6000.3.24f1). Beim ersten Öffnen importiert Unity die kopierten Plugin-DLLs und legt die zugehörigen .meta-Dateien an.

Struktur (`client/Assets/Lumiis/Scripts/`, Namespace `Lumiis.Client`, code-first, kein Inspector-Verkabeln, ein
Assembly `Lumiis.Client` für den gesamten Client-Code (`Lumiis.Client.asmdef`, seit der Test-Infrastruktur -
PlayMode-Tests brauchen Compile-Zeit-Zugriff auf die Haupt-Client-Assembly, das geht nur über ein eigenes
asmdef statt der Standard-Assembly `Assembly-CSharp`; keine weitere Aufteilung in mehrere Assemblies). Die
davon unabhängige Regel "keine veränderlichen statischen Zustände wegen Fast Enter Play Mode/CoreCLR" (siehe
"Verbindliche Architektur") gilt unverändert weiter - das asmdef ändert nur die Compile-Grenze, nicht das
Domain-Reload-Verhalten):
- `Bootstrap.cs`: RuntimeInitializeOnLoadMethod (AfterSceneLoad), erzeugt Kamera und das Root-GameObject mit NetClient/GameClient/LoginController.
- `NetClient.cs`: dünner LiteNetLib-Adapter (MonoBehaviour) - Connect(host, port, token, characterId)/JoinRequest (M2b-Protokoll: Token+CharacterId statt Name), PacketCodec.Decode, C#-Events für Connected/Disconnected/Paket.
- `LoginController.cs`: Login/Registrierung/Charakterauswahl gegen Lumiis.Auth (HTTP, UnityWebRequest), eigenes UIDocument (UI Toolkit, code-first erzeugt - siehe `client/Assets/Resources/UI/`). Zustände LoggedOut -> LoggingIn -> CharacterSelect -> Connecting -> InGame -> (Disconnect) zurück zu LoggedOut. Ruft nach Charakterwahl `GameClient.PrepareJoin(characterName)` und `NetClient.Connect(...)` auf. Details siehe Abschnitt "Datenbank & Auth-Dienst".
- `ClientWorld.cs`: reine Weltsicht ohne UnityEngine-Bezug und ohne `Time.*`; Bewegung wird über `MovementMath` (Shared) anhand einer von außen übergebenen Zeit interpoliert (Client-Empfangszeit, keine Uhrensynchronisation in M1b), Blickrichtung über `FacingMath` (Shared) aus dem Bewegungsvektor. JoinAccepted (eigene Figur) und EntitySpawn (andere) laufen beide durch denselben `ApplyAppearance`-Pfad.
- `GameClient.cs`: Orchestrierung - Views, Klick-Eingabe (New Input System, `Mouse.current`), Mausrad-Zoom, Kamera-Folgen, IMGUI-Debug-Overlay (Statuszeile, Entity-Liste, F1-Umschaltung; Default aus) und F2-Testboard/F3-F8-Look-Hotkeys/F10-Asset-Testlabor (nur Editor/Development-Build) - alles erst sichtbar nach erfolgreichem Join (`_inGame`-Flag, von LoginController unabhängig gepflegt). Login/Charakterauswahl selbst sind seit M2c LoginControllers Sache, nicht mehr Teil von GameClient.
- `LookCatalogClient.cs`: lädt `looks.json` aus StreamingAssets (JsonUtility).
- `MapCatalogClient.cs`: lädt `lumiis-data/maps/index.json` plus `map.json`/`portals.json` je Karte aus StreamingAssets - nur für Kartenname/Portalmarkierungen (Multi-Map), keine Spielwahrheit.
- `ISpriteProvider.cs`/`PlaceholderSpriteProvider.cs`: liefert Sprites je Ebene/Richtung; aktuell zur Laufzeit erzeugte Formen, später ein `AtlasSpriteProvider` mit echten PNGs.
- `CharacterView.cs`/`CharacterAnimator.cs`/`CharacterTestboard.cs`: Paper-Doll-Zusammensetzung (Sortierung, Spiegeln, Anker, Tönung), prozedurale Idle-/Lauf-Animation, F2-Debug-Raster.
- `AssetTestLab.cs` (+ `Resources/UI/AssetTestLab.uxml`): F10-Testlabor (nur Editor/Development) mit echter UI-Toolkit-Oberfläche, lokaler Vorschaufigur und Trainingspuppen; blendet Server-Figuren/Monster/altes Debug-Overlay während des Testens aus.
- `PlaceholderSprites.cs`: Boden-Raster, Kartenbegrenzung, Ring-Marker für den lokalen Spieler (zur Laufzeit erzeugt, keine Kunst-Assets).
- `LocalPlayerMarker.cs`: Ring am Fußpunkt der eigenen Figur; der eigene Name wird zusätzlich gelb gezeichnet (in `GameClient.DrawEntityLabels`) - keine Ebenen-Tönung, keine Protokolländerung.
- `Editor/BuildScript.cs` (Namespace `Lumiis.Editor`, liegt im Ordner `Editor` - von Unity automatisch aus allen Player-Builds ausgeschlossen): `BuildScript.BuildDevelopment` baut einen Development-Standalone-Build für die aktuelle Plattform nach `client/Builds/<plattform>/` (git-ignoriert, vom bestehenden Muster `client/[Bb]uild*/` abgedeckt).

UI Toolkit-Inhalte (`client/Assets/Resources/UI/`, per `Resources.Load<T>` code-first geladen, kein
Inspector-Verkabeln): `lumiis.uss` (minimale, funktionale Gestaltung - Design-Arbeit kommt später mit dem
Art-Stil), `DefaultRuntimeTheme.tss` (Basis-Theme, `@import url("unity-theme://default")`), `Login.uxml`,
`CharacterSelect.uxml`.

Demo-Ablauf:
1. `docker compose up -d` (nach `source tools/dev-env.sh`), Migration anwenden, dann Server starten: `dotnet run --project server/src/Lumiis.Server` und Auth-Dienst: `dotnet run --project server/src/Lumiis.Auth` (siehe Abschnitt "Datenbank & Auth-Dienst").
2. Unity-Projekt öffnen, Play drücken. Im Login-Fenster registrieren oder anmelden (Benutzername/Passwort), danach in der Charakterauswahl einen Charakter wählen oder neu erstellen - verbindet danach automatisch zum Spielserver.
3. Linksklick auf den Boden bewegt die eigene Figur dorthin, sichtbar interpoliert und mit Blickrichtungswechsel. Die eigene Figur ist am gelben Namen und am Ring unter den Füßen erkennbar.
4. Bots dazuholen: `dotnet run --project server/src/Lumiis.Bot -- --count 6` - melden sich selbst per HTTP bei Lumiis.Auth an (Login-oder-Registrieren, legen bei Bedarf einen Charakter an) und erscheinen dann mit dem beim Anlegen vergebenen Aussehen (siehe `LookCatalog.CreateDefaultAppearance`).
5. Debug-Werkzeuge (nur Editor/Development-Build): **F1** blendet Status-/Debug-Panel ein (Default aus), dort liegen Testboard/Sortier-Testfeld und Look-Cycler. **F10** öffnet das aufgeräumte Asset-Testlabor mit Vorschaufigur, Trainingspuppen und UI für Waffe/Stufe/Rüstung/Facing/Animation (siehe Abschnitt "Asset-Testlabor"). F2/F3-F8/F9 bleiben als Tastatur-Shortcuts zusätzlich nutzbar.

Zwei Clients gleichzeitig testen: Editor plus Build. Der Play-Modus im Editor drosselt ohne Fensterfokus (siehe
"Bekannte Eigenheiten"), was bei mehreren gleichzeitigen Clients stört. Alternative: `tools/build-client.sh`
baut einen Development-Standalone-Build (Unity-Editor muss dafür geschlossen sein, Projekt-Lock). Danach einen
Client per Editor-Play und einen zweiten per `client/Builds/<plattform>/Lumiis` starten - der Build läuft ohne
Fokus-Drosselung, beide können gleichzeitig gegen denselben Server verbunden sein.
`tools/build-client.sh` zeigt währenddessen eine Statuszeile (Phase 1-4, vergangene Zeit, letzter Unity-
Fortschrittsschritt über `LUMIIS-BUILD:`-Marker aus `BuildScript`), bricht sofort ab, wenn schon ein Unity-Prozess
das Projekt offen hat, entfernt eine verwaiste Lockfile und schreibt das Log je Checkout nach
`/tmp/lumiis-build-client-<ordner>.log`. Am Ende steht der Startbefehl; `-logFile -` gibt das Player-Log auf stdout aus.

## Dev-Tools
`--allow-debug-commands` (Default aus, `ServerConfig.AllowDebugCommands`) schaltet Dev-Only-Befehle frei, die
sonst still ignoriert werden, als gäbe es sie nicht (kein Ablehnungspaket, keine Disconnect - nur ein
Log-Eintrag). **Nie in einem "echten" Serverstart aktivieren** - das ist ausschließlich für lokales
Kampf-Testing gedacht (`dotnet run --project server/src/Lumiis.Server -- --allow-debug-commands`, zusätzlich
zu den sonstigen Argumenten).

Drei Befehle:
- `DebugSpawnMonsterRequestPacket` (Client->Server, MobId) - spawnt ein Monster des angegebenen Mob-Typs nahe
  der Position des Absenders (`World.FindNearbyWalkablePoint`, 100-300 Wire-Einheiten Versatz in zufälliger
  Richtung, auf Kartengrenzen geklemmt, bei blockierter Zielzelle kein Retry sondern Rückfall auf die
  Absenderposition selbst). Im Client: Debug-Testpanel (`#if UNITY_EDITOR || DEVELOPMENT_BUILD`) hat dafür
  einen Bereich "Monster spawnen" mit "<"/">"-Cycler + Knopf.
- `DebugGiveYangRequestPacket` (M5b, Client->Server, Amount) - schreibt Amount über `World.CreditYang`
  (MaxYang-Deckel) gut. Im Client: Knopf "+1000 Yang" im Debug-Testpanel.
- `DebugSetLevelRequestPacket` (M6a, Client->Server, Level) - setzt Level = Level, Xp/Spent* zurückgesetzt,
  Unspent* entsprechend nachgezählt ((Level-1) * StatPointsPerLevel/SkillPointsPerLevel), HP und SP voll -
  dient auch als Stat-Reset zum Testen (`World.TryDebugSetLevel`). Im Client: Level-Cycler + "Level setzen"-
  Knopf im Debug-Testpanel-Bereich "Charakter".

Ein so gespawntes Monster ist wie jedes andere behandelt (`World.TryAttack` kennt keinen Sonderfall) - der
einzige Unterschied ist `Entity.SpawnPointId == null` statt der echten `SpawnPoint.Id` aus spawns.json (die
regulären, beim Serverstart aus der SpawnCatalog erzeugten Monster haben immer eine gesetzte SpawnPointId).
Das ist bewusst so gebaut, damit ein künftiges Monster-Respawn-pro-SpawnPoint (M4b) sein Scheduling strikt an
`SpawnPointId.HasValue` hängen kann, nie an `Kind` allein - ein Debug-Monster löst dadurch strukturell nie
einen Respawn an einem (evtl. schon belegten) SpawnPoint aus.

## Alpha-Umfang (Ziel)
Zwei Karten (Map1 Yongan + Map2 Jayang) mit Portalwechsel, Krieger, Level 1-25, Monster/Bosse/Lumiis-Steine, Items/Drops/Shop, Chat, Gruppe. NICHT: Quests/Lua, Reittiere, Kostüme, Drachenseelen, Gildenkrieg, weitere Reiche.

## Meilensteine
M0 Fundament (fertig) | M1 Walking Skeleton (fertig, inkl. M1b Unity-Client, M1c Sichtbarkeit/Testing, M1c-layered-characters Paper-Doll-Charaktere) | M2 Account & Charakter (komplett fertig): M2a Datenbank & Auth-Dienst, M2b Server-Join über Account/Token/Charakter, M2c Unity-Login-UI (UI Toolkit) | M3 Welt (Grid, AOI, Mobs) komplett fertig: M3a Kartenkollision, M3b Sichtbarkeitsbereich (kein Grid - siehe Abschnitt "Sichtbarkeitsbereich (AOI)"), M3c Monster (Spawns, Wander-KI, AOI-Integration - siehe Abschnitt "Monster") | M4 Kampf komplett fertig (inkl. Gegenwehr): M4a Kampf-Grundlagen, M4b Monster-Respawn, M4c Monster-Aggro/Verfolgung/Leine (siehe Abschnitt "Kampf") | M5 Items komplett fertig: M5a Items/Inventar/Ausrüstung/Drops (siehe Abschnitt "Items und Inventar"), M5b Shop/NPCs (siehe Abschnitt "Shop und NPCs") | M6 Metin-Kern abgeschlossen: M6a Level/XP/Werte/SP/Ausweichen/Regeneration (siehe Abschnitt "Fortschritt: Level, XP, Werte"), M6b Skills (siehe Abschnitt "Skills"), M6c Ausruestungs-Aufwertung +0..+9 (siehe Abschnitt "Ausruestungs-Aufwertung"); M6d Lumiis-Stein/Boss inkl. Wellen umgesetzt | M7 Sozial inkl. Multi-Map/Portale | M8 Alpha-Betrieb.
Aktuelle Aufgabe: keine - Multi-Map/Portale, M6c-Aufwertung und M6d Lumiis-Stein/Boss inklusive
75/50/25-%-Wellen sind umgesetzt. Der Import echter Referenzkarten und weitere Inhalte bleiben offen.

## Arbeitsweise
- Kleine Schritte. Jeder endet mit grünem `dotnet test` und einem Commit (Präfix "M1: ...").
- Architektur nicht eigenmächtig ändern. Bei Widerspruch oder Unklarheit stoppen und den User fragen (ein Architekt-Chat prüft die Ergebnisse).
- Am Ende jeder Aufgabe den Abschnitt "Status" hier aktualisieren.

## Bekannte Eigenheiten
- Unity-Editor drosselt die Play-Mode-Update-Schleife (und damit NetClient.PollEvents), wenn das Unity-Fenster
  nicht fokussiert ist. Beim Testen mit mehreren Bots im Editor: entweder das Unity-Fenster fokussiert halten,
  oder in den Editor-Preferences unter "Interaction Mode" (Menüort kann je Unity-Version abweichen) auf
  "No Throttling" stellen. Ein Standalone-Build ist davon nicht betroffen (siehe Abschnitt "Client" -
  Development-Build für Tests ohne Fokus-Effekt).
- Vereinzelter Timing-Flake bei `GameServerIntegrationTests.AfterBuy_DisconnectAndRejoin_YangAndInventoryPersistExactly`
  (M5b) - reproduziert nicht isoliert (15/15 grün einzeln), nur gelegentlich im vollen `dotnet test`-Lauf; keine
  gezielte Ursachendiagnose bisher, nur per Retry toleriert - vermutlich dieselbe grundsätzliche Klasse wie die
  feste `Thread.Sleep(15)` in `Pump()` gegen `NetManager.UpdateTime` bei echten Sockets unter CPU-Last (siehe
  `GameServerIntegrationTests.Pump`-Kommentar), aber nicht bestätigt.

## Bekannte Lücken
- AOI (Sichtbarkeit): Aktuell sehen alle Verbindungen alle Entities, keine Kartenausschnitt-Filterung. Geplant für M3 (Grid, AOI, Mobs). Markiert mit TODO(M3) in `World.cs` und `GameServer.cs`.
- Rate-Limit pro Verbindung: Kein Schutz gegen Paketflut eines einzelnen Clients (z. B. MoveTo-Spam). Geplant für M8 (Alpha-Betrieb).
- Lumiis.Auth: kein Passwort-Reset, keine E-Mail-Verifizierung. Läuft nur über HTTP (kein TLS) - für ein echtes Deploy vor einen TLS-terminierenden Reverse-Proxy stellen.
- Positions-Speicherintervall (Lumiis.Server) fix bei 30s, keine Konfiguration (siehe `GameServer.PositionSaveIntervalMs`).
- Kein Token-Refresh: die Session (LoginController, Lumiis.Data.TokenService) endet nach 30 Minuten fix, danach muss sich der Client neu anmelden (JoinRejectReason.TokenExpired bzw. 401 auf GET/POST /characters schicken automatisch zurück zu LoggedOut).
- ResolveDefaultDataDir() existiert unabhängig in Lumiis.Server und Lumiis.Auth (Pfad-Kandidaten je nach typischem CWD beim Start). Bei künftigen data-dir-bezogenen Bugs beide Stellen prüfen, nicht nur eine. Eine gemeinsame Stelle (z. B. in Lumiis.Data) ist eine spätere Aufräumarbeit, kein aktueller Blocker.
- Verfolgungs-EntityMove-Pakete (M4c): ein aktiv ein Ziel verfolgendes Monster erzeugt jeden Tick (25x/s) ein neues EntityMovePacket an alle AOI-Beobachter (siehe Abschnitt "Kampf") - kein Throttling/Diffing, ähnlich der Rate-Limit-Lücke oben. Für die aktuelle Spielergröße unkritisch, bei mehr gleichzeitig verfolgenden Monstern eine spätere Optimierung.
- Multi-Map-Limitations: Kartenmaße sind global fix (kein `tiles.bin`/Terrain), die Lumiis-Referenzkarten
  wurden nicht importiert (`greenfield` ist generiert), der Client rendert keine Kollision und Gruppe/Party
  ist weiterhin kartenübergreifend (siehe Abschnitt "Multi-Map und Portale").
- Kein YangChangedPacket bei Join: `GameServer.CompleteJoin` lädt `Character.Yang` in die Entity, verschickt dafür aber kein Paket (YangChangedPacket wird nur bei Monster-Kill/Kauf/Verkauf/Debug-Give-Yang ausgelöst) - die Debug-Overlay-Anzeige zeigt bis zum ersten Yang-Ereignis nach dem Login 0 statt des echten Standes. Bislang kein Auftrag, das zu beheben (analog zur inzwischen behobenen HP-Sync-Lücke, siehe Status-Eintrag "Bugfix HP-Anzeige").

## Status
- Multi-Map + Portale (2026-10-25, Protocol.Version 19) fertig: `MapCatalog`/`MapDefinition`/
  `PortalDefinition` + `data/maps/index.json` (village Default + generiertes greenfield), `Character.MapId`
  (Migration `AddCharacterMapId`), eine `World` pro Karte im `GameServer`, `MapChangedPacket`/
  `DebugChangeMapRequestPacket`, AOI-/Chat-Trennung pro Karte, Portal-Sofortsave, Client-Kartenwechsel
  (Boden/Portale/HUD/Debug-API), Tests (`MapCatalogTests`, `WorldPortalTests`, Roundtrips, 5
  Integrationstests). Siehe `docs/map-import-plan.md` (Limitations: kein echter Referenzkarten-Import,
  globale Kartenmaße, kein Client-Kollisionsrendering).
- M6d (Lumiis-Stein/Boss inkl. Wellen) fertig: `data/metins.json` + `MetinCatalog` (Validierung gegen
  `mobs.json`), `World.TryTriggerMetinWaves` in `ResolveHit`, Spawn der Wellen-Mobs über `SpawnMonster`
  (kein SpawnPoint/Respawn), `Entity.MetinWavesTriggeredMask` gegen Mehrfachauslösung, `Program`/`GameServer`
  reichen `MobCatalog`+`MetinCatalog` an jede Karten-World durch. Unit-Test deckt 75/50/25 % und
  "Tod löst keine Nachwelle aus" ab. Siehe `docs/tasks/M6d-lumiis.md`.
- M0 fertig. Shared: PacketWriter/PacketReader/PacketCodec, PacketId (JoinRequest, MoveTo), 23 Tests grün.
- M1 (Server, Bot, Bewegung) fertig, in 5 Commits: Shared (Server->Client-Pakete 1000-1005, GameConstants, NameRules), Game-Logik (World/Entity, reine Bewegung pro Tick), Transport/GameServer (LiteNetLib-Adapter, fester 25-Hz-Loop, Join/Move/Disconnect-Handling), Bot (Lumiis.Bot joint und läuft zufällig), Integrationstests (echtes LiteNetLib auf Loopback). 62 Tests grün, manuell mit Server + `Lumiis.Bot -- --count 3` getestet. M1b (Unity-Client) ist eine eigene, noch offene Aufgabe.
- M1-Fixes (Architektur-Review-Ergebnis) fertig, in 3 Commits: (1) Exceptions dürfen den Server nie beenden - jeder Netzwerk-Callback fängt Fehler ab, nur die betroffene Verbindung wird getrennt, 50 fehlerhafte Ticks in Folge lösen einen sauberen Shutdown mit Exit-Code 1 aus; (2) Fristen laufen in Wandzeit (ms) mit injizierbarer Uhr statt in Ticks; (3) Paketgrößenlimit (1200 Bytes, vor dem Decode geprüft) und NameRules nur noch ASCII (Homoglyph-Schutz). 67 Tests grün, 5x hintereinander stabil.
- M1b (Unity-Client) fertig, in 4 Commits: Shared (Protocol.ConnectionKey, MovementMath + Vertragstest gegen Entity.Tick), MSBuild-Kopierschritt nach client/Assets/Plugins/Lumiis/, Unity-Code (Bootstrap/NetClient/ClientWorld/GameClient, s. Abschnitt "Client"), Doku. 77 Tests grün, 5x stabil. Unity-Batchmode-Kompilierlauf (Lizenz vorhanden) lief fehlerfrei durch (0 Compiler-Fehler); Play-Modus/Demo wurden nicht automatisiert geprüft, siehe Prüf-Checkliste unten. Nebenbei einen Bug in .gitignore behoben, der seit M0 alle Unity-.meta-Dateien verschluckt hat.
- M1c (Sichtbarkeit beim Testen) fertig, in 3 Commits: (1) Bot `--radius` (Default 20 Einheiten) für Zielwahl um den Spawn-Punkt, klemmt auf Kartengrenzen, `--radius 0` = ganze Karte wie bisher; Zielwahl in `BotTargetPicker` ausgelagert und getestet. (2) Zoom (Mausrad, Orthographic Size 3-300, nur Vorzeichen des Scroll-Werts wegen Plattformunterschieden) und F1-Umschaltung fürs Debug-Overlay; Overlay zeigt jetzt zusätzlich eine nach Entfernung sortierte Entity-Liste (max. 12 Zeilen); Debug.Log bei EntitySpawn/EntityDespawn. (3) Auf User-Meldung hin (Bots, die nach dem Unity-Client joinen, erscheinen nicht) `ClientWorld` per Compile-Include in `Lumiis.Tests` testbar gemacht und den Empfangspfad untersucht - siehe unten, Ursache noch offen. 91 Tests grün, 3-5x stabil. Batchmode-Kompilierlauf konnte für Punkt 2-4 nicht laufen, da der User-eigene Editor das Projekt offen hatte; die neu genutzten Input-System-APIs wurden stattdessen im Paket-Quellcode verifiziert.
- M1c-layered-characters (Ebenen-Charaktere/Paper Doll mit Platzhaltern) fertig, in 4 Commits: (1) data/looks.json (Platzhalter-Katalog) plus Shared `Appearance` (Struct, eine Write/Read-Implementierung für JoinAccepted UND EntitySpawn) und `Facing`/`FacingMath` (Hysterese gegen Flackern). (2) Server: `LookCatalog` lädt/validiert looks.json (`--data-dir`), Appearance-Vergabe deterministisch aus dem Namen per FNV-1a - bewusst serverintern, Übergangslösung bis M2; Protocol.Version 2. (3) Client: `LookCatalogClient` (JsonUtility), `PlaceholderSpriteProvider`, `CharacterView`/`CharacterAnimator`/`CharacterTestboard` - Sortierung, Spiegeln, Hand-/Kopf-Anker, Haar-Tönung, hidesHair, F2-Testboard, F3-F8 lokale Look-Hotkeys. `EntityView` entfernt (durch `CharacterView` ersetzt). (4) Doku: neue Konventionen (Asset-Herkunft, Client berechnet nie Spielzustand), docs/art-provenance.md angelegt. 133 Tests grün, 5x stabil. Unity-Batchmode-Kompilierlauf: 0 Compiler-Fehler.
- M1c-Nacharbeiten fertig, in 4 Commits: (1) Bug-Ticket "Bots nach dem Client erscheinen nicht" geschlossen (Editor-Drosselung ohne Fensterfokus, kein Codefehler), Abschnitt "Bekannte Eigenheiten" ergänzt. (2) Lokaler Spieler ohne Ebenen-Tönung erkennbar: gelber Name plus Ring am Fußpunkt (`LocalPlayerMarker`, `PlaceholderSprites.CreateRing`). (3) Review (nur Befund): Sprite-Caching, sortingOrder-Bereich, Testboard/Hotkey-Ausschluss aus Release-Builds. (4) `Editor/BuildScript.cs` (`Lumiis.Editor.BuildScript.BuildDevelopment`) plus `tools/build-client.sh` bauen einen Development-Standalone-Build nach `client/Builds/<plattform>/`; Build tatsächlich ausgeführt und die Binärdatei verifiziert. 133 Tests grün, 5x stabil.
- M1c-Review-Fixes fertig, in 5 Commits (4 geplante Punkte plus eine nachgetragene .meta-Datei): (1) Sortierung: `CharacterView` bekommt eine `SortingGroup` am Figuren-Root (immer sortingOrder 0), Kind-Renderer sortieren nur noch über den festen Slot-Rang (0-5, Marker-Ring -1) statt der überlaufenden Y-Formel; `Renderer2D.asset` auf Transparency Sort Mode "Custom Axis" (Wert 3, per Metadaten-Reflection auf UnityEngine.CoreModule.dll verifiziert) mit der schon vorhandenen Achse (0,1,0) umgestellt; Camera-Properties in Bootstrap bewusst NICHT gesetzt (URP-2D-Quellcode zeigt: nur Renderer2DData wird gelesen); neues F9-Sortier-Testfeld. (2) `CachingSpriteProvider` (Instanz auf GameClient, kein Static) cacht nach (Slot,LookId,Facing,State,Frame), `OnDestroy` räumt Sprites und Texturen auf, Cache-Größe im Debug-Overlay. (3) `CharacterTestboard.cs` komplett in `#if UNITY_EDITOR || DEVELOPMENT_BUILD`, dafür auch die Feld-Deklarationen in `GameClient.cs` abgesichert. (4) Vier vom ersten Build veränderte Unity-Settings-Dateien geprüft und als reines Housekeeping bestätigt und committet. 133 Tests grün, 5x stabil, gesammelter Batchmode-Compile-Check am Ende: 0 Fehler.
- Waffen-Positionsfix + Debug-Test-Panel fertig, in 2 Commits: (1) Waffe hing am Fußpunkt statt auf Handhöhe - `CharacterView.ComputeWeaponPivot` setzte den Sprite-Pivot der Waffe auf den Hand-Anker (richtig für die Schwung-Rotation um den Griff), ließ den Slot-Transform aber wie alle anderen Ebenen bei (0,0) stehen, wodurch der Anker-Pivot seinen eigenen Höhenbeitrag rechnerisch wieder aufhob; `PositionWeapon()` verschiebt den Transform jetzt zusätzlich an die Anker-Weltposition, `PlaceholderSpriteProvider.DrawWeapon` zeichnet exakt am übergebenen Pivot, `hand`-Y-Anker in looks.json von 40/40/42 auf 70 angehoben (≈64 % Körperhöhe). (2) Neues Debug-Test-Panel rechts oben (nur Editor/Development-Build, überlappt das Status-Overlay nicht): Buttons für Testboard/Sortier-Testfeld sowie "<"/">"-Paare für Frisur/Haarfarbe/Rüstung/Helm/Waffe/Schild - primärer Weg statt F-Tasten-Gedächtnis, F1-F9-Hotkeys bleiben zusätzlich bestehen. 172 Tests grün.
- M2a (Datenbank & Auth-Dienst) fertig, in 6 Commits: (1) docker-compose.yml (Postgres 16) plus neues Projekt Lumiis.Data (Account/Character-Entities, LumiisDbContext, DbConnectionOptions liest LUMIIS_DB_* mit klarem Abbruch bei Fehlern, UsernameRules), EF-Core-Tool lokal eingerichtet, erste Migration erzeugt und gegen echtes Postgres verifiziert. (2) PasswordHasher: PBKDF2-HMAC-SHA256, 600.000 Iterationen (pro Zeile gespeichert), Konstantzeit-Vergleich. (3) TokenService: eigenes signiertes Token-Format (kein JWT), trägt nur die AccountId, 30 Minuten gültig, injizierbare Uhr. (4) LookCatalog von Lumiis.Server nach Lumiis.Data verschoben (beide Dienste brauchen ihn), Verhalten unverändert. (5) Neuer Dienst Lumiis.Auth (ASP.NET Core Minimal API, nur HTTP, Port 5080) mit POST /register, POST /login, GET/POST /characters (siehe Abschnitt "Datenbank & Auth-Dienst"); `LookCatalog.CreateDefaultAppearance()` (erster gültiger Eintrag je Slot) für automatische Charakter-Aussehen; `EfCoreVersion` zentral in Directory.Build.props gepinnt (sonst echter Assembly-Versionskonflikt zwischen Lumiis.Data und Lumiis.Auth); Bug gefunden und behoben - `dotnet run --project server/src/Lumiis.Auth` setzt bei Web-SDK-Projekten das Arbeitsverzeichnis auf den Projektordner statt auf das des Aufrufers (launchSettings.json-Mechanismus der dotnet-run-CLI, deren eigenes "workingDirectory"-Feld dabei ignoriert wird), Programm fällt deshalb auf einen zweiten data-dir-Kandidaten zurück; manuell end-to-end mit echtem Postgres und curl verifiziert. (6) Endpunkt-Tests über WebApplicationFactory mit EF-Core-InMemory (ausdrücklich Logik-Tests, kein Beweis für Postgres-Verhalten); zwei DI/Namenskollisions-Bugs beim Umbauen gefunden und behoben (AddDbContext-Verkettung erfordert einen breiteren Removal-Sweep statt nur RemoveAll<DbContextOptions<T>>(); Lumiis.Server.Program kollidiert dank dessen [InternalsVisibleTo] mit Lumiis.Auth.Program, neuer `AuthEntryPointMarker` als eindeutiger WebApplicationFactory-Typ). 187 Tests grün, 5x stabil, ohne dass Postgres dafür laufen muss.
- M2a-Nachbesserungen (vom User beim ersten Testlauf gefundene Fehler): echter Root-Cause-Bug in `LumiisDbContextFactory` behoben - fing bei fehlenden `LUMIIS_DB_*` still auf einen abweichenden Dummy-Connection-String zurück statt den Fehler zu melden (Ursache für "password authentication failed" trotz korrektem `.env`); `docker-compose.yml` liest jetzt direkt `LUMIIS_DB_*` statt eigener `POSTGRES_*`-Namen (nur noch ein Variablensatz); neues `tools/dev-env.sh` lädt `.env` zentral in die Shell. Danach von Grund auf mit frischem Test-Passwort verifiziert (`docker compose down -v`, neues `.env`, vier curl-Aufrufe tatsächlich ausgeführt).
- M2b (Server-Join über Account/Token/Charakter) fertig, in 5 Commits (Punkt 0 Review-Fix, 1 Shared, 2 Server, 3 Positions-Speicherung, 4 Bot - Punkt 5 Tests bereits durch 2/3 abgedeckt, Punkt 6 diese Doku-Aktualisierung): (0) Review-Fix aus M2a: Charakternamen waren bereits global (nicht pro Account) eindeutig, aber nicht case-insensitiv - `LumiisDbContext` bekommt eine berechnete Kleinschreibungs-Schattenspalte `NameNormalized` mit Unique-Index, App-Check in `AuthEndpoints.CreateCharacter` vergleicht jetzt ebenfalls kleingeschrieben; NameRules-Validierung war schon vorhanden. (1) Shared: Protocol.Version 3, JoinRequestPacket trägt Token+CharacterId statt Name, JoinRejectReason sauber neu durchnummeriert. (2) Server liest LUMIIS_DB_*/LUMIIS_TOKEN_SECRET wie Auth; Join validiert das Token sofort synchron, lädt den Charakter (Eigentümerschaft geprüft) asynchron auf einem Threadpool-Thread und sammelt das Ergebnis einmal pro Tick ein (World bleibt dem Game-Loop-Thread vorbehalten); World.TryJoin um CharacterId/Position erweitert, eigene Namens-Eindeutigkeitsprüfung entfällt (jetzt DB-Sache, siehe Punkt 0); Appearance kommt aus der Character-Zeile, FNV-1a-Hash-Mechanismus (`LookCatalog.CreateAppearanceForName`, `Fnv1a.cs`) komplett entfernt. (3) Positions-Speicherung: bounded Channel (Kapazität=MaxPlayers, DropOldest) + Hintergrund-Task, alle 30s (fix) sowie sofort bei Disconnect. (4) Bot bekommt einen echten HTTP-Login-Flow (`AuthClient`: Register-oder-Login, Charakter finden/anlegen) vor dem LiteNetLib-Connect. Unity-Client wird bewusst NICHT angefasst (kompiliert seither nicht mehr gegen Lumiis.Shared, siehe "Bekannte Lücken" - Aufgabe für M2c). 197 Tests grün, 5x stabil, weiterhin ohne laufendes Postgres.
- M2c (Unity-Login und Charakterwahl, UI Toolkit) fertig, in 5 Commits: (0) Kontrakt-Fix zuerst - GET /characters lieferte ein rohes JSON-Array, Unitys JsonUtility kann Top-Level-Arrays nicht parsen; liefert jetzt { "characters": [...] } (neuer `CharacterListResponse`), Lumiis.Bot (nutzt denselben Endpunkt) mitgezogen, neuer Vertragstest. (1) `client/Assets/Resources/UI/`: `lumiis.uss` (minimale, funktionale Gestaltung), `DefaultRuntimeTheme.tss` (Basis-Theme per `@import url("unity-theme://default")`, als reine Textdatei angelegt - kein Editor nötig), `Login.uxml`, `CharacterSelect.uxml`. (2) Neuer `LoginController` (eigenes UIDocument, code-first): Zustände LoggedOut -> LoggingIn -> CharacterSelect -> Connecting -> InGame -> (Disconnect) zurück zu LoggedOut; HTTP gegen Lumiis.Auth über UnityWebRequest + TaskCompletionSource (bewusst nicht UnityEngine.Awaitable, siehe Klassenkommentar); clientseitige Vorprüfung vor jedem Aufruf, verbindlich bleibt der Server; alle sechs JoinRejectReason-Werte plus HTTP-Fehler/Netzwerkfehler als deutscher Text im UI; letzter Benutzername per PlayerPrefs, Token nur im Speicher. `NetClient.Connect` nimmt Token+CharacterId, `GameClient.PrepareJoin(name)` neu (der Server schickt den eigenen Namen nie zurück). (3) Alte IMGUI-Debug-Login-Box (freies Namensfeld, direkter Connect) komplett entfernt, `UiState`-Enum durch ein einfaches `_inGame`-Flag ersetzt. (4) Kein neues Unity-Testautomatisierung-Setup (UI Toolkit/UnityWebRequest wirkt erst im Play-Modus) - stattdessen eine konkrete Checkliste ans Ende von CLAUDE.md für den manuellen Test durch den User. (5) Diese Doku-Aktualisierung. 198 Tests grün (unverändert seit Abschnitt 0), Batchmode-Compile-Check am Ende: 0 Fehler - komplette Solution und Unity-Projekt kompilieren wieder zusammen fehlerfrei. M2 (Account & Charakter) damit komplett fertig.
- M2c-Fixes (vom User beim ersten echten Play-Test gefundene Laufzeitfehler, alle bestanden den Batchmode-Compile-Check trotzdem), in 8 Commits: (1) `LoginController.BuildUi()` griff auf `_uiDocument.rootVisualElement` zu, bevor das GameObject aktiv war (`UIDocument.OnEnable()` läuft nur bei aktivem GameObject) - NullReferenceException beim ersten Play-Test; `go.SetActive(true)` vor den Zugriff verschoben. (2) Boden-Sprite (`PlaceholderSprites.CreateCheckerTile`) explizit mit `SpriteMeshType.FullRect` erzeugt (drawMode Tiled verlangt das, sonst Laufzeit-Warnung). (3) `DefaultRuntimeTheme.tss` an eine echte, im Unity-Installationsverzeichnis gefundene Referenzdatei angeglichen (fehlende `VisualElement {}`-Zeile nach dem `@import`) - Verdacht auf unvollständig aufgelöste Default-Theme-Regeln, nicht abschließend laborativ bewiesen. (4) `BuildUi()` versteckt die CharacterSelect-Wurzel jetzt selbst sofort (Härtung, kein tatsächlich beobachtbarer Bug bei der bestehenden Aufrufreihenfolge). (5) UI-Aufbau um einen Frame verzögert (Coroutine) - `Initialize()` lief über `RuntimeInitializeOnLoadMethod(AfterSceneLoad)` vor dem ersten echten Frame, UI-Toolkit-Panel-Größe (Layout UND Klick-Trefferbereich) wurde dabei einmalig aus einer noch nicht eingeschwungenen `Screen.width/height` berechnet - Buttons kaum klickbar, TextFields kollabiert, ohne jede Fehlermeldung. (6) Eingabefelder/Buttons in `lumiis.uss` explizit dimensioniert (min-height/padding/font-size) statt sich auf Default-Theme-Maße zu verlassen. (7) Echter Bug: `ShowLogin()` löschte am Ende immer die Fehlermeldung (`SetLoginError("")`); alle sechs Fehlerpfade in Login/Registrierung setzten die Meldung aber davor, sie wurde also sofort wieder überschrieben (z. B. "Benutzername bereits vergeben." nie sichtbar) - Reihenfolge getauscht. (8) Charakterliste (ScrollView) ohne feste Höhe/Overflow-Schutz konnte visuell in die "Neu"-Zeile hineinlaufen - jetzt feste Box mit `overflow: hidden`.
- Client-Testinfrastruktur (docs/tasks/client-test-infrastructure.md), Abschnitte 1-2 fertig, in 2 Commits (Anlass: alle M2c-Fixes oben bestanden den Batchmode-Compile-Check trotzdem, reine Laufzeitfehler): (1) `LoginFlow.cs`/`IAuthClient.cs` (neu, UnityEngine-frei, Compile-Include in `Lumiis.Tests.csproj` analog zu `ClientWorld.cs`) - die komplette Login/Registrierung/Charakterauswahl-Zustandslogik aus `LoginController` herausgezogen; `UnityWebRequestAuthClient` als einzige echte HTTP-Implementierung (dünn); `LoginController` auf reine UI-Verdrahtung reduziert (`ApplyLoginFlowState()` als einziger Ort, der LoginFlow.State/-ErrorMessage liest - macht den M2c-Fix-7-Fehler strukturell unmöglich); 25 neue xUnit-Tests über eine `FakeAuthClient`. (2) Architektur-Rücksprache nötig: `client/Assets/Lumiis/Scripts/` bekommt ein eigenes `Lumiis.Client.asmdef` (ein Assembly, keine weitere Aufteilung) - ohne eigene asmdef kompiliert Hauptcode in `Assembly-CSharp`, die aber nach allen asmdef-Assemblies kompiliert wird, ein PlayMode-Test-Assembly kann sie deshalb grundsätzlich nicht referenzieren (`Unity.InputSystem` musste dabei explizit in die References, Plugin-DLLs/Engine-Module werden automatisch aufgelöst); neues `client/Assets/Lumiis/Tests/PlayMode/` (Assembly `Lumiis.Tests.PlayMode`) mit 4 `[UnityTest]`-Coroutine-Tests (username-field sichtbar, höchstens eine UI-Wurzel gleichzeitig sichtbar, Validierungstext ohne Exception, erfolgreicher Login wechselt sichtbar zur Charakterauswahl); `tools/test-client-playmode.sh`. Tatsächlich mit einem absichtlich wieder eingebauten Bug (M2c-Fix 1, SetActive-Reihenfolge) verifiziert: alle 4 Tests liefen mit der exakt gleichen NullReferenceException rot, nach Rückgängigmachen wieder grün (Exit-Code 0/2 jeweils verifiziert, nicht nur behauptet). 223 Tests grün (`dotnet test`), 4 PlayMode-Tests grün. (3) Doku: Teststrategie-Abschnitt, Konvention, Status nachgetragen.
- M3a (Kartenkollision, docs/tasks/M3a-collision.md) fertig, in 4 Commits: (1) `tools/bake-collision.py` (Pillow) erkennt klar rote Pixel als blockiert, schreibt eine gepackte 1-Bit-pro-Zelle-Bitmaske (Magic "LCM1" + Breite/Höhe im Header) nach `collision.bin`; `data/maps/village/collision-source.png` (1000x1000-Platzhalter: Rand blockiert, ein Rechteck "Haus", eine Ellipse "Teich") gebacken und mitcommitet. (2) `CollisionMap` (neu, `Lumiis.Data.Maps`, keine EF-Core-Kopplung, analog zu `LookCatalog`) lädt/validiert `collision.bin` fail-fast; `World.SetDestination` klemmt wie bisher auf die Kartengrenzen und schreitet danach die Linie zum Ziel zellenweise per Bresenham ab, stoppt an der letzten begehbaren Zelle vor einer blockierten (freie Bewegung bleibt exakt wie zuvor, kein Zellen-Snapping); `CollisionMap` wird injiziert (optional, `null` = überall begehbar), `World` bleibt frei von Dateizugriffen; `GameServer` bekam dafür einen neuen optionalen Konstruktorparameter, `Lumiis.Server/Program.cs` bekam `--data-dir` zurück (vor M2b schon einmal da, für looks.json, mit M2b entfernt - jetzt für die Kollisionskarte wieder nötig) und lädt `data/maps/village/collision.bin` beim Start fail-fast; manuell verifiziert (Server startet mit der echten Datei). (3) 14 neue Tests: `CollisionMapTests` (Loader-Rundtrip, Grenzprüfung, kaputte Header - Bitmaske direkt im Test aus Bytes), `WorldTests` (freie Bewegung unverändert, Wand-Stop, Wand-parallel unbeeinflusst, Klemmen-vor-Kollisionsprüfung-Reihenfolge, blockierte Startzelle - synthetische CollisionMaps), ein Integrationstest mit der echten village-Karte (Bot läuft in den Teich-Platzhalter, EntityStop kommt nachweislich auf einer begehbaren Zelle an) - dabei eine enge, vorbestehende Randfall-Eigenheit gefunden und dokumentiert (`CollisionMap.cs`-Kommentar: `Math.Clamp` erlaubt als Ziel exakt `GameConstants.MapWidth/Height` selbst, das entspricht einer Zelle außerhalb einer genau `MapWidth/100` Zellen breiten Karte - bei der echten village-Karte unsichtbar, weil der Rand ohnehin bemalt blockiert ist). (4) Diese Doku-Aktualisierung (Abschnitt "Kartenkollision", Meilensteine/Status). 237 Tests grün, 5x stabil. Danach zwei kleine Nachbesserungen: ein vom User gefundener Pfad-Auflösungsfehler (Server startete nicht mehr, wenn `dotnet run` mit Arbeitsverzeichnis `server/` statt Repo-Root aufgerufen wurde - `Lumiis.Server/Program.cs` bekam ein eigenes `ResolveDefaultDataDir()`, analog zu `Lumiis.Auth`, aber mit anderen Pfad-Kandidaten, siehe Kommentar dort) sowie ein Doku-Hinweis, dass beide Implementierungen unabhängig sind und bei künftigen data-dir-Bugs beide geprüft werden müssen.
- M3b (Sichtbarkeitsbereich/AOI, docs/tasks/M3b-aoi.md) fertig, in 6 Commits (0 Review nur berichtet, kein Commit): (1) `GameConstants.AoiRadiusWire` = 1500 (15 Weltenheiten). (2) `World.ComputeVisibility(radiusWire)`: für jede Entity die Menge der anderen innerhalb des Radius, quadrierte Distanz mit `long`-Arithmetik, symmetrisch (jedes Paar nur einmal verglichen), Grenzfall "Distanz == Radius" bewusst inklusiv, kein Netzwerkbezug. (3) `GameServer`: `ConnectionState.VisibleEntityIds`, neue `UpdateVisibility()` einmal pro Tick - Differenz zum vorherigen Stand, neu sichtbar → EntitySpawn (+ EntityMove bei laufender Bewegung, ab aktueller Position), nicht mehr sichtbar → EntityDespawn. (4) Bestehende Broadcasts umgestellt: `BroadcastToJoined` (sendete an alle) komplett durch `SendToVisible(entityId, packet)` ersetzt (nur an Verbindungen, die die Entity sehen, plus immer an die Entity selbst - keine Client-Prediction, der Client rendert seine eigene Figur genauso über EntityMove/EntityStop); `CompleteJoin` schickt dem Joiner nur noch die laut Radius sichtbaren Entities (vorher alle) und initialisiert `VisibleEntityIds` sofort mit; der Extra-Broadcast "Joiner an alle anderen" ersatzlos gestrichen - andere Spieler sehen den Joiner automatisch über den nächsten Tick-Diff, kein Sonderfall nötig (wie in der Aufgabenstellung vermutet, im Bericht bestätigt); `OnPeerDisconnected` nutzt `HashSet.Remove` für Filtern+Aufräumen in einem Aufruf. (5) 11 neue Tests (6 Unit für `ComputeVisibility`, 5 Integration mit echtem LiteNetLib) - dabei einen echten Test-Bug gefunden: `CharacterId` (DB) und `EntityId` (World-intern) sind unabhängige Zähler, die nur zufällig übereinstimmen, je nach asynchroner Join-Reihenfolge; führte zu zwei hartnäckig scheinbar hängenden Tests (bis 15s Timeout ausprobiert), tatsächlich aber falsch verglichenen IDs - Fix: EntityId immer aus dem jeweiligen `JoinAcceptedPacket` lesen. (6) Diese Doku-Aktualisierung (Abschnitt "Sichtbarkeitsbereich (AOI)", TODO(M3)-Kommentare entfernt, Meilensteine/Status). Bewusst kein Grid/Quadtree - O(n²/2) reicht für unsere Spielerzahl, siehe Doku-Abschnitt. 248 Tests grün, 5x stabil.
- M3c (Monster, docs/tasks/M3c-monsters.md) fertig, in 7 Commits (0 Review nur berichtet, kein Commit; 5 AOI/Versand nur bestätigt, kein Commit - siehe Bericht): (1) `data/mobs.json` (Wildhund/Waldschrat) und `data/maps/village/spawns.json` (5 Spawn-Punkte, Positionen gegen collision-source.png verifiziert begehbar). (2) Shared: `EntityKind` enum (Player/Monster), `EntitySpawnPacket` um `Kind`/`MobId` erweitert (Appearance bleibt für beide Kinds im Paket, bei Monstern ohne Bedeutung), Protocol.Version 3->4. (3) `MobCatalog`/`SpawnCatalog` (`Lumiis.Data.Mobs`, analog zu LookCatalog/CollisionMap) laden/validieren fail-fast; `Entity.CharacterId` jetzt `int?` (nur bei Kind=Player), neue `Kind`/`MobId`/`WanderAnchorX/Y`-Felder, Player-Konstruktor unverändert (delegiert intern), neue statische `Entity.CreateMonster(...)` (vermeidet eine sonst mehrdeutige Konstruktor-Überladung); `World.SpawnMonster(...)`, GameServer legt beim Start für jeden Spawn-Punkt eine Monster-Entity an; Positions-Speicherung auf `CharacterId.HasValue` eingeschränkt (Monster haben keine DB-Zeile - der in Abschnitt 0a vermutete Fund). (4) `World` bekommt eine injizierbare Random-Quelle; `World.Tick(deltaSeconds, nowMs)` läuft nach der Bewegungsfortschreibung `RunWanderAi` für fällige Monster (zufälliger Punkt im `WanderRadiusWire` um den Anker, blockiertes Ziel wird übersprungen statt Retry-Loop, nächste Fälligkeit immer neu bestimmt) und gibt zusätzlich zurück, welche Monster ein neues Ziel bekommen haben (für EntityMove, analog zu Spieler-MoveTo); dabei einen echten Fund behoben - `SetDestination` nutzte hartkodiert `GameConstants.PlayerSpeed` für JEDE Entity, neues `Entity.MovementSpeed` (bei Monstern aus `SpeedWire`) behebt das. (5) Bestätigt: der M3b-AOI-Code behandelt Monster ohne Änderung korrekt (rein positions-/id-basiert), kein Fix nötig, kein eigener Commit. (6) Client: neue, sehr einfache `MonsterView` (Kreis/Dreieck per MobId, `PlaceholderSprites.CreateCircle/CreateTriangle`), `ClientWorld`/`EntitySpawnPacket`-Handling um Kind/MobId erweitert, Debug-Liste zeigt "[Spieler]"/"[Monster]"; Batchmode-Compile-Check 0 Fehler. (7) 15 neue Tests (MobCatalog/SpawnCatalog-Validierung, Wander-KI mit injizierbarer Zeit/Zufallsquelle, 1 Integrationstest mit echtem LiteNetLib - Bot sieht Monster-Spawn und dessen spätere Wander-Bewegung). (8) Diese Doku-Aktualisierung (Abschnitt "Monster", Meilensteine/Status - M3 damit komplett fertig, M4 offen). 264 Tests grün, 5x stabil.
- M4a (Kampf-Grundlagen, docs/tasks/M4a-combat-core.md) fertig, in 7 Commits (0 Review nur berichtet, kein Commit): (1) `data/classes.json` (Krieger) neu, `data/mobs.json` um MaxHp/Attack/Defense/YangMin/YangMax erweitert - reine Datendateien, `MobCatalog` liest die neuen Felder noch nicht. (2) `Character.Yang` (long, Default 0), Migration `AddCharacterYang` gegen echtes Postgres verifiziert; `Lumiis.Auth` unverändert (Yang startet automatisch bei 0). (3) Shared: `AttackRequestPacket`, `EntityDamagedPacket`/`EntityDiedPacket`/`AttackFailedPacket` (Reason-Enum)/`YangChangedPacket`, Protocol.Version 5, Round-trip-Tests. (4) `ClassCatalog` (`Lumiis.Data.Classes`, analog zu MobCatalog) neu; `MobCatalog` um die Kampfwerte erweitert und validiert (bestehende Test-Fixtures entsprechend ergänzt); `Entity` bekommt CurrentHp/MaxHp/Attack/Defense/MeleeRangeWire/AttackCooldownMs/AttackReadyAtMs/RespawnAtMs/YangMin/YangMax/Yang sowie `Teleport()`/`StopMoving()`, neue `Entity.DefaultXxx`-Konstanten als Fallback für kampfferne Tests (TryJoin/SpawnMonster/CreateMonster bekommen optionale Kampfwert-Parameter, bestehende Aufrufe bleiben unverändert kompatibel); `World.TryAttack`/`World.ApplyDamage` (siehe Abschnitt "Kampf" für die Prüfreihenfolge/Formel), `World.Tick` verarbeitet fällige Spieler-Respawns und liefert sie als dritte Tupel-Liste zurück. (5) `GameServer.HandleAttackRequest` verdrahtet `World.TryAttack` mit den Paketen; `BroadcastDespawn` aus `OnPeerDisconnected` extrahiert und für Monstertod wiederverwendet (Abschnitt-0-Fund: der bestehende Despawn-Mechanismus war schon kind-agnostisch, keine "Monster despawnen nie"-Annahme zu brechen); neuer, eigener Yang-Save-Channel (gleiches Bounded/DropOldest-Muster wie Positionen, aber sofortiger statt periodischer Auslöser); Spieler-Respawn nutzt `EntityStopPacket` (kein neues Paket nötig); `CompleteJoin` löst die Klasse über `ClassCatalog` auf (Alpha: nur "Krieger") und lädt `Character.Yang` in die Entity; `Program.cs` lädt `data/classes.json` fail-fast. (6) Client: `NetClient.SendAttack`, `MonsterView.ContainsWorldPoint` (Bounds-Check statt Collider-System) - Rechtsklick auf ein sichtbares Monster; `ClientWorld` bekommt `CurrentHp`/`MaxHp` auf `ClientEntity` (-1 = unbekannt, reaktiv über das erste EntityDamaged) und `HandleEntityDamaged`/`HandleEntityDied`; neue Debug-Overlay-Zeile (eigenes Leben, anvisiertes Ziel, Yang, deutsche AttackFailed-Kurzmeldung bzw. Todesmeldung - erkannt am ersten EntityStop für die eigene Entity nach EntityDied, kein eigenes Respawn-Paket). Keine neue Grafik, keine echten Health-Bars. (7) `ClassCatalogTests`, `WorldCombatTests` (Schaden/Mindestschaden/alle fünf Ablehnungsgründe einzeln inkl. Beleg der Prüfreihenfolge "Ziel lebt" vor "kein Spieler"/Monstertod+Yang/Spielertod+Respawn), ein Integrationstest (echtes LiteNetLib: Angreifer tötet ein per SpawnCatalog geladenes Monster in mehreren Treffern, EntityDamaged bei Angreifer UND einem zusätzlichen Beobachter, EntityDied dann EntityDespawn bei beiden, YangChanged nur beim Angreifer). (8) Diese Doku-Aktualisierung. 294 Tests grün, 3x stabil, Batchmode-Compile-Check 0 Fehler. Manuell mit echtem Auth+Server+Bot noch zu verifizieren (siehe Checkliste unten).
- Dev-Tools (docs/tasks/dev-tools-debug-spawn.md, kein eigener Meilenstein) fertig, in 4 Commits: (1) 4 zusätzliche Spawn-Punkte (Ids 6-9, Mix Wildhund/Waldschrat) 300-500 Wire-Einheiten um die Kartenmitte, gegen collision-source.png/echten Serverstart als begehbar verifiziert. (2) `ServerConfig.AllowDebugCommands` (`--allow-debug-commands`, Default aus); Shared `DebugSpawnMonsterRequestPacket`, Protocol.Version 6; `GameServer.HandleDebugSpawnMonster` verwirft das Paket ohne die Option still (kein Ablehnungspaket, nur Log - Dev-Only-Befehl soll "wie nicht vorhanden" wirken), spawnt sonst über `World.FindNearbyWalkablePoint` (neu: zufälliger 100-300-Wire-Versatz, Rückfall auf Absenderposition bei blockierter Zielzelle) ein Monster nahe dem Absender; `Entity.SpawnPointId` (neu, nullable) unterscheidet Katalog-Spawns (echte SpawnPoint.Id) von Debug-Spawns (null) - Vorbereitung für M4b, keine Kollision mit künftigem Respawn-pro-SpawnPoint; Client-Debug-Panel bekommt einen "Monster spawnen"-Bereich (Mob-Typ-Cycler aus einer fest hinterlegten kleinen Liste, keine neue mobs.json-Ladefunktion im Client); CLAUDE.md-Befehle-Zeile um den Flag ergänzt. (3) 7 neue Tests (SpawnCatalog lädt die echte erweiterte spawns.json; SpawnMonster setzt/lässt SpawnPointId korrekt; FindNearbyWalkablePoint im Bereich bzw. Rückfall bei Vollblockade; 2 Integrationstests - ohne Option keine Wirkung, mit Option spawnt und ist normal angreifbar). (4) Diese Doku-Aktualisierung (neuer Abschnitt "Dev-Tools"). 302 Tests grün, 5x stabil, Batchmode-Compile-Check 0 Fehler.
- M4b (Monster-Respawn, docs/tasks/M4b-monster-respawn.md) fertig, in 4 Commits (0 Review nur berichtet, kein Commit): (1) `Entity.RespawnSeconds` (nur bei Kind=Monster mit gesetzter SpawnPointId aussagekräftig, aus SpawnPoint.RespawnSeconds - wird bei jedem Respawn an die neue Entity weitergereicht, damit der Zyklus weiterläuft); `World.ApplyDamage` terminiert bei Monstertod MIT SpawnPointId zusätzlich zum bisherigen sofortigen Entfernen einen Respawn (neues privates `ScheduleMonsterRespawn` friert MobId/Name/Position/Kampfwerte/RespawnSeconds als "Rezept" in einem neuen `Dictionary<SpawnPointId, PendingMonsterRespawn>` ein - höchstens ein Eintrag pro SpawnPoint, da nie gleichzeitig lebende Entity UND ausstehender Respawn); `World.Tick` erzeugt bei Fälligkeit über den normalen `SpawnMonster`-Weg eine NEUE Entity mit neuer EntityId (nicht die alte wiederbelebt, anders als beim Spieler-Respawn - die alte wurde beim Tod schon entfernt, der Client hat längst EntityDespawn bekommen), vierte Tupel-Liste `RespawnedMonsters`. Debug-gespawnte Monster (SpawnPointId == null) unverändert ohne Respawn. (2) `GameServer`-Konstruktor reicht `spawn.RespawnSeconds` jetzt durch (vorher immer 0/unbenutzt); RunTick verarbeitet die neue Liste - kein eigener Broadcast nötig (bestätigt: derselbe automatische AOI-Diff-Mechanismus wie bei jeder neu erschienenen Entity, kein Sonderfall), nur eine Log-Zeile ergänzt. (3) 7 neue Tests (3 Unit: Spawn-Punkt-Monster respawnt exakt nach RespawnSeconds mit neuer EntityId/voller Leben/gleicher SpawnPointId, Debug-Monster respawnt nie, zwei SpawnPoints respawnen unabhängig mit eigenen RespawnSeconds-Werten; 1 Integrationstest: Bot tötet ein Spawn-Punkt-Monster mit kurzer synthetischer RespawnSeconds=2, bekommt nach der Wartezeit EntitySpawn für die neue Entity an derselben Position). (4) Diese Doku-Aktualisierung (Abschnitte "Monster"/"Kampf" um Respawn-Verhalten ergänzt, Meilensteine/Status - M4 damit komplett fertig, M5 offen). 306 Tests grün, 5x stabil.
- M4c (Monster-Aggro/Verfolgung/Leine, docs/tasks/M4c-monster-aggro.md) fertig, in 7 Commits (0 Review nur berichtet, kein Commit): (1) 6 zusätzliche Spawn-Punkte (Ids 10-15) 200-400 Wire-Einheiten um die Kartenmitte. (2) `data/mobs.json` um MeleeRangeWire/AttackCooldownMs (analog classes.json, Monster hatten bisher hartverdrahtet 0/0 - konnten nie angreifen) sowie Aggressive/AggroRangeWire/LeashRangeWire erweitert; Wildhund aggressiv (kleiner AggroRangeWire, etwas größere Leine, kurzer Cooldown/kurze Reichweite), Waldschrat passiv (AggroRangeWire unbenutzt, etwas kürzere Leine, längerer Cooldown/etwas größere Reichweite). (3) `MobCatalog` liest/validiert die neuen Felder; `Entity` bekommt Aggressive/AggroRangeWire/LeashRangeWire sowie `TargetEntityId` (`int?`, dasselbe Nullable-Feld-Muster wie SpawnPointId/CharacterId - Abschnitt-0d-Empfehlung, kein paralleler Zustandscontainer); `CreateMonster`/`SpawnMonster` bekommen echte MeleeRangeWire/AttackCooldownMs; `PendingMonsterRespawn` (M4b) friert die neuen Werte für respawnte Monster mit ein. `World.TryAttack` verallgemeinert (Abschnitt-0a-Empfehlung): "Ziel ist Spieler" durch "Angreifer und Ziel gleichen Kinds" ersetzt (blockt weiterhin Spieler-vs-Spieler, erlaubt jetzt Monster-vs-Spieler), `AttackFailedReason.TargetIsPlayer` unverändert (bleibt für den einzig erreichbaren Fall korrekt). Neues `World.RunMonsterAi` (ersetzt die reine Wander-Prüfung): Zielsuche für aggressive Mobs ohne Ziel (`FindNearestLivingPlayerWithinRange`), mit Ziel Existenz-/Lebend-Prüfung, Leine (Distanz vom Anker zum AKTUELLEN Monster-Standort), Nahkampf-Angriff via TryAttack oder Verfolgung (Bewegungsziel jeden Tick neu auf die aktuelle Spielerposition, wie gefordert) - pausiert die normale Wander-Bewegung, solange ein Ziel aktiv ist; `Tick` liefert zusätzlich `MonsterAttacks` zurück. (4) `World.TryAttack`: getroffenes Monster ohne Ziel merkt sich reaktiv den Angreifer (gilt für aggressive UND passive Mobs). (5) `GameServer`-Konstruktor reicht die neuen mobDef-Werte jetzt durch; Paketversand aus `HandleAttackRequest` in neues, gemeinsames `BroadcastAttackResult` extrahiert (ohne Yang-Handling, das bleibt bei `HandleAttackRequest` - Monster als Angreifer haben keine Verbindung und YangAwarded ist bei ihnen immer 0); `RunTick` verarbeitet `MonsterAttacks` über denselben Helfer. (6) 10 neue Unit-Tests (`WorldMonsterAiTests` - Aggro-Erfassung/-Ignoranz, Passivität, reaktive Zielerfassung, Cooldown-Rhythmus im Nahkampf, Verfolgung außerhalb Nahkampf, Leinen-Rückkehr, Zielverlust bei Tod/Trennung, Spielertod nutzt bestehenden M4a-Respawn) plus 2 Integrationstests (Bot bleibt neben aggressivem Wildhund stehen und wird angegriffen, ohne selbst anzugreifen; Bot trifft passiven Waldschrat einmal und wird danach angegriffen, ohne erneut anzugreifen). (7) Diese Doku-Aktualisierung (Abschnitt "Kampf" um Aggro/Verfolgung/Leine ergänzt, veraltete Protocol.Version-Angabe korrigiert, neue Bekannte-Lücke zur Verfolgungs-Paketfrequenz, Meilensteine/Status - M4 komplett inkl. Gegenwehr fertig, M5 offen). 318 Tests grün, 5x stabil.
- M5a (Items/Inventar/Ausrüstung/Drops, docs/tasks/M5a-items-equipment.md) fertig, in 8 Commits (0 Review nur berichtet, kein Commit): (1) `data/items.json` (Holzschwert als Startwaffe, zwei weitere Waffen, Rüstung/Helm/Schild, Heiltrank), `data/mobs.json` um eine `drops`-Liste je Mob-Typ erweitert. (2) Migration `ReplaceCharacterEquipmentWithItems`: `Character.ArmorId/HelmetId/WeaponId/ShieldId` (LookIds) werden zu `EquippedArmorItemId/HelmetItemId/WeaponItemId/ShieldItemId` (nullable Item-IDs, bestehende Werte NULL-zurückgesetzt - andere Wertspanne, keine Alpha-Spielerdaten zu erhalten); neue Tabelle `CharacterInventoryItem` (SlotIndex 0-19, Unique-Index mit CharacterId); `Lumiis.Auth` weist bei Charaktererstellung kein Armor/Helmet/Shield mehr automatisch zu, nur die feste Startwaffe (Item-Id 1). (3) Shared: `EquipItemRequestPacket`/`UnequipItemRequestPacket`/`UseConsumableRequestPacket`, `InventorySyncPacket`/`InventoryActionFailedPacket`, Protocol.Version 7. (4) `ItemCatalog` (`Lumiis.Data.Items`, analog zu MobCatalog/ClassCatalog, braucht `LookCatalog` zur Validierung); `Entity` bekommt `Inventory`(int[GameConstants.InventorySize])/EquippedXItemId/BaseAttack/BaseDefense (Attack/Defense jetzt mutable); `World` hält `ItemCatalog` wie `CollisionMap` (laufende Abfragen statt Einmal-Auflösung); `TryEquip/TryUnequip/TryUseConsumable/TryAddItemToInventory/RecalculateCombatStats` neu, kein Netzwerkbezug. (5) Drops: `World.TryAttack` würfelt nach dem Yang-Gewinn unabhängig über die Drops-Liste des toten Mob-Typs (deterministisch bei ChancePercent 0/100, kein Random-Seed für diese Fälle nötig), legt Treffer per `TryAddItemToInventory` ab - volles Inventar verwirft den Drop ohne den Kill ungültig zu machen; `AttackResult` trägt jetzt DroppedItemIds/ItemLostToFullInventory, `Entity.Drops`/`MobDefinition.Drops` reichen die Werte durch (auch durch den M4b-Respawn-Zyklus). (6) `ItemCatalog` in `Program.cs`/`GameServer` verdrahtet (Lumiis.Server lädt looks.json seit M5a zusätzlich, nur zur Validierung); `GameServer.ToAppearance` löst Ausrüstung jetzt über ItemCatalog zu LookIds auf (Wire-Format unverändert); `CompleteJoin` lädt Ausrüstung+Inventar aus der DB, berechnet Attack/Defense einmalig neu, verschickt InventorySync zusätzlich zu JoinAccepted; neue Equip/Unequip/UseConsumable-Handler (Erfolg: InventorySync an den Anfragenden, Equip/Unequip zusätzlich ein neues `AppearanceChangedPacket` an alle AOI-Beobachter statt EntityDespawn+EntitySpawn - PacketId 1012, Protocol.Version 8); eigener `InventorySaveRequest`-Channel (wie Yang: voller Snapshot, sofortiger Auslöser) speichert Inventar+Ausrüstung. (7) Client: `ItemCatalogClient` (lädt items.json aus StreamingAssets, von Lumiis.Shared.csproj jetzt mitkopiert) nur für Anzeigenamen/ItemType; Debug-Panel um "Ausrüstung" (4 Slots+Ablegen) und "Inventar" (20 Zeilen, Klick legt an oder benutzt, je ItemType) erweitert, jetzt in einer ScrollView; kurze InventoryActionFailed-Meldung im Kampf-Overlay. (8) `ItemCatalogTests`, `MobCatalogTests` um Drops erweitert, `WorldInventoryTests` (Equip/Unequip/UseConsumable/TryAddItemToInventory/Drop-Logik inkl. Inventar-voll-Fall), ein Integrationstest (echtes LiteNetLib: garantierter Drop, Ausrüsten, messbar höherer Schaden im nächsten Treffer). (9) Diese Doku-Aktualisierung (neuer Abschnitt "Items und Inventar", Meilensteine/Status, neue Bekannte-Lücke zum fehlenden HP-Sync bei Heiltrank-Nutzung). 357 Tests grün, mehrfach stabil, Batchmode-Compile-Check 0 Fehler. Manuell mit echtem Postgres+Auth+Server+Bot verifiziert: ein neu registrierter Charakter joint mit aufgelöster Startwaffe (JoinAccepted zeigt WeaponId=2, die LookId des Holzschwerts, statt der ItemId 1); Drop/Ausrüsten/Schadenserhöhung sind zusätzlich über den echten LiteNetLib-Integrationstest aus Abschnitt 8 abgedeckt (Bot selbst kann nicht kämpfen/ausrüsten, kein Kampf-Feature).

- Bugfix HP-Anzeige (vom User nach einem echten Unity-Test gemeldet: Heiltrank benutzt, HP-Anzeige blieb auf dem alten Wert stehen), in 4 Commits: Audit ergab drei Lücken ohne Paket für eine HP-Änderung ohne Treffer - Heiltrank (`World.TryUseConsumable`), Spieler-Respawn (`World.Tick`) und eine bereits verletzte Entity, die per `EntitySpawnPacket` (trägt kein Leben) neu in die AOI eines Beobachters eintritt (Join UND laufender Sichtbarkeits-Diff). `ApplyDamage` (Schaden) und die Entity-Erzeugung selbst (immer volles Leben) hatten dagegen keine Lücke. (1) Neues `EntityHpChangedPacket` (EntityId, CurrentHp, MaxHp), Protocol.Version 8 -> 9; `EntityDamagedPacket` bleibt für den Schadensfall unverändert. (2) `GameServer.SendHpIfDamaged` direkt nach jedem `EntitySpawnPacket` für eine fremde Entity (CompleteJoin + UpdateVisibility); Heiltrank-Erfolg und Spieler-Respawn broadcasten das neue Paket zusätzlich zum jeweils bestehenden Paket. (3) Client: `ClientWorld.ApplyHp` ist jetzt der einzige Schreibpfad für `CurrentHp`/`MaxHp` - `HandleEntityDamaged` UND das neue `HandleEntityHpChanged` rufen beide dieselbe Methode auf, kein zweiter Codepfad. (4) Tests (3 `ClientWorldTests` + 3 LiteNetLib-Integrationstests, Respawn-Test nutzt eine fake Uhr statt echt 3s zu warten, analog zu JoinTimeout/RejectGrace) - dabei einen zweiten, unabhängigen Bug gefunden: `LookupCharacterForJoinAsync` lud `Character.InventoryItems` ohne `Include(...)`, das gespeicherte Inventar ging bei jedem Login still verloren (EquippedXItemId-Spalten waren davon nicht betroffen, nur die separate Tabelle) - mitbehoben. 364 Tests grün, mehrfach stabil.
- M5b (Shop/NPCs, docs/tasks/M5b-shop.md) fertig, in 7 Commits (0 Review nur berichtet, kein Commit): (1) `data/items.json` um BuyPrice/SellPrice erweitert (Holzschwert nur Verkaufspreis, Rest nach Kampfwert-Bonus gestaffelt); `data/npcs.json` (Platzhalter-Händler "Dorfhaendler") und `data/maps/village/npcs.json` (eine Platzierung 500 Wire-Einheiten südlich der Kartenmitte). (2) Shared: `EntityKind.Npc`, `EntitySpawnPacket.MobId` zu `TypeId` umbenannt (0b: reiner Wire-Rename, `Entity.MobId`/`ClientEntity.MobId` mitgezogen, `SpawnPoint.MobId`/`DebugSpawnMonsterRequestPacket.MobId` bleiben unverändert - monster-spezifisch); neue Pakete `OpenShopRequestPacket`/`BuyItemRequestPacket`/`SellItemRequestPacket`/`ShopContentPacket`/`ShopActionFailedPacket`/`DebugGiveYangRequestPacket`; `AttackFailedReason.TargetNotAttackable` (0a); `GameConstants.NpcInteractRangeWire`/`MaxYang`; Protocol.Version 9 -> 10. (3) `ItemCatalog` um BuyPrice/SellPrice erweitert; neue `NpcCatalog`/`NpcSpawnCatalog` (`Lumiis.Data.Npcs`, analog zu MobCatalog/SpawnCatalog) - jede ShopItemId muss in ItemCatalog existieren UND BuyPrice > 0 haben; `Entity.CreateNpc`/`World.SpawnNpc` (keine Bewegung, keine SpawnPointId/CharacterId, Kampfwerte auf Default-Konstanten, nie gelesen); `GameServer`-Konstruktor legt beim Start für jede Platzierung eine NPC-Entity an, `Program.cs` lädt npcs.json fail-fast - manuell verifiziert (Server startet fehlerfrei). (4) `World.CreditYang` (0c) ist jetzt die einzige Stelle, die Entity.Yang erhöht (Monster-Drops, Verkauf, Debug-Tools) - deckelt bei MaxYang, `TryAttack` nutzt sie für Monster-Kill-Yang (Kill bleibt gültig, auch wenn der Deckel greift); `TryAttack` lehnt Kind=Npc-Ziele mit TargetNotAttackable ab (VOR dem Gleiche-Kinds-Check, der Player-vs-Npc sonst nicht blockt); `TryOpenShop`/`TryBuy`/`TrySell` (World, kein Netzwerk) mit `CheckShopAccess` als gemeinsamer, bei jeder Aktion erneuter Vorprüfung (NPC existiert/ist Npc-Kind/hat Sortiment/Reichweite); bestehende Monster-KI-/Save-Schleifen brauchten keine Änderung (prüfen bereits explizit auf Kind=Monster/Player bzw. CharacterId.HasValue, siehe 0e). (5) Handler rufen die World-Methoden auf (Erfolg: ShopContent bzw. bestehende InventorySync/YangChanged, Fehlschlag: ShopActionFailed); neuer, dritter `CharacterSaveRequest`-Channel (0d) - Kauf/Verkauf speichern Yang UND Inventar/Ausrüstung in einem SaveChangesAsync-Aufruf (EF Core fasst das automatisch in einer Transaktion zusammen), bestehende Yang-only-/Inventory-only-Channels bleiben für ihre bisherigen Auslöser unverändert; DebugGiveYangRequest-Handler nutzt den bestehenden reinen Yang-Channel. (6) Client: NPCs nutzen dieselbe CharacterView wie Spieler (grüner statt weißer Name, "NPC" in der Debug-Liste, `CharacterView.ContainsWorldPoint` neu für den Rechtsklick-Bounds-Check über alle Ebenen-Renderer); Shop-Panel im bestehenden Debug-Test-Panel (Sortiment mit Kaufen-Knopf, eigene Slots mit Verkaufspreis aus `ItemCatalogClient` [kein Paket trägt Verkaufspreise], Yang-Stand, Schließen-Knopf, automatisches Schließen bei zu großer Distanz); "+1000 Yang"-Knopf. Batchmode-Compile-Check 0 Fehler. (7) `NpcCatalogTests`/`NpcSpawnCatalogTests`, `WorldShopTests` (TryOpenShop/TryBuy/TrySell/CreditYang vollständig), TryAttack-vs-Npc, aggressive Monster ignorieren NPCs, NPCs wandern/speichern nie; 3 LiteNetLib-Integrationstests (AOI-Sichtbarkeit/Kauf/Verkauf/Außer-Reichweite, Regressionstest: Yang UND Inventar bleiben nach Kauf+Disconnect+Rejoin exakt erhalten - deckt sowohl den neuen kombinierten Channel als auch den Include-Fund aus dem HP-Anzeige-Bugfix ab). (8) Diese Doku-Aktualisierung (neuer Abschnitt "Shop und NPCs", Dev-Tools/Meilensteine/Status, veraltete HP-Sync-Lücke entfernt und durch die neue Yang-bei-Join-Lücke ersetzt - M5 damit komplett fertig, M6 offen). 417 Tests grün, mehrfach stabil (ein vereinzelter, vorbestehender Timing-Flake bei einem unveränderten AOI-Test trat einmal auf, in Wiederholung grün), Batchmode-Compile-Check 0 Fehler.

- M6a (Level, XP, Werte, SP, Ausweichen, Speichern von HP/SP, docs/tasks/M6a-progression.md) fertig, in 7
  Commits (0 Review nur berichtet, kein Commit): (1) `data/classes.json` (Krieger) auf Formelkonstanten
  umgestellt (BaseHp/BaseSp/BaseAttack/BaseDefense/BaseSt/BaseHt/BaseDx/BaseIq + Faktoren, so hergeleitet,
  dass Level 1 exakt die bisherigen Werte ergibt), neue `data/progression.json` (XpToNext-Tabelle, 15-stufige
  Levelunterschieds-Tabelle), `data/mobs.json` um Level/BaseXp erweitert. (2) Migration `AddCharacterProgression`
  - Level (DB-Spalten-Default 1, nicht nur der C#-Default, sonst hätten bestehende Zeilen das ungültige
    Level 0 bekommen), Xp, SpentSt/Ht/Dx/Iq, UnspentStatPoints/SkillPoints, CurrentHp/CurrentSp (beide
    nullable, siehe Login-Regeln). (3) Shared: `StatKind`, `CharacterStatsPacket`, `AllocateStatPointRequestPacket`,
    `EntityDodgedPacket`, `DebugSetLevelRequestPacket`, Protocol.Version 11. (4) `ClassCatalog`/`MobCatalog`
    um die neuen Felder erweitert, neue `ProgressionCatalog` (`Lumiis.Data.Progression`, analog zu
    MobCatalog); `Entity` hält eine `ClassDefinition`-Referenz statt ~20 Formelkonstanten zu duplizieren
    (World kennt Lumiis.Data ohnehin), dazu Level/Xp/Spent*/Unspent*/MaxSp/CurrentSp/DodgeBp/LastCombatAtMs/
    NextRegenAtMs/BaseXp; `World.RecomputeDerivedStats` ersetzt das bisherige M5a-`RecalculateCombatStats`
    (mit ClassDef: volle Formel; ohne ClassDef bleibt nur der bestehende Ausrüstungs-Bonus-Pfad aktiv, damit
    alle kampf-/fortschrittsfernen Bestandstests unverändert funktionieren); `TryJoin` bekommt
    classDef/level/xp/spent*/unspent*/currentHp/currentSp als neue optionale Parameter (Defaults erhalten
    alle bestehenden Aufrufer) und wendet die Login-Regeln an (null→voll, ≤0→wiederbelebt, über Max→
    begrenzt); `TryAttack` würfelt nach Cooldown-Verbrauch/reaktiver Zielerfassung einen Ausweichwurf;
    `AwardXp` (Schleife für mehrere Levelaufstiege pro Kill), `TryAllocateStatPoint`, `TryDebugSetLevel`,
    `RunRegeneration` (Teil von `World.Tick`) neu. (5) `GameServer`: `CharacterStatsPacket` beim Join und nach
    jedem der in der Aufgabe genannten Ereignisse (neuer `FindConnectionByEntityId`-Helfer für Respawn/
    Regeneration, die außerhalb eines eingehenden Pakets ausgelöst werden); `BroadcastAttackResult` verschickt
    bei Ausweichen `EntityDodgedPacket` statt `EntityDamagedPacket`; kombinierter `CharacterSaveRequest`-
    Channel (M5b) um den vollen Fortschritts-Stand erweitert, mit zwei neuen Auslösern (periodisch wie
    Position, Disconnect) zusätzlich zu sofort bei Levelaufstieg/Punkteverteilung; `Program.cs` lädt
    progression.json fail-fast. (6) Client: `ClientWorld.CharacterStats`/`CharacterState` (eigene HP
    weiterhin über die bestehende `ApplyHp`, kein zweiter Speicherort), Debug-Panel-Bereich "Charakter"
    (Level/XP/HP/SP/Werte mit "+"-Knöpfen/Status-Fertigkeitspunkte/Angriff/Verteidigung/Ausweichen,
    Level-Cycler + "Level setzen"), Overlay-Meldungen "Level Up!"/"Ausgewichen!"/"Verfehlt!". Batchmode-
    Compile-Check 0 Fehler, `tools/test-client-playmode.sh` 4/4 grün. (7) Neue `ProgressionCatalogTests`
    (inkl. Regressionstest gegen die echte data/progression.json), neue `WorldProgressionTests` (40 Tests:
    Level-1-Regressionstest, Formel-Beispiele, Dodge-Deckel, alle Levelunterschieds-Stufen, Mindestens-1-Regel,
    mehrere Levelaufstiege pro Kill, kein XP bei MaxLevel, Punkteverteilung inkl. HT/IQ-CurrentHp/Sp-Kopplung,
    Ausweichen mit DodgeBp 10000/0, Regeneration mit Fake-Zeit, Login-Regeln), 6 neue LiteNetLib-
    Integrationstests (Join-CharacterStats, Kill-XP-mit-Levelaufstieg, AllocateStatPoint inkl. Ablehnung,
    Disconnect/Rejoin-Persistenz-Regression - dabei einen echten Timing-Race-Testfehler gefunden und durch
    räumlichen Abstand zwischen Spieler und Monster behoben, statt den eigentlichen Code anzupassen -,
    erzwungenes Ausweichen, DebugSetLevel nur mit Flag), ClientWorldTests für `ApplyCharacterStats`. (8)
    Diese Doku-Aktualisierung (neuer Abschnitt "Fortschritt: Level, XP, Werte", Dev-Tools/Meilensteine/Status
    - M6a damit fertig, M6b (Skills) offen). 504 Tests grün, mehrfach stabil, Batchmode-Compile-Check 0
    Fehler. Migration noch nicht gegen ein echtes Postgres angewendet/end-to-end mit Auth+Server+Bot
    verifiziert (siehe "Fertig, wenn" in der Aufgabenstellung) - offen für einen abschließenden manuellen Check.
- M6b (Skills: Wuchtschlag, Kreisschlag, Kampfrausch, Hast, docs/tasks/M6b-skills.md) fertig, in 7 Commits
  (0 Review nur berichtet, kein Commit): (1) `data/skills.json` (vier Platzhalter-Skills, je 17
  Stufen-Einträge, `_hinweis`-Feld markiert alle Zahlen als Platzhalter). (2) Migration
  `AddCharacterSkillLevels` - neue Tabelle `CharacterSkillLevel` (analog `CharacterInventoryItem`,
  Unique-Index CharacterId+SkillId). (3) Shared: `SkillType`/`BuffType`-Enums, `UseSkillRequestPacket`,
  `AllocateSkillPointRequestPacket`, `UseSkillFailedPacket`, `ActiveBuffsChangedPacket`,
  `CharacterStatsPacket` um eine Skills-Liste (SkillId, Level) erweitert, Protocol.Version 11 -> 12. (4)
  `SkillCatalog`/`SkillCatalogData` (`Lumiis.Data.Skills`, analog MobCatalog) laden/validieren fail-fast;
  `World.TryAttack` in ein schlankes `ResolveHit` extrahiert (Schaden jetzt mit einem
  `damageMultiplierPercent`-Parameter skaliert, bei 100% mathematisch identisch zur bisherigen Formel -
  alle bestehenden TryAttack-Tests blieben dabei unverändert grün, der Beleg für die Extraktion selbst);
  `TryUseSkill`/`TryUseSingleStrike`/`TryUseAreaAttack`/`TryUseBuffSkill`/`ConsumeSkillUse` (vier
  UNABHÄNGIGE Skill-Cooldowns pro Charakter, `Entity.SkillCooldownReadyAtMs`, getrennt vom normalen
  Angriffs-Cooldown), `TryAllocateSkillPoint` (analog `TryAllocateStatPoint`); Buffs
  (`Entity.ActiveBuffs`, kein Stapeln bei erneutem Cast) fließen über `RecomputeDerivedStats`
  (Kampfrausch, Attack) bzw. das neue `World.EffectiveAttackCooldownMs` (Hast, nächster normaler Angriff)
  ein und werden von `World.Tick` bei Ablauf entfernt (kein Persistenz-/Disconnect-Sonderfall nötig, siehe
  Entity.ActiveBuffs-Kommentar). (5) `Program.cs` lädt skills.json fail-fast;
  `HandleUseSkillRequest`/`HandleAllocateSkillPointRequest` neu, dabei die Netzwerk-/Yang-/XP-/Drop-Seite
  eines Treffers aus `HandleAttackRequest` in ein gemeinsames `ApplyAttackResult` extrahiert (reine
  Extraktion, `HandleAttackRequest` unverändert im Verhalten) - wiederverwendet für Wuchtschlag (ein
  Treffer) UND Kreisschlag (mehrere Treffer, je Ziel ein Aufruf); `World.Tick`s neue `BuffsExpired`-Liste
  löst `ActiveBuffsChangedPacket`+`CharacterStatsPacket` an den Besitzer aus; Skillstufen-Persistenz über
  den bestehenden kombinierten `CharacterSaveRequest`-Channel (M5b/M6a), voller Snapshot wie Inventar,
  sofort bei Punkteverteilung. (6) Client: echte Tasten 1-4 (NICHT hinter
  `#if UNITY_EDITOR || DEVELOPMENT_BUILD`, echte Spielsteuerung) senden `UseSkillRequestPacket` -
  Wuchtschlag nutzt das per Rechtsklick anvisierte Ziel; neue `SkillCatalogClient` (lädt skills.json aus
  StreamingAssets, analog `ItemCatalogClient`, nur Anzeigenamen/CooldownMs je Stufe); Overlay-Zeile
  (ungated) mit Tastenbelegung, lokal optimistisch gezählten Skill-Cooldowns und server-autoritativen
  Buff-Restzeiten (`ActiveBuffsChangedPacket`, lokal mit `Time.deltaTime` heruntergezählt);
  `ClientWorld.CharacterState` um eine Skills-Dictionary erweitert (UnityEngine-frei, über `dotnet test`
  mitgeprüft); Debug-Test-Panel-Bereich "Skills" (Stufe 0-17 je Skill, "+"-Knopf, analog zu M6a).
  Batchmode-Compile-Check 0 Fehler, `tools/test-client-playmode.sh` 4/4 grün. (7) `SkillCatalogTests` (15,
  inkl. Regressionstest gegen die echte data/skills.json - Hast Stufe 17 EXAKT 20% Cooldown-Reduktion, wie
  vom User vorgegeben), `WorldSkillTests` (17, eigene kleine SkillCatalog-Fixture: gemeinsame
  Vorprüfungen/vier unabhängige Skill-Cooldowns, Wuchtschlag-Schaden/-Reichweite, Kreisschlag-Mehrfachtreffer
  mit unabhängigem Ausweichen je Ziel, Kampfrausch sofort+Ablauf+kein Stapeln, Hast Stufe 1/17 exakt+Ablauf,
  AllocateSkillPoint), 6 neue LiteNetLib-Integrationstests (Punkteverteilung, Wuchtschlag-Mehrschaden,
  Kreisschlag-Mehrfachtreffer, Kampfrausch+ActiveBuffsChanged+Mehrschaden, Hast mit Fake-Zeit beweist einen
  früher möglichen zweiten Angriff, Skillstufen überleben Disconnect+Rejoin - dabei denselben bekannten
  Async-Save-Race wie bei `AfterBuy_DisconnectAndRejoin_YangAndInventoryPersistExactly` getroffen, hier
  aber mit einem expliziten DB-Poll-Wait vor dem Rejoin deterministisch abgefangen statt es dem
  Netzwerk-Zufallstiming zu überlassen). (8) Diese Doku-Aktualisierung (neuer Abschnitt "Skills",
  Meilensteine/Status - M6 damit abgeschlossen, M7 vs. M6c/M6d als offene Frage markiert, siehe
  "Aktuelle Aufgabe"). 548 Tests grün, mehrfach stabil (ein einzelner Fehlschlag in einem Lauf war der
  bereits dokumentierte, unveränderte CPU-Kontentions-Flake bei `AfterBuy_DisconnectAndRejoin...`),
  Batchmode-Compile-Check 0 Fehler. Noch offen: manueller End-to-End-Check mit echtem Auth+Server+Bot
  (Bot selbst nutzt keine Skills, siehe "Fertig, wenn" in der Aufgabenstellung) sowie das Anwenden der
  Migration `AddCharacterSkillLevels` gegen ein echtes Postgres (bisher nur generiert, wie schon
  `AddCharacterProgression` aus M6a).
- Kampf-Visualisierung (docs/tasks/combat-visuals.md, kein eigener Meilenstein) fertig, in 6 Commits
  (0 Review nur berichtet, kein Commit; 7 Prüfung bestätigt bestehende Tests unverändert grün, kein
  eigener Commit nötig): (1) `Lumiis.Shared.csproj` kopiert `classes.json` seit dieser Aufgabe
  zusätzlich zu looks.json/items.json/skills.json; neue `ClassCatalogClient` (nur MeleeRangeWire,
  erster Katalog-Eintrag statt echter Klassen-Zuordnung, analog `GameServer.DefaultClassName`);
  `SkillCatalogClient` um `RadiusWire` je Skill erweitert. (2) Neue `RangeRingView` - dünner,
  halbtransparenter Ring in MeleeRangeWire um den lokalen Spieler, immer sichtbar solange verbunden;
  nutzt eine neu gebackene Einheits-Ring-Sprite (`pixelsPerUnit == pixelSize`, Durchmesser 1
  Weltenheit bei `localScale` 1) - unparented, rührt die bestehende `CharacterView`-SortingGroup
  nicht an. (3) Neue `RadiusFlashView` (transient, self-destructing, ~350ms Ein-/Ausblenden) - beim
  Senden von Kreisschlag OPTIMISTISCH ausgelöst (nicht reaktiv am `EntityDamaged`/`EntityDodged`,
  da diese Pakete keine Skill-Kennzeichnung tragen und ein Kreisschlag ohne Ziel im Radius ohnehin
  kein Paket erzeugen würde - reaktiv also strukturell unzuverlässig, im Bericht begründet). (4) Neue
  `HitLineView` (LineRenderer, transient, linear verblassend ~200ms) - bei jedem eingehenden
  `EntityDamagedPacket` (rot) und `EntityDodgedPacket` (blau-grau), unabhängig davon ob der lokale
  Spieler beteiligt ist, solange beide EntityIds bekannte Positionen haben; mehrere gleichzeitige
  Linien unabhängig (je ein GameObject). (5) Neue `ClientWorld.SetFacingOverride` (UnityEngine-frei,
  per `dotnet test` geprüft) - dreht eine Entity per `FacingMath.FromDelta` zum Ziel, kein eigener
  Zustand/Timer (der nächste `HandleEntityMove`-Aufruf überschreibt wieder bewegungsbasiert); lokaler
  Spieler optimistisch beim Senden (Rechtsklick-Angriff, zielgerichtetes Wuchtschlag), beliebiger
  Angreifer reaktiv im selben Hook wie die Trefferlinie (Abschnitt 4) mitbedient - 4 neue
  `ClientWorldTests`. (6) `RangeRingView` verallgemeinert (Farbe/Sortierung als Parameter statt
  Konstanten) - trägt jetzt auch die neuen Buff-Ringe (`GameServer.SyncBuffRings`, voller Resync bei
  jedem `ActiveBuffsChangedPacket`, Kampfrausch rot-orange/Hast gelb mit größerem Durchmesser,
  konzentrisch überschneidungsfrei); `ActiveBuffsChangedPacket` geht laut Protokoll nur an den
  Besitzer, Buff-Ringe sind deshalb strukturell auf den lokalen Spieler beschränkt. (8) Diese
  Doku-Aktualisierung (neuer Abschnitt "Kampf-Visualisierung", manuelle Prüf-Checkliste, Status).
  552 Tests grün (548 + 4 neue `ClientWorldTests`), 5x stabil, Batchmode-Compile-Check nach jedem
  Abschnitt 0 Fehler, `tools/test-client-playmode.sh` 4/4 grün (bestehende Login-PlayMode-Tests laufen
  vor jedem Join ab, die neuen Visuals werden dabei nie instanziiert - keine Anpassung nötig).
- Escape-Menü (docs/tasks/escape-menu.md, kein eigener Meilenstein) fertig, in 4 Commits (0 Review nur
  berichtet, kein Commit; 4 Prüfung bestätigt bestehende Tests/PlayMode-Tests unverändert grün, kein
  eigener Commit nötig): (1) `NetClient.StopClient()` (privat) zu öffentlichem `Stop()` gemacht, mit
  `IsRunning`-Guard (per Reflection gegen die installierte LiteNetLib-DLL verifiziert: `NetManager.
  IsRunning` existiert) - dadurch nachweislich sicher mehrfach aufrufbar. Neue `LoginFlow.
  SwitchCharacter()` (State auf CharacterSelect, Token bleibt) - muss VOR dem tatsächlichen Trennen
  aufgerufen werden, damit `OnDisconnected` (das jeden Disconnect aus InGame sonst als unfreiwillig
  behandelt) nur noch einen No-Op sieht. `LoginController` bekommt drei neue öffentliche Methoden fürs
  Escape-Menü: `SwitchCharacter()` (State setzen, `NetClient.Stop()`, `RefreshCharactersAsync` -
  behandelt ein zwischenzeitlich abgelaufenes Token über den bestehenden 401-Pfad), `Logout()` (reine
  Wiederverwendung des bestehenden `LoginFlow.Logout()`, gleiches Vorzieh-Prinzip), `QuitGame()`
  (`Application.Quit()`, im Editor `EditorApplication.isPlaying = false`). Neuer `LoginFlowTests`-Test
  (`SwitchCharacter_KeepsTokenAndReturnsToCharacterSelect`). (2) Neue `EscapeMenu.uxml` (drei Buttons,
  wiederverwendet bestehende `.screen`/`.panel`/`.title`/`.button-row`-Klassen aus `lumiis.uss`, keine
  neue CSS) - als DRITTE Wurzel NACH Login/CharacterSelect in `rootVisualElement` angehängt (die
  bestehenden `LoginControllerPlayModeTests` greifen per `root[0]`/`root[1]` auf genau die ersten beiden
  zu). Escape (nur `LoginFlowState.InGame`) öffnet/schließt es; `ApplyLoginFlowState()` schließt es
  zusätzlich bei JEDEM Zustandswechsel. Neues `GameClient.SetGameplayInputLocked(bool)` sperrt
  Bewegungsklick/Angriff/Skills 1-4, während es offen ist (Zoom/F1 bleiben aktiv). (3) `ProjectSettings.
  asset`: `fullscreenMode` 1 (FullScreenWindow) -> 3 (Windowed), `defaultScreenWidth`/`defaultScreenHeight`
  1920x1080 -> 1280x720, `resizableWindow` 0 -> 1 - nur Standalone-Builds betroffen, Editor-Play-Modus
  unverändert. (5) Diese Doku-Aktualisierung (neuer Abschnitt "Escape-Menü", manuelle Prüf-Checkliste,
  Status). 553 Tests grün (552 + 1 neuer LoginFlow-Test), mehrfach stabil (ein Fehlschlag bei
  `LaterJoiner_SeesAlreadyDamagedMonster_ReceivesEntityHpChangedAfterSpawn` in einem vollen Lauf war
  derselbe bereits dokumentierte CPU-Kontentions-Flake-Typ wie bei `AfterBuy_DisconnectAndRejoin...` -
  5/5 grün in Isolation bestätigt, diese Aufgabe berührt keinerlei Server-/Shared-Code), Batchmode-
  Compile-Check nach jedem Abschnitt 0 Fehler, `tools/test-client-playmode.sh` 4/4 grün (dritte UI-Wurzel
  stört die bestehenden Tests nicht).

- Paper-Doll-Rendering-Umbau (Socketing, Waffen-Sprites, Rüstungs-Palette, +7/+9-VFX) fertig, in
  einem Arbeitsblock: (1) `data/items.json` Waffen um `spriteKey`/`pivotOffset`/`upgradeLevel` (0-9)
  erweitert, `data/looks.json` Rüstungs-Looks um `colors` (primary/secondary/accent als Hex);
  Server-DTOs (`ItemCatalogData`/`LookCatalogData`) und `ItemCatalog` (Validierung 0-9) sowie
  `ItemCatalogClient`/`LookCatalogClient` mitgezogen. (2) `ISpriteProvider.LoadWeaponSprite` +
  `CachingSpriteProvider` (Resources/Sprites/Items/, Cache nach spriteKey, null-Cache bei fehlendem
  Asset); `PlaceholderSpriteProvider` liefert bewusst null. (3) `CharacterView`-Hierarchie
  Body -> WeaponSocket -> Weapon, `SetWeapon`/`ClearWeapon`, MaterialPropertyBlock-Farben
  (`_Color_ArmorPrimary/Secondary/Trim`), gemeinsames `CharacterArmor.mat`/Shader, zwei
  ParticleSysteme am Socket und TrailRenderer. (4) `CharacterAnimator` mit
  `WeaponSocketFrameConfig` (Frame-Data je State/Frame/Facing) und Schlagimpuls; `GameClient` setzt
  die Item-Waffe nach jedem InventorySync, triggert Schlaganimation/Trail bei Senden und bei
  EntityDamaged/EntityDodged. (5) `CharacterTestboard` um +0..+9-Waffentestfeld (mit periodischem
  Schlag) und Debug-Panel-Button/Stufen-Override erweitert. 555 Server-/Unit-Tests grün,
  `tools/test-client-playmode.sh` 9/9 grün (4 bestehende + 5 neue CharacterView-PlayMode-Tests),
  Batchmode-Kompilierung ohne Fehler. Manuelle Sichtprüfung der VFX/Farben steht noch aus (siehe
  Checkliste unten).
- Asset-Testlabor + aufgeräumte Testsicht: (1) `AssetTestLab` (nur Editor/Development, F10) baut eine
  eigene UI-Toolkit-Oberfläche (`AssetTestLab.uxml`) mit Waffen-/Stufen-/Rüstungs-/Facing-/
  Animationssteuerung; blendet Server-Figuren, Monster, NPCs, Ringe und die alten Testboards aus und
  stellt eine lokale Vorschaufigur plus drei Trainingspuppen auf. (2) `GameClient` bekam dafür
  `SetAssetLabMode`/`LocalPlayerWorldPosition`; das IMGUI-Debug-Overlay ist standardmäßig aus und wird
  mit F1 wieder eingeblendet, neue Entities werden während des Labors ebenfalls versteckt. (3) Drei
  reproduzierbare Platzhalter-Waffen-PNGs (`weapon_2_wood`, `weapon_2_iron`, `weapon_3_axe`) inkl.
  Import-Metadaten, Generator `tools/generate-placeholder-weapons.py` und Art-Provenance-Einträgen.
  (4) `PlaceholderSpriteProvider.DrawWeapon` zeichnet jetzt Griff/Parierstange/Klinge statt eines
  dünnen Rechtecks, falls ein PNG fehlt. (5) Schnelle Screenshot-Verifikation ohne Unity-Neustart:
  F12/Button im Labor, `ScreenshotRequestWatcher` + `tools/request-screenshot.sh` für laufende
  Play-Modi sowie `tools/capture-client-screenshots.sh` als einmaliger GUI-Lauf. 12/12 PlayMode-Tests
  grün; die automatisch erzeugten Bilder (`/tmp/lumiis-asset-lab-plus0.png`, `-plus9.png`, `-attack.png`,
  `-icons.png`, `-map.png`, `-characters.png`, `-hud.png`) zeigen Labor-UI, Vorschaufigur,
  Trainingspuppen, Waffe, +9-Glitzern, Trail, Icon-Galerie, Greenfield-Kartenvorschau, die acht echten
  Lumiis-Charaktere und das HUD mit Demo-Gruppe.
- Lumiis-Referenz-Assets: (1) `tools/import-lumiis-assets.py` importiert 546 Icons (34 Kategorien)
  aus dem Asset-Archiv nach `Resources/Icons`, inkl. Windows-sauberer Dateinamen, Manifest,
  deterministischer `.meta`-Dateien und Provenance-Einträgen. (2) `ItemIconCatalog` + `iconKey`
  zeigen Icons in Inventar/Ausrüstung/Shop; die Labor-UI hat eine paginierte Icon-Galerie
  (64 Icons/Seite, Kategorie-Filter). (3) 53 echte Charakter-Sheets aus dem Lumiis-Referenzprojekt
  unter `Resources/Characters`; `CharacterSheetGallery` zeigt Krieger/Ninja/Sura/Schamane (m/w) im
  Labor und der Screenshot-Lauf legt `/tmp/lumiis-asset-lab-characters.png` ab. (4) Feature-Gap-,
  Icon- und Kartenplanung als `docs/feature-gap-lumiis.md`, `docs/icon-integration-plan.md`,
  `docs/map-import-plan.md`. 644 Server-/Unit-Tests und 15 PlayMode-Tests grün.
- M7-HUD + Chat-MVP: (1) `Hud.uxml`/`HudController` zeigen Name/Level, HP/SP/EXP/Yang, Ziel + HP,
  Meldungen, vier Skill-Slots mit Cooldowns, aktive Buffs und einen Chat mit Karte/Global/Flüstern.
  Das alte IMGUI-Overlay bleibt F1-Debug. (2) Chat-Protokoll: `ChatRequestPacket` (16),
  `ChatMessagePacket` (1020), `ChatChannel`, `Protocol.Version` 13/14; 400-ms-Rate-Limit pro Verbindung,
  Textlimit 200 Zeichen, SenderName serverseitig aus der Entity. (3) Chat-UI sperrt während der
  Eingabe Bewegung/Skills (`SetChatInputFocused`), Echo für Whisper, Systemmeldung bei unbekanntem Ziel.
  (4) Roundtrip-Tests, HUD-PlayMode-Test und LiteNetLib-Integrationstest (Karte + Whisper) ergänzt.
- M7-Gruppen-MVP: Chat-Befehle `/invite Name`, `/accept`, `/decline`, `/leave`, `/party`; max. 8,
  Leader = Einladender, Leaderwechsel beim Verlassen. `PartyChangedPacket` (1021) ist ein voller
  Resync; das HUD zeigt Leader/Name/Level/HP und einen Verlassen-Knopf, HP wird bei Treffer/Heilung/
  Respawn nachgezogen. EXP-Teilen: andere Mitglieder erhalten 50 % des Kill-XP (mind. 1). Kein DB-Save, keine Rollen. Integrationstest mit zwei Clients
  (Invite → Accept → Leave) ist grün.
- M6c (Ausruestungs-Aufwertung +0..+9, docs/tasks/M6c-upgrade.md) fertig: `data/upgrade.json`
  (Max 9, Platzhalter-Erfolgstabelle 90..10, Schleifstein/Bewahrungsrolle), `UpgradeCatalog` mit
  Startabbruch-Validierung, `ItemType.UpgradeMaterial/ProtectionScroll` und `upgradeBonusPerLevel` je
  Ausrüstungs-Item, Shop-Erweiterung. Migration `AddUpgradeLevels` (`CharacterInventoryItem.UpgradeLevel`
  + vier `Character.Equipped...UpgradeLevel`), Save/Load über den kombinierten Kanal, Stufen wandern bei
  Equip/Unequip mit. `World.TryUpgrade` prüft alles vor jedem Verbrauch, würfelt mit injizierbarem
  `Random`, verbraucht Schleifstein (und Rolle) immer, zerstört bei Fehlschlag ohne Rolle das Item.
  Protocol.Version 19 mit `UpgradeItemRequest/Result/ActionFailed`; `InventorySyncPacket` trägt Stufen
  pro Slot und je Equipped-Slot. Client: +N-Anzeige, Aufwerten-Knopf + Bewahrungsrollen-Checkbox im
  Debug-Panel, deutsche Overlay-/Systemmeldungen. 644 Server-Tests grün (inkl. 20 neue Aufwertungs-/
  Katalog-/Packet-Tests und 3 LiteNetLib-Integrationstests inkl. Disconnect/Rejoin), Unity-PlayMode
  grün.
- Technik-Review des Boden-Drop/Schmied/Party-Blocks (2026-10-25): Boden-Drops waren angreifbar (normaler
  Angriff, Wuchtschlag, Kreisschlag) und blieben für immer liegen - jetzt unangreifbar, 10 s Killer-/
  Gruppenbesitz, 5 min Lebensdauer, versetzt abgelegt. Bugfixes: erster Login nach Serverstart lief in den
  Join-Timeout (DB-Vorwärmen + `JoinLookupTimeoutMs`), Charakterliste ohne Aussehen (Auth liefert jetzt
  Level/Karte/Aussehen/Ausrüstung, Erstellen mit Körper/Frisur/Haarfarbe; Client-`CharacterInfo` trägt
  `Appearance`/`WeaponItemId`/`WeaponUpgradeLevel`), Holzschwert senkrecht über dem Kopf (Item-Waffen-PNGs
  auf 48-px-Figuren auf ~0,8 Einheiten skaliert, Ruhewinkel je Richtung in `WeaponSocketFrameConfig`).
  656 Tests grün.
- Gruppe über Ausloggen hinweg + Rollen (2026-10-25, Protocol.Version 20): `PartyManager` (CharacterId-
  basiert, Offline-Mitglieder, Leiterwechsel, Kick, Promote, Rollen), `data/party.json` + `PartyCatalog`,
  Rollen-Boni in `RecomputeDerivedStats`, Pakete 25-27, `PartyChangedPacket.MemberStatus`. Außerdem Bugfix
  Holzschwert: `CharacterView` erkannte den Lumiis-Provider hinter `CachingSpriteProvider` nicht (Hand-
  Anker/Skalierung/Namensschild-Höhe der Platzhalter griffen) - in Unity per Screenshot bestätigt. 687 Tests
  grün (PartyManager 12, PartyCatalog 10, Rollen-Bonus 1, 2 neue Integrationstests).
- Map2 = Jayang (2026-10-25): Quelldaten aus `lumiis-2d` (`buildMap("jayang")`, Stand 4376dae) per
  `tools/export-lumiis-map.mjs` nach `tools/map2-jayang-source.json`/`-mobs.json` exportiert,
  `tools/import-map2-jayang.py` schneidet 256×256 Tiles (Original x 136-391, y 200-455: Dorf + erstes
  Jagdgebiet "Lager des Weißen Eids", Lv 18-25) mittig in die 1000×1000-Zellenkarte (Origin 372) und
  schreibt `Resources/Maps/map2.png` (16 px/Tile, 4096²), `collision.bin`, `map.json`, `portals.json`,
  `npcs.json` (Schmied + Händler an den Originalplätzen), `spawns.json` (alle Zone-1-Gruppen 1:1 = 340
  Monster außerhalb der Dorf-Sicherheitszone, dazu 3 feste "Lumiis der Schwärze"). Neue Mobs 10-15, 18 in
  `mobs.json` (Werte bewusst nach unserer Formel skaliert, Originalwerte bleiben in `-mobs.json`), Wellen
  für Mob 18 in `metins.json`. Map1 bekam ein Nordportal am oberen Wegende (das Original-Portal liegt
  außerhalb des Map1-Ausschnitts), Jayang führt am Osttor zurück. Nicht übernommen: Zonen 2-5 (Lv 25-45,
  über dem Alpha-Levelcap), Hasun-Dong-Portal, Lager des Alten Mannes, eigene Monstergrafik für die neuen
  Mobs (Platzhalterform). 684 Tests grün.
- Spielbarkeit I (2026-10-25, Protocol.Version 21): Tote Spieler können bis zum Respawn nichts mehr -
  `World.TryAttack` liefert `AttackFailedReason.AttackerDead`, `TryUseSkill` `UseSkillFailedReason.UserDead`,
  `SetDestination` lehnt ab; `GameServer.IsDeadPlayerAction` verwirft MoveTo/StopMove/Aufheben/Inventar/
  Shop/Aufwerten eines toten Spielers still, und beim Spielertod geht sofort ein `EntityStop` an die
  Beobachter. Ruckeln beim Laufen behoben: `ClientWorld` setzte bei jedem EntityMove die Anzeige hart auf
  die Server-Startposition zurück (WASD schickt alle 100 ms ein neues Ziel); jetzt wird die Abweichung als
  Offset mit 0,1 s Zeitkonstante ausgeblendet, ab 300 Wire (Teleport/Respawn) hart gesetzt.
  Trainingspuppen: Mob 6 "Trainingspuppe" (`dummy`-Sheet, 2000 HP, steht still, 5 s Respawn, keine
  Beute) dreimal am Westrand des Map1-Dorfplatzes; neues optionales Mob-Feld `fightsBack` (Default true,
  `MobDefinition.FightsBack`/`Entity.FightsBack`) - bei false erfasst der Mob auch nach einem Treffer kein Ziel.
  `EntitySpawnPacket.Level` (Protocol 21): Level von Monster/Spieler beim Erscheinen, `ClientEntity.Level` -
  für Namensschild/Zielanzeige (Spieler-Levelaufstiege danach aktualisieren es bisher nicht).
- Offen: Monster-Sheets für die Jayang-Mobs, weitere Zonen/Dungeons, Reittiere; siehe
  `docs/feature-gap-lumiis.md`.
### Geschlossen: später beitretende Entities erschienen nicht im Unity-Client
War kein Codefehler. User hat den Test mit fokussiertem Unity-Fenster wiederholt (Server, Unity Play, dann Bots
gestartet, ins Unity-Fenster geklickt): alle Spawns kamen an, auch mit 99 Bots. Wahrscheinliche Ursache war die
Drosselung der Editor-Update-Schleife ohne Fensterfokus (siehe "Bekannte Eigenheiten" unten) - kein
Server-/Client-Bug. Die dabei entstandenen Tests (ClientWorld ohne Unity testbar, Integrationstests für spätere
Joiner/Burst) bleiben als nützliche Regressionsabdeckung erhalten.

### Noch zu prüfen (Editor/Play-Modus, vom User)
- Unity öffnet `client/` ohne Compiler-Fehler in der Console (Batchmode-Check war bereits grün, Editor-Check zur Sicherheit trotzdem empfohlen).
- Play drücken: Login-Fenster erscheint, Verbinden mit laufendem Server klappt, eigene Figur erscheint, Klick auf den Boden bewegt sie sichtbar interpoliert zum Zielpunkt.
- `Lumiis.Bot -- --count 6` (oder mehr) parallel starten: weitere Figuren mit unterschiedlichen Looks erscheinen und laufen zufällig.
- Server stoppen bzw. Verbindung kappen: Client zeigt "Getrennt", räumt alle Figuren auf und zeigt wieder das Login-Fenster.
- Ungültiger Name (z. B. "ab") bzw. falscher Port testen: Statuszeile/Login zeigt eine verständliche Fehlermeldung statt eines stillen Hängers.
- Mausrad-Zoom (3-300) und F1-Overlay-Umschaltung testen; Entity-Liste im Overlay zeigt Id/Name/Position/Entfernung, sortiert, max. 12 Zeilen; Statuszeile zeigt zusätzlich "Sprites im Cache: N".
- Eigene Figur erkennbar: Name über dem Kopf ist gelb (statt weiß), ein Ring liegt sichtbar unter den eigenen Füßen; bei anderen Spielern weder das eine noch das andere.
- Paper-Doll-Charaktere: Figuren zeigen Körper+Rüstung+Haare+ggf. Helm/Waffe/Schild statt eines Quadrats, drehen sich beim Laufen sichtbar (Down/Up/Side, Side gespiegelt bei Linkslauf), Waffe schwingt beim Gehen um den Griff (nicht um die Füße), Haare sind eingefärbt (nicht weiß), ein Helm mit hidesHair blendet die Haare korrekt aus.
- F2 zeigt/versteckt ein Testboard (Raster aus Body x allen Rüstungen x 4 Facings) an einer festen Stelle neben der Karte.
- F9 zeigt/versteckt ein zweites Testfeld daneben: drei überlappende Figuren mit 0,3 Einheiten Y-Abstand - die unterste muss durchgehend vorn liegen, keine Ebene einer Figur darf zwischen den Ebenen einer anderen auftauchen (prüft die neue SortingGroup+Custom-Axis-Sortierung, siehe Konvention/Status).
- F3-F8 wechseln bei der eigenen Figur sichtbar Haarstil/-farbe/Rüstung/Helm/Waffe/Schild, ohne dass sich andere Spieler dadurch ändern (rein lokal).
- Sortierung zweier sich überlappender, normal laufender Figuren im echten Spiel (nicht nur im F9-Testfeld): Ebenen der einen Figur mischen sich nicht mit denen der anderen, die weiter unten stehende Figur liegt vorn.
- Zwei Clients gleichzeitig (Editor-Play plus `client/Builds/<plattform>/Lumiis`, siehe Abschnitt "Client"): beide sehen sich gegenseitig, der Build läuft ohne Fokus-Drosselung im Hintergrund weiter.

### Noch zu prüfen (M2c: Login/Charakterauswahl, vom User)
Voraussetzung: Postgres läuft (`docker compose up -d` nach `source tools/dev-env.sh`), Migration angewendet,
Lumiis.Auth läuft (`dotnet run --project server/src/Lumiis.Auth`), Lumiis.Server läuft.

- Unity öffnet `client/` ohne Compiler-Fehler in der Console (Batchmode-Check war bereits grün, Editor-Check zur Sicherheit trotzdem empfohlen).
- Play drücken: Das neue Login-Fenster (UI Toolkit) erscheint zentriert, lesbar (heller Text auf dunklem Grund, Buttons erkennbar) - keine alte IMGUI-Login-Box mehr sichtbar.
- Neuen Benutzernamen/Passwort eingeben (Passwort mind. 8 Zeichen), "Registrieren" klicken: führt automatisch weiter zur Charakterauswahl (leere Liste).
- Neuen Charakternamen eingeben, "Erstellen" klicken: Charakter erscheint in der Liste, danach automatisch verbunden - die eigene Figur erscheint auf der Karte, das bekannte Debug-Overlay (Statuszeile, F1) wird sichtbar.
- Spiel verlassen (z. B. Server-Prozess beenden oder Verbindung kappen): zurück zum Login-Fenster, Passwort-Feld leer, Benutzername noch ausgefüllt.
- Unity-Fenster/Play-Modus neu starten: Benutzername ist vorausgefüllt (PlayerPrefs), Passwort nicht.
- Mit demselben Konto erneut anmelden ("Anmelden" statt "Registrieren"): Charakterauswahl zeigt den zuvor erstellten Charakter, Auswahl verbindet direkt (keine erneute Charaktererstellung nötig).
- Falsches Passwort eingeben: verständliche deutsche Fehlermeldung im UI ("Benutzername oder Passwort falsch."), kein stiller Hänger.
- Bereits vergebenen Benutzernamen erneut registrieren: Fehlermeldung "Benutzername bereits vergeben.".
- Auth-URL im Bereich "Erweitert" auf einen falschen Port ändern, anmelden versuchen: Fehlermeldung "Lumiis.Auth nicht erreichbar. ...", kein Absturz/Hänger.
- Charakter erstellen, dessen Name bereits vergeben ist (z. B. von einem zweiten Testkonto): Fehlermeldung "Charaktername bereits vergeben.".
- Zwei Unity-Instanzen (oder Unity + `Lumiis.Bot`) mit demselben Charakter gleichzeitig verbinden: die zweite bekommt "Dieser Charakter ist bereits online." im UI zu sehen, nicht nur im Log.
- Server-Port im Bereich "Erweitert" auf einen falschen Wert ändern, einen Charakter wählen: Fehlermeldung zur fehlgeschlagenen Verbindung im UI (Charakterauswahl bleibt sichtbar, kein Hänger bei "Verbinde ...").
- Nach erfolgreichem Join: Debug-Test-Panel (F2/F9/F3-F8 bzw. die Buttons rechts oben) funktioniert unverändert wie vor M2c.

### Noch zu prüfen (Kampf-Visualisierung, vom User)
Voraussetzung: laufender Server + eigener Charakter mit mindestens Wuchtschlag und Kreisschlag
gelernt (Debug-Panel-Bereich "Skills", siehe Abschnitt "Skills") sowie mindestens ein Wildhund/
Waldschrat in Reichweite (Dev-Tools-Debug-Spawn oder ein regulärer Spawn-Punkt).

- Direkt nach dem Join: ein dünner, halbtransparenter Ring um die eigene Figur ist sichtbar, folgt ihr beim Laufen, bleibt auch ohne anvisiertes Ziel sichtbar.
- Rechtsklick-Angriff auf ein Monster: die eigene Figur dreht sich sichtbar sofort zum Monster (noch bevor ein Treffer-/Ausweichen-Ergebnis ankommt), eine kurze rote (Treffer) oder blau-graue (Ausweichen) Linie zwischen eigener Figur und Monster blitzt kurz auf und verschwindet wieder.
- Taste 1 (Wuchtschlag) auf ein anvisiertes Monster: gleiches Drehen+Linie wie beim normalen Angriff.
- Taste 2 (Kreisschlag): ein größerer Ring blitzt kurz um die eigene Figur auf und verblasst wieder (auch wenn kein Monster in Reichweite ist) - bei mehreren Monstern im Radius kommen mehrere Trefferlinien gleichzeitig unabhängig voneinander an.
- Ein aggressives Monster (z. B. Wildhund) neben sich angreifen lassen: auch beim GEGNERISCHEN Angriff erscheint eine Trefferlinie, und das Monster dreht sich sichtbar zur eigenen Figur (falls als Sprite mit Blickrichtung erkennbar - Monster haben aktuell keine eigene Blickrichtungsdarstellung, MonsterView zeigt das also nicht sichtbar an; bei einem ANDEREN Spieler als Angreifer wäre es sichtbar).
- Taste 3 (Kampfrausch): ein rot-oranger Ring erscheint um die eigene Figur und bleibt für die Buff-Dauer sichtbar, verschwindet danach von selbst.
- Taste 4 (Hast): ein gelber, größerer Ring erscheint zusätzlich/parallel - beide Ringe (Kampfrausch+Hast gleichzeitig aktiv) überschneiden sich nicht, keiner verdeckt den anderen.
- Verbindung trennen und neu verbinden: keine "Geister"-Ringe/-Linien vom vorherigen Login bleiben sichtbar.

### Noch zu prüfen (Escape-Menü, vom User)
- Im Spiel Escape drücken: ein Menü mit drei Buttons ("Charakter wechseln", "Ausloggen", "Beenden") erscheint mittig, das Status-Overlay bleibt sichtbar.
- Während das Menü offen ist: Linksklick auf den Boden bewegt die Figur NICHT, Rechtsklick auf ein Monster greift NICHT an, Tasten 1-4 lösen KEINEN Skill aus.
- Escape erneut drücken: Menü schließt sich wieder, Eingaben funktionieren wieder normal.
- "Charakter wechseln" klicken: zurück zur Charakterauswahl (kein erneuter Login nötig), die eigene Figur/alle anderen Entities verschwinden von der Karte, die Charakterliste zeigt weiterhin den/die bekannten Charakter(e).
- Von der Charakterauswahl aus erneut denselben oder einen anderen Charakter wählen: verbindet normal.
- "Ausloggen" klicken: zurück zum Login-Bildschirm, Benutzername ist noch ausgefüllt, Passwort leer.
- "Beenden" klicken (im Editor): Play-Modus stoppt sauber, der Editor selbst bleibt offen.
- Einen Standalone-Build erzeugen (`tools/build-client.sh`) und starten: läuft in einem Fenster (nicht Vollbild), Fenster ist in der Größe veränderbar.

### Noch zu prüfen (M7-HUD/Chat, vom User)
- Nach dem Join: links oben Name/Level, HP/SP/EXP-Balken und Yang; unten rechts vier Skill-Slots mit
  Tasten 1-4, Stufe/Cooldown; bei aktivem Kampfrausch/Hast erscheint die Restzeit.
- Rechtsklick auf ein Monster: Zielname + HP erscheinen oben in der Mitte.
- Enter öffnet das Chatfeld; „Hallo“ + Enter senden: Nachricht erscheint links unten mit `[Karte] Name:`.
- „Global“ auswählen und senden: Nachricht erscheint bei allen verbundenen Clients.
- „Flüstern“ auswählen, Zielnamen eintragen, senden: nur Ziel und Absender sehen die Nachricht.
- Bei unbekanntem Flüsterziel erscheint eine Systemmeldung; während des Tippens bewegt sich die Figur
  nicht und Taste 1-4 löst keinen Skill aus.
- Zwei Clients/Bots schnell hintereinander senden: Das 400-ms-Limit verwirft die zweite Nachricht still.
- Gruppe: `/invite Bob` senden, Bob bekommt die Einladung im Chat; Bob sendet `/accept`, beide sehen das
  Party-Panel mit Leader-Markierung und HP. Bob sendet `/leave` (oder klickt „Gruppe verlassen“), Alice
  sieht nur noch sich, Bobs Panel verschwindet.
- Gruppe mit mehr als 8 Spielern: die neunte Einladung wird mit „Gruppe ist voll“ abgelehnt.

### Noch zu prüfen (Asset-Testlabor/Paper-Doll, vom User)
Voraussetzung: laufender Server, Charakter ausgewählt; Editor-Play oder Development-Build.

- **F10** drücken oder im Debug-Panel (F1) "Asset-Testlabor" klicken: Die Szene ist aufgeräumt (keine
  Monster, NPCs, anderen Spieler, Ringe oder Testboards), links erscheint die Testlabor-UI, in der Mitte
  eine Vorschaufigur mit drei Trainingspuppen.
- Waffe durchklicken: Holzschwert/Eisenschwert/Kriegsaxt werden aus
  `Resources/Sprites/Items/` geladen; die UI zeigt den PNG-Pfad bzw. einen Hinweis, falls der Platzhalter
  greift. Waffe bleibt am Griff, rotiert beim Schlag und liegt je Richtung vor/hinter dem Körper.
- Stufe auf +7: gelb-weißes Funkeln; +9: zusätzlich dichter blauer Glanz; +0..+6: kein Glitzern.
- "Laufen" und "Angriff" testen: Lauf-Demo-Wippen, Schlagrotation, Trail nur in der aktiven Schlagphase.
- Rüstung/Helm/Schild/Frisur/Haarfarbe durchklicken; Rüstung wechselt Form und Farbe, der Körper bleibt
  unverändert.
- Blickrichtung Unten/Oben/Links/Rechts testen (Links ist gespiegelt).
- "Trainingspuppen neu platzieren" und "Figur zurücksetzen" funktionieren; F10 schließt das Labor, danach
  sind Server-Figuren/Monster wieder normal sichtbar.
- **F12** oder "Screenshot aufnehmen" im Labor erzeugt sofort ein PNG unter `/tmp`; alternativ
  `tools/request-screenshot.sh` aus einer Shell, während Unity im Play-Modus läuft (kein Neustart).
- Icon-Galerie: Kategorie/Seite durchklicken; es werden 64 Icons je Seite aus dem importierten
  Lumiis-Archiv angezeigt (546 Icons, 34 Kategorien).
- "Charakter-Galerie zeigen": acht echte Lumiis-Figuren (Krieger/Ninja/Sura/Schamane, m/w) mit
  Waffe/Rüstung; der automatische Screenshot-Lauf legt `/tmp/lumiis-asset-lab-characters.png` ab.
- "Karten-Vorschau (Greenfield)": zeigt die importierte 80×80-Karte als Weltbild; der automatische
  Lauf legt `/tmp/lumiis-asset-lab-map.png` ab. Das ist nur eine Vorschau, noch keine spielbare
  zweite Karte (Server-Multi-Map siehe `docs/map-import-plan.md`).
- F1 zeigt außerhalb des Labors wieder das gewohnte Debug-Overlay und -Panel.

### Noch zu prüfen (Spiel-UI im klassischen MMO-Stil, vom User)
Hinweis: in der Cloud-Session ohne Unity entstanden - Batchmode-Compile-Check und
`tools/test-client-playmode.sh` stehen noch aus.
- Login: Logo und Anmeldefenster stehen mittig, auch nach Ändern der Fenstergröße; Enter im Passwortfeld
  meldet an; "Beenden" unten rechts.
- Charakterauswahl: Karten anklicken wählt aus (goldener Rahmen, Name unter der Vorschau), Doppelklick oder
  "Spiel starten" verbindet, "Zurück" meldet ab.
- HUD: HP-/SP-Kugeln füllen sich passend, EXP-Kugeln + Prozent, Schnellleiste 1-5 (Klick und Taste),
  Minimap rund mit Kartenausschnitt und Koordinaten, Ziel oben mittig.
- Klick auf Taskleiste/Fenster/Minimap bewegt die Figur nicht.
- I/C öffnen Inventar/Charakter, Fenster lassen sich an der Titelleiste ziehen, Escape schließt erst das
  oberste Fenster, danach öffnet es das Systemmenü (auch über "System" in der Taskleiste).
- Inventar: Tooltips, Klick legt an/benutzt, Rechtsklick auf Ausrüstung legt ab, Schleifstein -> Ausrüstung
  wertet auf (mit Haken "Bewahrungsrolle"). Rechtsklick auf Händler öffnet Shop + Inventar, Klick kauft,
  Rechtsklick im Inventar verkauft.
- Chat: Enter öffnet, Enter sendet, Escape schließt; WASD während des Tippens bewegt nicht.
