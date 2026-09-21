# M1b – Unity-Client (Walking Skeleton, sichtbar)

Ziel: Ein Unity-Client, der sich mit dem M1-Server verbindet, einloggt, Klick-Bewegung schickt und alle Spieler (auch Bots) als bunte Quadrate laufen sieht. Bewegung wird aus EntityMove/EntityStop interpoliert. Keine Kunst, keine Datenbank, keine Prediction.

## 0. Vorab
- Kurzcheck ohne Umbau: TestFaultHook muss in Produktion unerreichbar sein (internal, Default null, kein öffentliches API). Nur korrigieren, wenn das nicht stimmt.
- Prüfe client/ProjectSettings: Unity 6000.3.24f1, activeInputHandler (welches Input-API ist aktiv?) und apiCompatibilityLevel (muss .NET Standard 2.1 sein). Nutze das passende Input-API (bei New Input System `Mouse.current`, nicht `Input.GetMouseButtonDown`).

## 1. Shared/Server-Seite (server/, Tests grün halten)
- `Protocol.ConnectionKey = "lumiis"` in Shared (falls der Schlüssel noch im Server hartkodiert ist). Server, Bot und Unity nutzen die Konstante.
- Neue reine Funktion `MovementMath` in Shared (netstandard2.1, C# 9): Position einer Bewegung nach `elapsedSeconds` aus Start, Ziel und Speed (int, 1/100 Einheiten pro Sekunde). Liefert x, y (double, in 1/100 Einheiten) und `arrived`. Kein Überschießen. Speed <= 0 oder Distanz 0 bedeutet sofort am Ziel.
- xUnit-Tests: t=0, Mitte, exakte Ankunft, weit nach Ankunft, Distanz 0, Speed 0, diagonal. Dazu ein Vertragstest: Serverbewegung (Entity.Tick, N Ticks à 1000/Protocol.TickRate ms) und MovementMath stimmen nach N Ticks auf 0,01 Einheiten (also 1 in 1/100) überein.
- MSBuild-Kopierschritt: Nach jedem Build von Lumiis.Shared wird Lumiis.Shared.dll nach client/Assets/Plugins/Lumiis/ kopiert, ebenso LiteNetLib.dll (aus dem NuGet-Paket, netstandard-Variante; prüfe, welche Target-Frameworks das Paket enthält, und nimm keine net8+/net10-Variante). Nur wenn client/Assets existiert, Ordner bei Bedarf anlegen, SkipUnchangedFiles, plattformunabhängig (Linux und Windows). Keine PDBs.
- .gitignore: client/Assets/Plugins/ und client/Assets/Plugins.meta ignorieren (Binärartefakte, entstehen per dotnet build).

## 2. Unity-Code (client/Assets/Lumiis/Scripts/, Namespace Lumiis.Client)
Regeln: code-first (kein Inspector-Verkabeln, keine Szenenänderung, alles wird von einem RuntimeInitializeOnLoadMethod-Bootstrap erzeugt), keine veränderlichen statischen Zustände, vorerst kein asmdef, kein UI Toolkit in dieser Aufgabe, nur Unity-6.3-APIs, die du sicher kennst. `ClientWorld` enthält keine UnityEngine-Typen.
- Bootstrap: RuntimeInitializeOnLoadMethod (AfterSceneLoad). Erzeugt ein Root-GameObject mit den Komponenten unten, setzt Application.runInBackground = true, konfiguriert Camera.main orthographisch (legt eine Kamera an, falls keine existiert).
- NetClient (MonoBehaviour): LiteNetLib NetManager (UpdateTime = 15), Connect(host, port, Protocol.ConnectionKey), PollEvents() in Update. Nach dem Verbinden JoinRequest (Protocol.Version, Name; Name vorab mit NameRules.IsValid prüfen). Eingehende Bytes per PacketCodec.Decode, Fehler loggen und Verbindung trennen. Stop() in OnDestroy und OnApplicationQuit. C#-Events für Verbunden, Getrennt (mit Grund) und Paket.
- ClientWorld (reines C#): Entities per EntityId, verarbeitet JoinAccepted, EntitySpawn, EntityDespawn, EntityMove, EntityStop. Position per MovementMath aus dem Bewegungssegment und einer übergebenen Zeit (double), kein Time.* in der Klasse. EntityStop setzt die Position exakt. Unbekannte EntityIds bei Move/Stop/Despawn: ignorieren und warnen. Zeitbasis ist die Empfangszeit auf dem Client (keine Uhrensynchronisation in M1b).
- Ansicht: pro Entity ein GameObject mit SpriteRenderer und zur Laufzeit erzeugtem Platzhalter-Sprite (einfarbiges Quadrat per Texture2D), lokaler Spieler in anderer Farbe. Welt-Einheit = 1 Server-Einheit (Wire-Wert / 100), Y nach oben. Ein sichtbarer Boden (Raster oder Schachbrett, ca. alle 10 Einheiten) und eine Kartenbegrenzung, damit Bewegung erkennbar ist.
- Kamera folgt dem lokalen Spieler (Ortho-Größe ca. 8).
- Eingabe: Linksklick, wenn eingeloggt: Bildschirm zu Welt zu MoveTo (in int-Wire-Werte umrechnen). Das Klemmen an die Kartengrenzen bleibt Sache des Servers.
- Debug-UI (IMGUI/OnGUI, ausdrücklich temporär, in M2 wird sie durch UI Toolkit ersetzt): Login-Fenster (Host Default 127.0.0.1, Port 7777, Name, Verbinden-Knopf), Statuszeile (Verbunden, JoinRejected mit Klartext je JoinRejectReason, Getrennt), eigene Position, Anzahl Entities, Ping. Entity-Namen über den Quadraten (WorldToScreenPoint).
- Beim Trennen: Ansichten aufräumen, zurück zum Login.

## 3. Prüfung
Du kannst den Unity-Editor nicht bedienen. Versuche einmal eine Kompilierprüfung im Batchmode (`Unity -batchmode -nographics -quit -projectPath client -logFile -`, Editor unter dem Hub-Pfad) oder über die Unity CLI. Klappt Lizenz oder Headless nicht, gib dem User eine kurze Checkliste (Console auf Fehler prüfen, Play drücken, erwartetes Verhalten).

## Fertig, wenn
- `cd server && dotnet build && dotnet test` grün (5x stabil). Danach liegen Lumiis.Shared.dll und LiteNetLib.dll in client/Assets/Plugins/Lumiis/ (git-ignoriert).
- Der Unity-Editor öffnet das Projekt ohne Compilerfehler (der User bestätigt das).
- Demo: Server läuft, Unity im Play-Modus verbindet sich, das eigene Quadrat läuft per Klick, und `Lumiis.Bot -- --count 3` erscheint als 3 weitere laufende Quadrate.
- CLAUDE.md: Abschnitt "Client" (Setup nach dem Klonen: erst `cd server && dotnet build`, dann Unity öffnen; Struktur; Demo-Ablauf), "Aktuelle Aufgabe" und Status aktualisiert. Ein Commit pro Teilschritt: Shared/MovementMath, Kopierschritt, Unity-Code, Doku.
