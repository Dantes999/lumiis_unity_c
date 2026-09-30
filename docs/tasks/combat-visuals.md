# Kampf-Visualisierung (Reichweite, Flaechenradius, Treffer, Blickrichtung, Buff-Ring)

Ziel: Kampf wird sichtbar. Reichweitenring um den Spieler, Radius-Aufblitzen bei Kreisschlag, kurze Trefferlinie bei jedem Angriff/Skill (eigene und gegnerische), Charakter dreht sich beim Angreifen zum Ziel, aktive Buffs bekommen einen farbigen Ring. Rein clientseitig, KEINE Protokolländerung, KEINE echte Kunst/Partikel, nur klare Platzhalterformen.

## 0. Review zuerst (nicht aendern, nur berichten, Datei:Zeile)
a) Wo genau erzeugt der Client aktuell den Ring unter dem lokalen Spieler (M1c/M1-fixes, "LocalPlayerMarker" bzw. Ring-Generator in PlaceholderSprites)? Ist diese Funktion parametrisierbar fuer beliebigen Radius/Farbe, oder fest? Empfehlung, wie sie fuer Reichweitenring/Radius-Blitz/Buff-Ring wiederverwendet wird.
b) Hat der Client aktuell Zugriff auf MeleeRangeWire (aus classes.json, bisher nur serverseitig genutzt in M4a) und auf RadiusWire pro Skill (aus skills.json, M6b)? Werden diese Dateien ueberhaupt in client/Assets/StreamingAssets kopiert (wie looks.json/items.json)? Falls nicht: kleinste Ergaenzung im bestehenden Kopierschritt (Lumiis.Shared.csproj) vorschlagen.
c) Wird die Blickrichtung (Facing) aktuell ausschliesslich aus Bewegungsdeltas abgeleitet (FacingMath.FromDelta, M1b)? Gibt es dort schon einen Mechanismus fuer einen kurzen "Override" (z. B. kurzzeitig in eine andere Richtung schauen, ohne die naechste Bewegung zu verwirren)?
d) Welche Client-Klasse verarbeitet aktuell EntityDamagedPacket/EntityDodgedPacket/ActiveBuffsChangedPacket (GameClient/ClientWorld)? Wo ist der sauberste Ort, um daraus visuelle Effekte auszuloesen, ohne die bestehende Verarbeitung (HP-Anzeige, Overlay-Text) zu veraendern?
e) Sind Positionen aller sichtbaren Entities (nicht nur des lokalen Spielers) im Client jederzeit bekannt (fuer die Trefferlinie zwischen zwei beliebigen Entities, auch wenn keine davon der lokale Spieler ist, z. B. wenn ein Monster einen anderen Spieler angreift)?

## 1. Daten-Zugaenglichkeit (falls durch 0b noetig)
Kopierschritt erweitern, damit classes.json und skills.json ebenfalls unter client/Assets/StreamingAssets/lumiis-data/ landen (wie looks.json/items.json/mobs.json). Kleiner ClassCatalogClient/SkillCatalogClient (nur die fuer die Visualisierung noetigen Felder: MeleeRangeWire, RadiusWire pro Skill), analog zu bestehenden Client-Katalogen.

## 2. Reichweitenring (Nahkampf)
Ring-Generator aus 0a mit variablem Radius wiederverwenden: duenner, halbtransparenter Ring in MeleeRangeWire um den lokalen Spieler, IMMER sichtbar solange man verbunden ist (unabhaengig von Zielauswahl), in Weltkoordinaten (folgt der Spielerposition, keine eigene Rotation noetig da rund). Farbe dezent (z. B. helles Grau/Weiss mit niedriger Deckkraft), soll nicht ablenken.

