# Lumiis – Projektbriefing für Claude Code

Unity-2D-Online-RPG. Mechaniken 1:1 wie Metin2 "unter der Haube", eigene Optik (Pixelart/Comic). Am Anfang wenige Spieler. "Lumiis" ist auch der eigene Name für Metin-Steine im Spiel. Getrennter, autoritativer Server.

## Verbindliche Architektur
- Server: C# / .NET 10, ein Prozess, EIN Game-Loop-Thread, 25 Ticks/s. DB-Zugriffe (ab M2) asynchron, nie den Loop blockieren.
- Netzwerk: LiteNetLib (reliable ordered). Server ist autoritativ. Der Client schickt nur Absichten (MoveTo, Attack, ...) und rendert Serverzustand. Keine Client-Prediction im MVP.
- server/src/Lumiis.Shared: netstandard2.1, LangVersion 9.0, KEINE Abhängigkeiten. Enthält Pakete, Reader/Writer, Enums, Konstanten, Kampfformeln. Wird später als DLL nach client/Assets/Plugins kopiert. Deshalb dort: keine records/init, keine file-scoped namespaces, keine Reflection, kein veränderlicher statischer Zustand, keine Floats auf der Leitung.
- Koordinaten auf der Leitung: int in 1/100 Welteinheit.
- Spiellogik (World, Entities, Movement, Combat) kennt weder LiteNetLib noch Sockets und ist ohne Netzwerk testbar. Transport-Adapter sind dünn.
- Spieldaten (Mobs, Items, Skills, Drops, Maps) als JSON in /data (Prinzip mob_proto/item_proto), beim Serverstart validiert. Nichts hartkodieren, was Spieldaten sind.
- DB ab M2: PostgreSQL + EF Core (docker compose). Vorher keine DB.
- Client: Unity 6.3 LTS (6000.3.24f1), 2D Top-Down, 4 Richtungen, code-first (Bootstrap-Szene erzeugt alles per Code, Platzhalter-Grafik wird generiert), UI per UI Toolkit (UXML/USS). Kein Inspector-Verkabeln, keine veränderlichen statischen Zustände (Fast Enter Play Mode / CoreCLR).
- Tests: xUnit (server/tests) plus Konsolen-Bot (Lumiis.Bot).

## Repo
client/ (Unity-Projekt), server/ (Solution mit Shared, Server, Bot, Tests), data/, docs/, tools/.

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

## Befehle
- Build/Test: cd server && dotnet build && dotnet test
- Server: dotnet run --project server/src/Lumiis.Server
- Bot: dotnet run --project server/src/Lumiis.Bot -- --count 3

## Client
Setup nach dem Klonen (auch auf dem zweiten Rechner):
1. `cd server && dotnet build` - baut Lumiis.Shared und kopiert dabei Lumiis.Shared.dll sowie die netstandard2.1-Variante von LiteNetLib.dll nach `client/Assets/Plugins/Lumiis/` (git-ignoriert, entsteht bei jedem Build neu).
2. Danach `client/` mit Unity Hub öffnen (6000.3.24f1). Beim ersten Öffnen importiert Unity die kopierten Plugin-DLLs und legt die zugehörigen .meta-Dateien an.

Struktur (`client/Assets/Lumiis/Scripts/`, Namespace `Lumiis.Client`, code-first, kein Inspector-Verkabeln, kein asmdef):
- `Bootstrap.cs`: RuntimeInitializeOnLoadMethod (AfterSceneLoad), erzeugt Kamera und das Root-GameObject mit NetClient/GameClient.
- `NetClient.cs`: dünner LiteNetLib-Adapter (MonoBehaviour) - Connect/JoinRequest, PacketCodec.Decode, C#-Events für Connected/Disconnected/Paket.
- `ClientWorld.cs`: reine Weltsicht ohne UnityEngine-Bezug und ohne `Time.*`; Bewegung wird über `MovementMath` (Shared) anhand einer von außen übergebenen Zeit interpoliert (Client-Empfangszeit, keine Uhrensynchronisation in M1b), Blickrichtung über `FacingMath` (Shared) aus dem Bewegungsvektor. JoinAccepted (eigene Figur) und EntitySpawn (andere) laufen beide durch denselben `ApplyAppearance`-Pfad.
- `GameClient.cs`: Orchestrierung - Views, Klick-Eingabe (New Input System, `Mouse.current`), Mausrad-Zoom, Kamera-Folgen, temporäre IMGUI-Debug-UI (wird in M2 durch UI Toolkit ersetzt) mit F1-Umschaltung, F2-Testboard und F3-F8-Look-Hotkeys (nur Editor/Development-Build).
- `LookCatalogClient.cs`: lädt `looks.json` aus StreamingAssets (JsonUtility).
- `ISpriteProvider.cs`/`PlaceholderSpriteProvider.cs`: liefert Sprites je Ebene/Richtung; aktuell zur Laufzeit erzeugte Formen, später ein `AtlasSpriteProvider` mit echten PNGs.
- `CharacterView.cs`/`CharacterAnimator.cs`/`CharacterTestboard.cs`: Paper-Doll-Zusammensetzung (Sortierung, Spiegeln, Anker, Tönung), prozedurale Idle-/Lauf-Animation, F2-Debug-Raster.
- `PlaceholderSprites.cs`: Boden-Raster und Kartenbegrenzung (zur Laufzeit erzeugt, keine Kunst-Assets).

