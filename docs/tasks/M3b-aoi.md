# M3b – Sichtbarkeitsbereich (AOI)

Ziel: Ein Spieler bekommt nur noch Spawns, Bewegungen und Despawns von Entities in einem Radius um sich selbst, nicht mehr die ganze Karte. Protokoll ändert sich NICHT (EntitySpawn/EntityMove/EntityStop/EntityDespawn-Format bleibt gleich, nur wer welches Paket bekommt). Nur Spieler-Entities in diesem Schritt, Monster (M3c) nutzen denselben Mechanismus später mit.

## 0. Review zuerst (nicht ändern, nur berichten, Datei:Zeile)
a) Wo genau broadcastet GameServer aktuell EntitySpawn (Join-Fall: existierende Entities an den Joiner, Joiner an alle anderen), EntityMove, EntityStop, EntityDespawn (Disconnect)? Liste jede Fundstelle.
b) Gibt es bereits eine Struktur pro Verbindung, in der sich ein HashSet<int> für aktuell sichtbare EntityIds unterbringen ließe (z. B. eine bestehende PlayerConnection-Klasse), oder muss das neu angelegt werden?
c) Wie ist die Position in Entity aktuell repräsentiert (Typ, wire-Skala)? Für die Abstandsberechnung wichtig: quadrierte Distanz mit long-Arithmetik gegen Overflow bei bis zu 100000 wire Einheiten pro Achse.

## 1. GameConstants
AoiRadiusWire = 1500 (15 Weltenheiten), benannte Konstante, spätere Justierung erwartet.

## 2. World: reine Sichtbarkeitsberechnung (kein Netzwerk, testbar)
- Neue Methode(n), z. B. ComputeVisibility(int radiusWire): für jede Entity, welche anderen Entities liegen innerhalb des Radius (quadrierte Distanz, long). Nur Distanzvergleich, keine Sichtlinien-/Hindernisprüfung (kein Ziel hier). Eine Entity sieht sich selbst nicht. Sichtbarkeit ist symmetrisch (A sieht B genau dann, wenn B sieht A bei gleichem Radius) - ob das für die Hälfte der Vergleiche genutzt wird, dein Ermessen.
- Keine LiteNetLib-/Netzwerk-Referenz, testbar mit direkt konstruierten World-Instanzen.

## 3. GameServer: Sichtbarkeits-Diff und gezielter Versand
- Pro Verbindung: HashSet<int> VisibleEntityIds (letzter bekannter Stand), Start leer.
- Jeden Tick: World.ComputeVisibility aufrufen, pro Verbindung Differenz zum vorherigen Stand bilden:
  - neu sichtbar: EntitySpawn senden. Ist die Entity gerade in Bewegung, zusätzlich EntityMove ab der AKTUELLEN Position (gleiches Muster wie beim Join in M1c für bereits laufende Entities, nicht die ursprüngliche Startposition).
  - nicht mehr sichtbar: EntityDespawn senden.
  - weiterhin sichtbar: keine Aktion hier, normale Move/Stop-Pakete (Punkt 4) decken das ab.
- VisibleEntityIds aktualisieren.
- Kadenz jeden Tick ist hier bewusst die einfachste Lösung, keine vorzeitige Optimierung bei unserer Spielerzahl.

## 4. Bestehende Broadcasts umstellen
- Die in 0a gefundenen Sendestellen für EntityMove/EntityStop werden von "an alle Verbindungen" auf "an alle Verbindungen, für die diese EntityId aktuell in VisibleEntityIds steht" umgestellt.
- Join: Der Joiner bekommt nur EntitySpawn für Entities, die laut ComputeVisibility ab seiner Startposition sichtbar sind. Andere Spieler bekommen EntitySpawn für den Joiner automatisch über den normalen Tick-Diff aus Punkt 3, sobald er der World hinzugefügt wurde - kein Sonderfall nötig, wenn Punkt 3 korrekt sitzt. Bestätige das im Bericht oder widersprich mit Begründung.
- Disconnect: EntityDespawn geht nur an Verbindungen, die die Entity aktuell sehen. VisibleEntityIds-Eintrag bei allen anderen bereinigen.

## 5. Tests
- Unit (kein Netzwerk): ComputeVisibility mit synthetischen Positionen - innerhalb Radius sichtbar, außerhalb nicht, exakt am Radius (Grenzfall, entscheide inklusiv/exklusiv, dokumentiere es), Symmetrie, eigene Entity nicht in eigener Menge, leere World.
- Integration (LiteNetLib Loopback wie gehabt): zwei Spieler außerhalb des Radius - keiner bekommt EntitySpawn vom anderen; einer bewegt sich in Reichweite - Beobachter bekommt EntitySpawn (aktuelle Position) plus EntityMove falls noch in Bewegung; bewegt sich wieder raus - EntityDespawn; ein Spieler joint, während ein für ihn sichtbarer Spieler mitten in Bewegung ist - EntitySpawn mit aktueller Position plus EntityMove; Disconnect eines Spielers, der nur für EINEN von zwei Beobachtern sichtbar war - nur der sehende Beobachter bekommt EntityDespawn, der andere bekommt gar kein Paket (mit Timeout prüfen, nicht nur "kein Fehler").
- Alle bisherigen Tests bleiben grün.

## 6. Doku
CLAUDE.md: TODO-Kommentare zu AOI aus World.cs/GameServer.cs (seit M1c) entfernen, neuer Abschnitt "Sichtbarkeitsbereich (AOI)" (Radius, O(n²)-Ansatz und warum das für unsere Größenordnung reicht, kein Grid/Quadtree bisher), Status/Meilensteine aktualisieren (M3b abgeschlossen, M3c offen: Monster).

## Fertig, wenn
- dotnet build && dotnet test grün (5x stabil).
- Mit laufendem Auth+Server+mehreren Bots (z. B. --count 6 --radius 30, spawnen nahe der Kartenmitte, wandern per Zufall bis zu 30 Einheiten davon weg): Logs zeigen, dass Bots im Laufe der Zeit EntitySpawn/EntityDespawn füreinander bekommen, nicht nur beim initialen Join.
- Ein Commit pro Abschnitt.