## 3. Radius-Aufblitzen (Kreisschlag)
Beim erfolgreichen Ausloesen von Kreisschlag (erkennbar am eingehenden EntityDamaged/EntityDodged mit dem lokalen Spieler als Angreifer und dem bekannten AreaAttack-Skill, ODER einfacher: bei jedem lokal gesendeten UseSkillRequestPacket fuer Kreisschlag sofort optimistisch anzeigen, unabhaengig vom Serverergebnis - deine Wahl, im Bericht begruenden): kurzzeitig (ca. 300-400ms) ein groesserer Ring mit RadiusWire um den Spieler, der einblendet und ausblendet (Skalierung oder Deckkraft ueber die Zeit), dann entfernt.

## 4. Trefferlinie (jeder Treffer, jeder Fehlschlag)
Bei eingehendem EntityDamagedPacket ODER EntityDodgedPacket: falls beide beteiligten EntityIds aktuell im Client bekannte Positionen haben (Angreifer und Ziel, unabhaengig davon ob der lokale Spieler beteiligt ist), kurzzeitig (ca. 150-250ms) eine Linie zwischen beiden Positionen zeichnen (LineRenderer oder aequivalent), die schnell verblasst, dann entfernt. Treffer (EntityDamaged) und Ausweichen (EntityDodged) bekommen unterschiedliche Farben (z. B. Rot fuer Treffer, Blau/Grau fuer Ausweichen). Mehrere gleichzeitige Linien (z. B. bei Kreisschlag gegen mehrere Ziele) muessen unabhaengig voneinander funktionieren, nicht nur eine globale Linie.

## 5. Blickrichtung beim Angreifen
Beim Senden eines AttackRequestPacket oder eines zielgerichteten UseSkillRequestPacket (Wuchtschlag) durch den lokalen Spieler: sofort lokal (bevor die Serverantwort da ist) die Blickrichtung des eigenen Charakters kurzzeitig auf "zum Ziel drehen" setzen (per FacingMath.FromDelta mit dem Vektor Spieler->Ziel), unabhaengig von der zuletzt bekannten Bewegungsrichtung. Sobald sich der Spieler wieder bewegt, uebernimmt die normale bewegungsbasierte Blickrichtung wieder (kein dauerhafter Override, keine Serveraenderung). Bei EntityKind=Monster/Npc als Angreifer (also wenn ein ANDERER Spieler oder ein Monster angreift) optional dieselbe Logik anwenden, wenn es ohne grossen Aufwand geht, sonst nur fuer den lokalen Spieler.

## 6. Buff-Ring
Waehrend Kampfrausch aktiv ist (aus ActiveBuffsChangedPacket bekannt): duenner farbiger Ring um den betroffenen Charakter (z. B. Rot-Orange), zusaetzlich zur bestehenden Overlay-Textanzeige. Waehrend Hast aktiv ist: andere Farbe (z. B. Gelb). Beide gleichzeitig aktiv: beide Ringe sichtbar (z. B. unterschiedlicher Radius oder nebeneinander), keine Ueberschneidung, die einen davon verdeckt. Ring verschwindet exakt wenn ActiveBuffsChanged den Buff nicht mehr listet.

## 7. Pruefung
Batchmode-Compile-Check. tools/test-client-playmode.sh: pruefe, ob sich die bestehenden 4 Tests durch die neuen visuellen Elemente stoeren lassen (z. B. wenn ein Test das Debug-Panel sucht und jetzt neue GameObjects im Weg sind) - falls ja, anpassen, aber keine neuen PlayMode-Tests fuer reine Visuals erzwingen (schwer sinnvoll automatisiert pruefbar), stattdessen eine kurze Checkliste fuer den manuellen Test durch den User ans Ende von CLAUDE.md.

## 8. Doku
CLAUDE.md: neuer Abschnitt "Kampf-Visualisierung" (was gezeigt wird, dass es rein clientseitig/kosmetisch ist, keine Serverwahrheit). Kurze manuelle Test-Checkliste ergaenzen.

## Fertig, wenn
- dotnet build und dotnet test gruen (5x stabil, unveraendert, diese Aufgabe aendert keinen Server-/Shared-Code ausser evtl. dem Datei-Kopierschritt).
- Batchmode-Compile-Check und tools/test-client-playmode.sh ohne Fehler.
- Ein Commit pro Abschnitt.
