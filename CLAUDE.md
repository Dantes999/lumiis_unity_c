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
- Server: dotnet run --project server/src/Lumiis.Server (--data-dir überschreibt den Basispfad für Spieldaten, Default "data" relativ zum Arbeitsverzeichnis - lädt data/maps/village/collision.bin, siehe Abschnitt "Kartenkollision"; --allow-debug-commands NUR für lokale Entwicklung, siehe Abschnitt "Dev-Tools")
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
- POST /login { username, password } -> 200 { token, expiresAtUnix, characters: [{ id, name }] } | 401 (keine Unterscheidung Name/Passwort in der Antwort)
- GET /characters, Header "Authorization: Bearer <token>" -> 200 [{ id, name }] | 401
- POST /characters, Header "Authorization: Bearer <token>", Body { name } -> 201 { id, name } | 401 | 400 | 409 (Aussehen wird automatisch aus data/looks.json vergeben, `LookCatalog.CreateDefaultAppearance()` - erster gültiger Eintrag je Slot, keine Auswahl in M2a)

Fehlerantworten immer als knappes JSON { error: "..." }, nie ein Stacktrace nach außen. Session-Token: eigenes, minimales Format (kein JWT-Standard), signiert per HMACSHA256, 30 Minuten gültig, trägt nur die AccountId (siehe server/src/Lumiis.Data/TokenService.cs).

Spielserver-Join (Lumiis.Server, seit M2b, Protocol.Version 3): JoinRequestPacket trägt Token + CharacterId
statt Name. Ablauf: Token sofort synchron per TokenService.Validate geprüft (InvalidToken/TokenExpired als
unterschiedliche JoinRejectReason) -> bei gültigem Token läuft der Charakter-Lookup (CharacterId + AccountId
aus dem Token, Eigentümerschaft geprüft) asynchron auf einem Threadpool-Thread, Ergebnis kommt über eine
Queue zurück und wird einmal pro Tick eingesammelt (World bleibt ausschließlich dem Game-Loop-Thread
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
die Kartengrenzen geklemmt). Aktuell nur eine Karte, "village" - Mehrkarten-Unterstützung ist bewusst kein
Ziel, der Pfad `data/maps/village/collision.bin` ist in `Lumiis.Server/Program.cs` noch hartkodiert.

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

**`data/maps/village/collision-source.png` ist aktuell ein Platzhalter** (einfacher Rand + ein Rechteck +
eine Ellipse, per Pillow-Skript erzeugt, keine echte Kartengrafik) - der User ersetzt es später durch eine
echte, handbemalte Version, sobald Kartengrafik existiert. Danach `tools/bake-collision.py` erneut ausführen
und beide Dateien neu committen.

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
- `data/mobs.json`: Mob-Typen (Id, Name, SpeedWire, WanderRadiusWire, WanderIntervalMinMs/MaxMs). Aktuell zwei
  Platzhalter ("Wildhund" schnell/kurzer Intervall, "Waldschrat" langsam/langer Intervall). Geladen/validiert
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
- `GameClient.cs`: Orchestrierung - Views, Klick-Eingabe (New Input System, `Mouse.current`), Mausrad-Zoom, Kamera-Folgen, IMGUI-Debug-Overlay (Statuszeile, Entity-Liste, F1-Umschaltung) und F2-Testboard/F3-F8-Look-Hotkeys (nur Editor/Development-Build) - alles erst sichtbar nach erfolgreichem Join (`_inGame`-Flag, von LoginController unabhängig gepflegt). Login/Charakterauswahl selbst sind seit M2c LoginControllers Sache, nicht mehr Teil von GameClient.
- `LookCatalogClient.cs`: lädt `looks.json` aus StreamingAssets (JsonUtility).
- `ISpriteProvider.cs`/`PlaceholderSpriteProvider.cs`: liefert Sprites je Ebene/Richtung; aktuell zur Laufzeit erzeugte Formen, später ein `AtlasSpriteProvider` mit echten PNGs.
- `CharacterView.cs`/`CharacterAnimator.cs`/`CharacterTestboard.cs`: Paper-Doll-Zusammensetzung (Sortierung, Spiegeln, Anker, Tönung), prozedurale Idle-/Lauf-Animation, F2-Debug-Raster.
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
5. Rechts oben zeigt das Debug-Test-Panel (nur Editor/Development-Build) Buttons für Testboard/Sortier-Testfeld sowie "<"/">"-Paare für Frisur/Haarfarbe/Rüstung/Helm/Waffe/Schild der eigenen Figur (rein kosmetisch, nicht gesendet) - primärer Weg zum Durchklicken. F2/F3-F8/F9 bleiben als Tastatur-Shortcuts zusätzlich nutzbar.

Zwei Clients gleichzeitig testen: Editor plus Build. Der Play-Modus im Editor drosselt ohne Fensterfokus (siehe
"Bekannte Eigenheiten"), was bei mehreren gleichzeitigen Clients stört. Alternative: `tools/build-client.sh`
baut einen Development-Standalone-Build (Unity-Editor muss dafür geschlossen sein, Projekt-Lock). Danach einen
Client per Editor-Play und einen zweiten per `client/Builds/<plattform>/Lumiis` starten - der Build läuft ohne
Fokus-Drosselung, beide können gleichzeitig gegen denselben Server verbunden sein.

