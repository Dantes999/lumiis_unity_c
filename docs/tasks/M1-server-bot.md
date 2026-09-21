# M1 – Server, Bot, Bewegung (Walking Skeleton)

Ziel: Ein autoritativer Server, auf dem sich mehrere Clients (zunächst Konsolen-Bots) einloggen, in einer Map bewegen und gegenseitig sehen. Der Unity-Client ist eine eigene Aufgabe (M1b). Hier: nur Shared, Server, Bot, Tests.

## Shared (netstandard2.1 / C# 9, keine neuen Abhängigkeiten)
Neue Server->Client-Pakete (je PacketId, Factory-Eintrag, Round-trip-Test):
- JoinAccepted (1000): EntityId:int, X:int, Y:int
- JoinRejected (1001): Reason:byte (enum JoinRejectReason: VersionMismatch=1, InvalidName=2, NameTaken=3, ServerFull=4)
- EntitySpawn (1002): EntityId:int, Name:string, X:int, Y:int
- EntityDespawn (1003): EntityId:int
- EntityMove (1004): EntityId, StartX, StartY, TargetX, TargetY (alle int), Speed:int (Einheiten/s in 1/100), ServerTick:uint. Der Client interpoliert selbst (Metin2-Stil), es gibt keine Positions-Broadcasts pro Tick.
- EntityStop (1005): EntityId:int, X:int, Y:int
Konstanten: MapWidth/MapHeight (100000 x 100000, also 1000 x 1000 Einheiten), PlayerSpeed (500 = 5 Einheiten/s), Namenslänge 3..16.
`NameRules.IsValid` als reine Funktion in Shared: 3-16 Zeichen, nur Buchstaben (char.IsLetter), Ziffern, Unterstrich. Mit Tests.

## Server (net10.0, NuGet: LiteNetLib)
- `Game/`: reine Logik ohne Netzwerk (World, Entity/Player, Movement pro Tick).
- `Net/`: dünner LiteNetLib-Adapter (Verbindungs-Events in die Logik, Senden von IPacket).
- `GameServer`: verbindet beides. `Update()` = ein Tick (manuell aufrufbar für Tests), `Run(CancellationToken)` = fester 25-Hz-Loop (Stopwatch, Catch-up begrenzt).
- `Program.cs`: Args `--port 7777`, `--max-players 100`. Ctrl+C = sauberer Shutdown.
- Logging: kleine eigene Konsolen-Logger-Klasse (Zeitstempel, Level), kein Framework.

Verhalten:
1. Verbindungsschlüssel "lumiis". Clients ohne gültiges JoinRequest innerhalb von 5 s werden getrennt.
2. JoinRequest: ProtocolVersion prüfen, Name validieren, Name eindeutig (Groß-/Kleinschreibung egal), Spielerlimit. Bei Fehler JoinRejected senden, danach trennen (so, dass das Paket noch ankommt). Bei Erfolg: Spawn am Kartenzentrum. Der Joiner erhält JoinAccepted, dann EntitySpawn für alle vorhandenen Entities (laufende Bewegungen zusätzlich als EntityMove ab aktueller Position). Alle anderen erhalten EntitySpawn des Joiners. Alle sehen alle, die Sichtbarkeit (AOI) folgt in M3: Stelle mit TODO markieren.
3. MoveTo: nur nach erfolgreichem Join. Ziel auf die Kartengrenzen klemmen. Bewegung setzen, EntityMove an alle.
4. Pro Tick: Bewegung mit Speed fortschreiben. Serverintern darf double genutzt werden, auf der Leitung nur int. Bei Ankunft exakt auf das Ziel snappen und EntityStop an alle senden. Kein Überschießen.
5. Disconnect: EntityDespawn an alle, Name wird wieder frei.
6. Kaputte oder unerwartete Pakete (PacketFormatException, MoveTo vor Join, doppeltes JoinRequest): Verbindung trennen, loggen. Der Server stürzt nie ab.
7. Server->Client: reliable ordered.

## Bot (net10.0, Lumiis.Bot)
Args: `--host 127.0.0.1 --port 7777 --count 1 --name Bot`. Startet `count` Clients (Bot1..BotN). Jeder joint und sendet alle 1-3 s ein zufälliges MoveTo. Eingehende Spawns/Moves/Stops knapp loggen. Ein gemeinsamer Poll-Loop im Hauptthread.

## Tests
- Unit (ohne Netzwerk): Movement (Ankunft exakt am Ziel, kein Überschießen, diagonal, Speed), Klemmung an Kartengrenzen, NameRules, Join/Despawn-Logik in World.
- Integration (echtes LiteNetLib auf Loopback, freier Port): 2 Clients joinen, B sieht Spawn von A. A sendet MoveTo, B empfängt EntityMove und später EntityStop mit exakter Zielposition. A trennt, B empfängt EntityDespawn. Falsche ProtocolVersion und ungültiger Name führen zu JoinRejected. Müll-Paket: Verbindung wird getrennt, Server lebt weiter.
- Keine flackernden Tests: Server.Update() und Clients manuell pumpen, mit Timeout (max. 5 s), keine festen Sleeps.
- Alle bisherigen 23 Tests bleiben grün.

## Fertig, wenn
- `cd server && dotnet build` ohne Fehler, `dotnet test` grün.
- Server und `Lumiis.Bot -- --count 3` laufen: Bots joinen, laufen, sehen sich (im Log erkennbar).
- Status in CLAUDE.md aktualisiert. Ein Commit pro Teilschritt: Shared, Game-Logik, Transport/GameServer, Bot, Integrationstests.
