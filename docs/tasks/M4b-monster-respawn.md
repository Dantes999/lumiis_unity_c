# M4b – Monster-Respawn

Ziel: Ein an einem echten Spawn-Punkt gestorbenes Monster erscheint nach RespawnSeconds automatisch wieder an derselben Position. Debug-gespawnte Monster (SpawnPointId == null, aus den Dev-Tools) respawnen NICHT. M4 ist mit diesem Schritt komplett.

## 0. Review zuerst (nicht ändern, nur berichten, Datei:Zeile)
a) Wie genau markiert World/GameServer aktuell ein Monster als tot (aus M4a) - wo ist der Punkt, an dem "SpawnPointId hat einen Wert" abgefragt werden müsste, um Respawn-Scheduling auszulösen?
b) Wie sieht das bestehende Muster für den Spieler-Respawn aus M4a genau aus (RespawnAtMs, World.Tick-Prüfung) - lässt sich das 1:1 für Spawn-Punkte wiederverwenden, oder gibt es einen Unterschied (Spieler ist eine bestehende Entity, die wiederbelebt wird; ein respawntes Monster ist eine NEUE Entity mit neuer EntityId)?
c) Bestätige: Setzt SpawnCatalog/das Server-Start-Spawning aus M3c die RespawnSeconds pro Spawn-Punkt bereits korrekt, nur unbenutzt bisher?

## 1. Server: Respawn-Scheduling (World, kein Netzwerk, testbar)
- Beim Tod eines Monsters mit SpawnPointId: den zugehörigen Spawn-Punkt als "wartend, fällig bei nowMs + RespawnSeconds*1000" markieren, statt (wie bisher bei allen Monstern) einfach nur zu entfernen. Debug-gespawnte Monster (SpawnPointId == null) unverändert wie in M4a/Dev-Tools.
- World.Tick: fällige Spawn-Punkte erzeugen eine neue Monster-Entity (gleicher MobId, gleiche Werte aus MobCatalog, neue EntityId aus dem üblichen Zähler, Position = exakt der Spawn-Punkt, SpawnPointId gesetzt), danach ist der Spawn-Punkt wieder frei/unbelegt.
- Injizierbare Zeitquelle wie überall sonst im Projekt, für deterministische Tests.

## 2. Server: Netzwerk-Anbindung
- Neu erschienene Monster laufen durch den normalen AOI-Diff-Mechanismus aus M3b/M4a (EntitySpawn an Beobachter in Reichweite) - bestätige im Bericht, dass kein Sonderfall nötig ist, analog zur bereits bestätigten Aussage aus M3c/M4a zu diesem Muster.

## 3. Tests
- Unit (kein Netzwerk, injizierbare Zeit): Spawn-Punkt-Monster stirbt -> Entity verschwindet sofort (wie bisher), aber VOR RespawnSeconds erscheint an diesem Spawn-Punkt kein neues Monster; nach Ablauf von RespawnSeconds erscheint exakt eine neue Entity an der Spawn-Position mit vollem Leben; Debug-gespawntes Monster (SpawnPointId == null) stirbt -> kein Respawn, egal wie viel Zeit vergeht; zwei Spawn-Punkte respawnen unabhängig voneinander mit ihren jeweiligen RespawnSeconds-Werten.
- Integration (LiteNetLib Loopback): Bot tötet ein Spawn-Punkt-Monster, wartet (mit kurzer RespawnSeconds für den Test, z. B. per Testdaten mit 2s statt der echten Werte) über die Respawn-Zeit hinweg, bekommt EntitySpawn für das neu erschienene Monster am selben Ort.
- Alle bisherigen Tests bleiben grün.

## 4. Doku
CLAUDE.md: Abschnitt "Monster" (aus M3c) und "Kampf" (aus M4a) um Respawn-Verhalten ergänzen, Status/Meilensteine aktualisieren (M4 komplett abgeschlossen, M5 offen: Items).

## Fertig, wenn
- dotnet build && dotnet test grün (5x stabil).
- Mit laufendem Auth+Server+Bot: ein Spawn-Punkt-Monster (nicht debug-gespawnt) wird getötet, Log zeigt nach der konfigurierten RespawnSeconds-Zeit ein neues EntitySpawn derselben MobId an derselben Position.
- Ein Commit pro Abschnitt.
