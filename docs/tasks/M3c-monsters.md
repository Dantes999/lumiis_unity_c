# M3c – Monster (Spawns, einfache Wander-KI, AOI-Integration)

Ziel: Monster existieren als Entities auf der Karte, bewegen sich gelegentlich in einem kleinen Radius um ihren Spawn-Punkt, respektieren die Kollisionskarte, und sind über denselben AOI-Mechanismus wie Spieler sichtbar. Noch KEIN Kampf, kein Sterben, kein aktiver Respawn-Timer (Datenfeld dafür wird vorbereitet, aber nicht ausgelöst - das kommt mit M4, wenn es etwas zum Wiederbeleben gibt).

## 0. Review zuerst (nicht ändern, nur berichten, Datei:Zeile)
a) Macht der AOI-Diff-/Spawn-Sendecode aus M3b irgendwo eine Annahme, die nur für Spieler-Entities gilt (z. B. dass jede Entity eine eigene Verbindung/einen Account hat)? Monster haben keine Verbindung.
b) Wie ist Entity/World aktuell strukturiert - reicht ein zusätzliches Feld (Kind, MobId) an der bestehenden Entity-Klasse, oder gibt es gute Gründe für eine Unterklasse/Interface? Deine Empfehlung mit kurzer Begründung.
c) Wie vergibt World aktuell EntityId (reiner Zähler)? Reicht das, damit Monster beim Serverstart IDs bekommen, die nicht mit später joinenden Spielern kollidieren?

## 1. Daten
- data/mobs.json: Liste von Mob-Typen (Id, Name, SpeedWire, WanderRadiusWire, WanderIntervalMinMs/MaxMs). Zwei Platzhalter: "Wildhund" (schneller, kleinerer Interval - wandert öfter) und "Waldschrat" (langsam, großer Interval - wirkt fast stationär).
- data/maps/village/spawns.json: Liste von Spawn-Punkten (Id, MobId, PositionX/Y wire, RespawnSeconds - Feld existiert im Schema, wird in M3c NICHT ausgewertet, nur vorbereitet für M4). Ein paar Wildhund-Punkte und ein paar Waldschrat-Punkte, an Stellen, die laut collision.bin begehbar sind.

## 2. Shared (netstandard2.1 / C# 9)
- EntityKind enum (byte): Player, Monster.
- EntitySpawnPacket: EntityKind und MobId (int, bei Player ungenutzt/0) ergänzen. Appearance-Feld bleibt im Paket (bei Monstern mit neutralen/Default-Werten befüllt, keine Bedeutung).
- Protocol.Version auf 4.
- Tests: Round-trip mit beiden EntityKind-Werten.

## 3. Server: Katalog und Spawning
- MobCatalog (analog zu LookCatalog): lädt/validiert mobs.json, eindeutige IDs, Startabbruch mit klarer Meldung bei Fehlern.
- SpawnCatalog: lädt/validiert spawns.json. Jeder MobId muss in MobCatalog existieren, jede Position muss laut CollisionMap begehbar sein - beides beim Start prüfen, bei Verstoß Startabbruch mit Angabe, welcher Spawn-Punkt betroffen ist.
- Beim Serverstart: für jeden Spawn-Punkt eine Monster-Entity in World anlegen (EntityId aus demselben Zähler wie Spieler, Kind=Monster, MobId, Anker=Spawn-Position).

## 4. Wander-KI (World, kein Netzwerk, testbar)
- Pro Monster: merkt sich Anker (Spawn-Position) und wann die nächste Zielwahl fällig ist (injizierbare Zeitquelle wie an anderer Stelle im Projekt, für deterministische Tests).
- Bei Fälligkeit: zufälliger Punkt innerhalb WanderRadiusWire um den Anker (injizierbare Zufallsquelle), auf Kartengrenzen geklemmt. Ist die Zielzelle blockiert, diesen Zyklus überspringen (kein Retry-Loop). Bewegung nutzt denselben Mechanismus wie Spieler-MoveTo (inklusive Kollisions-Klemmung entlang der Linie aus M3a) - keine neue Bewegungslogik.
- Danach neue Fälligkeit zufällig zwischen WanderIntervalMinMs/MaxMs des Mob-Typs bestimmen.
- Läuft als Teil von World.Tick, nach der Spieler-Bewegung (Reihenfolge willkürlich, aber konsistent).

## 5. AOI/Versand
- Bestätige (oder widerlege mit Fix), dass der bestehende M3b-Sichtbarkeitscode Monster ohne Änderung korrekt behandelt (Sichtbarkeit ist positionsbasiert, kennt keine Entity-Kind-Unterscheidung). EntitySpawn für Monster trägt jetzt Kind=Monster und die MobId.

## 6. Client (minimal, Platzhalter, KEINE echte Kunst)
- Bei EntitySpawn mit Kind=Monster: statt CharacterView eine einfache MonsterView (SpriteRenderer mit einer per Code erzeugten Platzhalterform, z. B. Kreis für Wildhund, Dreieck für Waldschrat, unterschiedliche Farbe je MobId - reuse PlaceholderSprites-Infrastruktur aus M1c). Bewegungsinterpolation (Move/Stop) nutzt denselben Mechanismus wie bei Spielern, keine Kind-Fallunterscheidung nötig.
- Debug-Entity-Liste (aus der M1c-Nachbesserung) zeigt Kind (Spieler/Monster) und bei Monstern den Namen aus mobs.json mit an.
- Batchmode-Compile-Check.

## 7. Tests
- MobCatalog/SpawnCatalog: Laden, Validierungsfehler (unbekannte MobId, Position auf blockierter Zelle) mit klarer Meldung.
- Wander-KI: mit injizierbarer Zeit/Zufallsquelle - Ziel bleibt innerhalb WanderRadiusWire vom Anker, blockiertes Ziel wird übersprungen statt zu crashen, neue Fälligkeit liegt im erwarteten Intervall.
- Integration (LiteNetLib Loopback): Bot in Sichtweite eines Spawn-Punkts bekommt EntitySpawn mit Kind=Monster und korrekter MobId; Monster bewegt sich irgendwann sichtbar (EntityMove kommt an).
- Alle bisherigen Tests bleiben grün.

## 8. Doku
CLAUDE.md: Abschnitt "Monster" (Datenformat, Wander-KI-Prinzip, was bewusst fehlt: Kampf/Sterben/aktiver Respawn), Status/Meilensteine (M3c abgeschlossen, M3 damit komplett, M4 offen: Kampf).

## Fertig, wenn
- dotnet build && dotnet test grün (5x stabil).
- Batchmode-Compile-Check ohne Fehler.
- Mit laufendem Auth+Server+Bot in Spawn-Nähe: Log zeigt EntitySpawn mit Kind=Monster, später EntityMove für dieselbe Entity (Wander-Bewegung sichtbar).
- Ein Commit pro Abschnitt.