## Dev-Tools
`--allow-debug-commands` (Default aus, `ServerConfig.AllowDebugCommands`) schaltet Dev-Only-Befehle frei, die
sonst still ignoriert werden, als gäbe es sie nicht (kein Ablehnungspaket, keine Disconnect - nur ein
Log-Eintrag). **Nie in einem "echten" Serverstart aktivieren** - das ist ausschließlich für lokales
Kampf-Testing gedacht (`dotnet run --project server/src/Lumiis.Server -- --allow-debug-commands`, zusätzlich
zu den sonstigen Argumenten).

Aktuell ein Befehl: `DebugSpawnMonsterRequestPacket` (Client->Server, MobId) - spawnt ein Monster des
angegebenen Mob-Typs nahe der Position des Absenders (`World.FindNearbyWalkablePoint`, 100-300 Wire-Einheiten
Versatz in zufälliger Richtung, auf Kartengrenzen geklemmt, bei blockierter Zielzelle kein Retry sondern
Rückfall auf die Absenderposition selbst). Im Client: Debug-Testpanel (`#if UNITY_EDITOR ||
DEVELOPMENT_BUILD`) hat dafür einen Bereich "Monster spawnen" mit "<"/">"-Cycler + Knopf.

Ein so gespawntes Monster ist wie jedes andere behandelt (`World.TryAttack` kennt keinen Sonderfall) - der
einzige Unterschied ist `Entity.SpawnPointId == null` statt der echten `SpawnPoint.Id` aus spawns.json (die
regulären, beim Serverstart aus der SpawnCatalog erzeugten Monster haben immer eine gesetzte SpawnPointId).
Das ist bewusst so gebaut, damit ein künftiges Monster-Respawn-pro-SpawnPoint (M4b) sein Scheduling strikt an
`SpawnPointId.HasValue` hängen kann, nie an `Kind` allein - ein Debug-Monster löst dadurch strukturell nie
einen Respawn an einem (evtl. schon belegten) SpawnPoint aus.

## Alpha-Umfang (Ziel)
Map 1 (Startdorf + Feld), Krieger, Level 1-25, Monster/Bosse/Lumiis-Steine, Items/Drops/Shop, Chat, Gruppe. NICHT: Quests/Lua, Reittiere, Kostüme, Drachenseelen, Gildenkrieg, weitere Reiche.

## Meilensteine
M0 Fundament (fertig) | M1 Walking Skeleton (fertig, inkl. M1b Unity-Client, M1c Sichtbarkeit/Testing, M1c-layered-characters Paper-Doll-Charaktere) | M2 Account & Charakter (komplett fertig): M2a Datenbank & Auth-Dienst, M2b Server-Join über Account/Token/Charakter, M2c Unity-Login-UI (UI Toolkit) | M3 Welt (Grid, AOI, Mobs) komplett fertig: M3a Kartenkollision, M3b Sichtbarkeitsbereich (kein Grid - siehe Abschnitt "Sichtbarkeitsbereich (AOI)"), M3c Monster (Spawns, Wander-KI, AOI-Integration - siehe Abschnitt "Monster") | M4 Kampf komplett fertig (inkl. Gegenwehr): M4a Kampf-Grundlagen, M4b Monster-Respawn, M4c Monster-Aggro/Verfolgung/Leine (siehe Abschnitt "Kampf") | M5 Items (offen) | M6 Metin-Kern | M7 Sozial | M8 Alpha-Betrieb.
Aktuelle Aufgabe: keine - M4 (inkl. aller Unteraufgaben) ist fertig, für M5 gibt es noch keine Task-Datei unter docs/tasks/.

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

## Bekannte Lücken
- AOI (Sichtbarkeit): Aktuell sehen alle Verbindungen alle Entities, keine Kartenausschnitt-Filterung. Geplant für M3 (Grid, AOI, Mobs). Markiert mit TODO(M3) in `World.cs` und `GameServer.cs`.
- Rate-Limit pro Verbindung: Kein Schutz gegen Paketflut eines einzelnen Clients (z. B. MoveTo-Spam). Geplant für M8 (Alpha-Betrieb).
- Lumiis.Auth: kein Passwort-Reset, keine E-Mail-Verifizierung. Läuft nur über HTTP (kein TLS) - für ein echtes Deploy vor einen TLS-terminierenden Reverse-Proxy stellen.
- Positions-Speicherintervall (Lumiis.Server) fix bei 30s, keine Konfiguration (siehe `GameServer.PositionSaveIntervalMs`).
- Kein Token-Refresh: die Session (LoginController, Lumiis.Data.TokenService) endet nach 30 Minuten fix, danach muss sich der Client neu anmelden (JoinRejectReason.TokenExpired bzw. 401 auf GET/POST /characters schicken automatisch zurück zu LoggedOut).
- ResolveDefaultDataDir() existiert unabhängig in Lumiis.Server und Lumiis.Auth (Pfad-Kandidaten je nach typischem CWD beim Start). Bei künftigen data-dir-bezogenen Bugs beide Stellen prüfen, nicht nur eine. Eine gemeinsame Stelle (z. B. in Lumiis.Data) ist eine spätere Aufräumarbeit, kein aktueller Blocker.
- Verfolgungs-EntityMove-Pakete (M4c): ein aktiv ein Ziel verfolgendes Monster erzeugt jeden Tick (25x/s) ein neues EntityMovePacket an alle AOI-Beobachter (siehe Abschnitt "Kampf") - kein Throttling/Diffing, ähnlich der Rate-Limit-Lücke oben. Für die aktuelle Spielergröße unkritisch, bei mehr gleichzeitig verfolgenden Monstern eine spätere Optimierung.

## Status
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
