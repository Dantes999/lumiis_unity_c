# M3a – Kollision (Kartenkollisionsmaske, blockierte Bewegung)

Ziel: Bewegung wird gegen eine pro Karte hinterlegte Blockierkarte geprüft, nicht mehr nur an die Kartengrenzen geklemmt. Protokoll ändert sich NICHT (EntityMove/EntityStop-Format bleibt gleich, nur das berechnete Ziel ändert sich). Nur eine Karte ("village") in diesem Schritt, Mehrkarten-Unterstützung ist bewusst kein Ziel hier.

## 1. Autoren-Pipeline (Python, außerhalb der Solution)
- tools/bake-collision.py (Pillow, gleiche Toolchain wie die Sprite-Pipeline): liest ein PNG, erkennt klar rote Pixel (Schwellwert konfigurierbar, z. B. R > 200 und G,B < 100, mit kurzer Begründung im Code) als blockiert, alles andere als begehbar. Schreibt eine gepackte Bitmaske (1 Bit pro Zelle, Little-Endian, mit kleinem Header: Magic-Bytes, Breite, Höhe) nach <input>-Verzeichnis als collision.bin. Gibt Statistik aus (blockierte Zellen absolut und in Prozent), damit der User beim Bemalen schnell Rückmeldung bekommt. Aufruf: python3 tools/bake-collision.py data/maps/village/collision-source.png.
- Erzeuge testhalber ein einfaches Platzhalter-collision-source.png für "village" (1000x1000 px): Rand komplett blockiert (Kartenaußengrenze), zusätzlich ein rechteckiger Block (z. B. "Haus") und eine ovale Fläche (z. B. "Teich") als Testfälle. Dokumentiere klar in CLAUDE.md, dass der User dieses Platzhalterbild später durch eine echte, handbemalte Version ersetzt, sobald Kartengrafik existiert.
- Backe die Platzhalterdatei einmal, committe Quelle und collision.bin.

## 2. Server (Lumiis.Data oder ein neues kleines Modul, keine Kopplung an EF Core nötig)
- CollisionMap: lädt collision.bin (Header-Validierung: Magic-Bytes, Maße gegen GameConstants prüfen, Startabbruch mit klarer Meldung bei Nichtübereinstimmung), IsBlocked(int cellX, int cellY) und eine Überladung, die Wire-Koordinaten (1/100 Einheit) in Zellen umrechnet (Ganzzahldivision durch 100) und dabei Grenzen prüft (außerhalb der Karte = blockiert).
- Bewegungsberechnung (dort, wo MoveTo bisher nur an die Kartengrenzen geklemmt hat): nach dem Klemmen die gerade Linie von der aktuellen Position zum geklemmten Ziel zellenweise abschreiten (Bresenham-artig), an der letzten begehbaren Zelle vor einer blockierten stoppen. Steht die Startzelle selbst schon auf einer blockierten Zelle (sollte nicht vorkommen, aber absichern), keine Bewegung auslösen. World bleibt weiterhin frei von Dateizugriffen selbst - CollisionMap wird injiziert/übergeben, nicht selbst geladen.
- GameServer/Program.cs: lädt data/maps/village/collision.bin beim Start (--data-dir bleibt der Basispfad wie bisher), reicht die CollisionMap an World weiter.

## 3. Tests
- CollisionMap-Loader: synthetische Bitmaske DIREKT IM TEST als Bytes konstruiert (nicht über Python/das Bake-Skript, damit der Test nicht von einer zweiten Sprache abhängt) - Rundtrip einfacher Fälle, Grenzprüfung, kaputter Header führt zu klarer Fehlermeldung statt Absturz.
- Bewegungslogik mit synthetischer CollisionMap (kein echtes File nötig): freie Bewegung unverändert wie bisher, Bewegung direkt in eine Wand stoppt exakt davor, Bewegung parallel an einer Wand entlang unbeeinflusst, Ziel jenseits der Kartengrenze wird zuerst geklemmt und DANN auf Kollision geprüft.
- Ein Integrationstest (wie gehabt, LiteNetLib auf Loopback): Bot sendet MoveTo in einen bekannt blockierten Bereich (z. B. mitten in den Platzhalter-Teich), EntityStop kommt an der Grenze davor an, nicht im blockierten Bereich.
- Alle bisherigen Tests bleiben grün.

## 4. Doku
CLAUDE.md: neuer Abschnitt "Kartenkollision" (Pipeline-Ablauf: Quellbild bemalen, tools/bake-collision.py ausführen, beide Dateien committen; 1 Pixel = 1 Weltenheit; aktuell nur eine Karte "village"), Hinweis dass das aktuelle collision-source.png ein Platzhalter ist, Status/Meilensteine aktualisieren (M3a abgeschlossen, M3b/M3c offen).

## Fertig, wenn
- dotnet build && dotnet test grün (5x stabil).
- Mit laufendem Server: ein Bot, der absichtlich in den blockierten Teich/das Haus zu laufen versucht, bleibt sichtbar davor stehen (im Log an EntityStop-Position erkennbar).
- Ein Commit pro Abschnitt.
