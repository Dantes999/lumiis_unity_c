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
- Server: dotnet run --project server/src/Lumiis.Server
- Bot: dotnet run --project server/src/Lumiis.Bot -- --count 3
- Umgebungsvariablen laden: source tools/dev-env.sh (mit "source", NICHT ./ - sonst gelten die Variablen nur im Subprozess des Skripts). Davor nötig für docker compose, dotnet ef, dotnet run --project server/src/Lumiis.Auth UND (seit M2b) server/src/Lumiis.Server.
- Postgres starten: docker compose up -d (Repo-Root, nach source tools/dev-env.sh)
- Migration anwenden: cd server/src/Lumiis.Auth && dotnet ef database update --project ../Lumiis.Data (nach source tools/dev-env.sh; Migrationen liegen in Lumiis.Data, Lumiis.Auth referenziert Microsoft.EntityFrameworkCore.Design zusätzlich selbst, damit es als Startup-Projekt für dotnet ef taugt)
- Auth-Dienst: dotnet run --project server/src/Lumiis.Auth (nach source tools/dev-env.sh; Port 5080, siehe Abschnitt "Datenbank & Auth-Dienst")

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

## Alpha-Umfang (Ziel)
Map 1 (Startdorf + Feld), Krieger, Level 1-25, Monster/Bosse/Lumiis-Steine, Items/Drops/Shop, Chat, Gruppe. NICHT: Quests/Lua, Reittiere, Kostüme, Drachenseelen, Gildenkrieg, weitere Reiche.

## Meilensteine
M0 Fundament (fertig) | M1 Walking Skeleton (fertig, inkl. M1b Unity-Client, M1c Sichtbarkeit/Testing, M1c-layered-characters Paper-Doll-Charaktere) | M2 Account & Charakter (komplett fertig): M2a Datenbank & Auth-Dienst, M2b Server-Join über Account/Token/Charakter, M2c Unity-Login-UI (UI Toolkit) | M3 Welt (Grid, AOI, Mobs) | M4 Kampf | M5 Items | M6 Metin-Kern | M7 Sozial | M8 Alpha-Betrieb.
Aktuelle Aufgabe: keine - M2 (inkl. aller Unteraufgaben) ist fertig, für M3 gibt es noch keine Task-Datei unter docs/tasks/.

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