Demo-Ablauf:
1. Server starten: `dotnet run --project server/src/Lumiis.Server`.
2. Unity-Projekt öffnen, Play drücken. Im Login-Fenster Host/Port/Name eingeben (Default 127.0.0.1:7777), "Verbinden" klicken.
3. Linksklick auf den Boden bewegt die eigene Figur dorthin, sichtbar interpoliert und mit Blickrichtungswechsel.
4. Bots dazuholen: `dotnet run --project server/src/Lumiis.Bot -- --count 6` - erscheinen mit unterschiedlichen, deterministisch aus dem Namen vergebenen Looks (siehe `LookCatalog.CreateAppearanceForName`).
5. F2 zeigt das Testboard (Body x alle Rüstungen x 4 Facings), F3-F8 wechseln lokal Haarstil/-farbe/Rüstung/Helm/Waffe/Schild der eigenen Figur (rein kosmetisch, nicht gesendet).

## Alpha-Umfang (Ziel)
Map 1 (Startdorf + Feld), Krieger, Level 1-25, Monster/Bosse/Lumiis-Steine, Items/Drops/Shop, Chat, Gruppe. NICHT: Quests/Lua, Reittiere, Kostüme, Drachenseelen, Gildenkrieg, weitere Reiche.

## Meilensteine
M0 Fundament (fertig) | M1 Walking Skeleton (fertig, inkl. M1b Unity-Client, M1c Sichtbarkeit/Testing, M1c-layered-characters Paper-Doll-Charaktere) | M2 Account & Charakter | M3 Welt (Grid, AOI, Mobs) | M4 Kampf | M5 Items | M6 Metin-Kern | M7 Sozial | M8 Alpha-Betrieb.
Aktuelle Aufgabe: keine - M1 (inkl. aller Unteraufgaben) ist fertig, für M2 gibt es noch keine Task-Datei unter docs/tasks/.

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
- Authentifizierung: JoinRequest prüft nur Name/ProtocolVersion, kein Login/Account. Geplant für M2 (Account & Charakter).

## Status
- M0 fertig. Shared: PacketWriter/PacketReader/PacketCodec, PacketId (JoinRequest, MoveTo), 23 Tests grün.
- M1 (Server, Bot, Bewegung) fertig, in 5 Commits: Shared (Server->Client-Pakete 1000-1005, GameConstants, NameRules), Game-Logik (World/Entity, reine Bewegung pro Tick), Transport/GameServer (LiteNetLib-Adapter, fester 25-Hz-Loop, Join/Move/Disconnect-Handling), Bot (Lumiis.Bot joint und läuft zufällig), Integrationstests (echtes LiteNetLib auf Loopback). 62 Tests grün, manuell mit Server + `Lumiis.Bot -- --count 3` getestet. M1b (Unity-Client) ist eine eigene, noch offene Aufgabe.
- M1-Fixes (Architektur-Review-Ergebnis) fertig, in 3 Commits: (1) Exceptions dürfen den Server nie beenden - jeder Netzwerk-Callback fängt Fehler ab, nur die betroffene Verbindung wird getrennt, 50 fehlerhafte Ticks in Folge lösen einen sauberen Shutdown mit Exit-Code 1 aus; (2) Fristen laufen in Wandzeit (ms) mit injizierbarer Uhr statt in Ticks; (3) Paketgrößenlimit (1200 Bytes, vor dem Decode geprüft) und NameRules nur noch ASCII (Homoglyph-Schutz). 67 Tests grün, 5x hintereinander stabil.
- M1b (Unity-Client) fertig, in 4 Commits: Shared (Protocol.ConnectionKey, MovementMath + Vertragstest gegen Entity.Tick), MSBuild-Kopierschritt nach client/Assets/Plugins/Lumiis/, Unity-Code (Bootstrap/NetClient/ClientWorld/GameClient, s. Abschnitt "Client"), Doku. 77 Tests grün, 5x stabil. Unity-Batchmode-Kompilierlauf (Lizenz vorhanden) lief fehlerfrei durch (0 Compiler-Fehler); Play-Modus/Demo wurden nicht automatisiert geprüft, siehe Prüf-Checkliste unten. Nebenbei einen Bug in .gitignore behoben, der seit M0 alle Unity-.meta-Dateien verschluckt hat.
- M1c (Sichtbarkeit beim Testen) fertig, in 3 Commits: (1) Bot `--radius` (Default 20 Einheiten) für Zielwahl um den Spawn-Punkt, klemmt auf Kartengrenzen, `--radius 0` = ganze Karte wie bisher; Zielwahl in `BotTargetPicker` ausgelagert und getestet. (2) Zoom (Mausrad, Orthographic Size 3-300, nur Vorzeichen des Scroll-Werts wegen Plattformunterschieden) und F1-Umschaltung fürs Debug-Overlay; Overlay zeigt jetzt zusätzlich eine nach Entfernung sortierte Entity-Liste (max. 12 Zeilen); Debug.Log bei EntitySpawn/EntityDespawn. (3) Auf User-Meldung hin (Bots, die nach dem Unity-Client joinen, erscheinen nicht) `ClientWorld` per Compile-Include in `Lumiis.Tests` testbar gemacht und den Empfangspfad untersucht - siehe unten, Ursache noch offen. 91 Tests grün, 3-5x stabil. Batchmode-Kompilierlauf konnte für Punkt 2-4 nicht laufen, da der User-eigene Editor das Projekt offen hatte; die neu genutzten Input-System-APIs wurden stattdessen im Paket-Quellcode verifiziert.
- M1c-layered-characters (Ebenen-Charaktere/Paper Doll mit Platzhaltern) fertig, in 4 Commits: (1) data/looks.json (Platzhalter-Katalog) plus Shared `Appearance` (Struct, eine Write/Read-Implementierung für JoinAccepted UND EntitySpawn) und `Facing`/`FacingMath` (Hysterese gegen Flackern). (2) Server: `LookCatalog` lädt/validiert looks.json (`--data-dir`), Appearance-Vergabe deterministisch aus dem Namen per FNV-1a - bewusst serverintern, Übergangslösung bis M2; Protocol.Version 2. (3) Client: `LookCatalogClient` (JsonUtility), `PlaceholderSpriteProvider`, `CharacterView`/`CharacterAnimator`/`CharacterTestboard` - Sortierung, Spiegeln, Hand-/Kopf-Anker, Haar-Tönung, hidesHair, F2-Testboard, F3-F8 lokale Look-Hotkeys. `EntityView` entfernt (durch `CharacterView` ersetzt). (4) Doku: neue Konventionen (Asset-Herkunft, Client berechnet nie Spielzustand), docs/art-provenance.md angelegt. 133 Tests grün, 5x stabil. Unity-Batchmode-Kompilierlauf: 0 Compiler-Fehler.

### Geschlossen: später beitretende Entities erschienen nicht im Unity-Client
War kein Codefehler. User hat den Test mit fokussiertem Unity-Fenster wiederholt (Server, Unity Play, dann Bots
gestartet, ins Unity-Fenster geklickt): alle Spawns kamen an, auch mit 99 Bots. Wahrscheinliche Ursache war die
Drosselung der Editor-Update-Schleife ohne Fensterfokus (siehe "Bekannte Eigenheiten" unten) - kein
Server-/Client-Bug. Die dabei entstandenen Tests (ClientWorld ohne Unity testbar, Integrationstests für spätere
Joiner/Burst) bleiben als nützliche Regressionsabdeckung erhalten.

### Noch zu prüfen (Editor/Play-Modus, vom User)
- Unity öffnet `client/` ohne Compiler-Fehler in der Console (Batchmode-Check war bereits grün, Editor-Check zur Sicherheit trotzdem empfohlen).
- Play drücken: Login-Fenster erscheint, Verbinden mit laufendem Server klappt, eigenes Quadrat erscheint, Klick auf den Boden bewegt es sichtbar interpoliert zum Zielpunkt.
- `Lumiis.Bot -- --count 3` parallel starten: 3 weitere Quadrate mit Namen erscheinen und laufen zufällig (siehe offenen Punkt oben, falls nicht).
- Server stoppen bzw. Verbindung kappen: Client zeigt "Getrennt", räumt alle Quadrate auf und zeigt wieder das Login-Fenster.
- Ungültiger Name (z. B. "ab") bzw. falscher Port testen: Statuszeile/Login zeigt eine verständliche Fehlermeldung statt eines stillen Hängers.
- Mausrad-Zoom (3-300) und F1-Overlay-Umschaltung testen; Entity-Liste im Overlay zeigt Id/Name/Position/Entfernung, sortiert, max. 12 Zeilen.
- Paper-Doll-Charaktere: Figuren zeigen Körper+Rüstung+Haare+ggf. Helm/Waffe/Schild statt eines Quadrats, drehen sich beim Laufen sichtbar (Down/Up/Side, Side gespiegelt bei Linkslauf), Waffe schwingt beim Gehen um den Griff (nicht um die Füße), Haare sind eingefärbt (nicht weiß), ein Helm mit hidesHair blendet die Haare korrekt aus.
- F2 zeigt/versteckt ein Testboard (Raster aus Body x allen Rüstungen x 4 Facings) an einer festen Stelle neben der Karte.
- F3-F8 wechseln bei der eigenen Figur sichtbar Haarstil/-farbe/Rüstung/Helm/Waffe/Schild, ohne dass sich andere Spieler dadurch ändern (rein lokal).
- Sortierung zweier sich überlappender Figuren: Ebenen der einen Figur mischen sich nicht mit denen der anderen (z. B. Haar von A nicht hinter Rüstung von B).
