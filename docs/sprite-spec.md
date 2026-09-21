# Sprite-Spec (Ebenen-System, "Paper Doll")

Status: provisorisch, bis der Art-Test den Stil entschieden hat (Pixelart oder Comic). Alle Zahlen stehen in data/looks.json unter "settings" und sind änderbar.

## Prinzip
Ein Charakter besteht aus Ebenen (Slots), die deckungsgleich übereinanderliegen. Der Server schickt nur IDs. Neue Rüstung, Waffe oder Frisur bedeutet neue Sprite-Dateien plus ein Eintrag in data/looks.json, kein Code.

## Slots
Body, Armor, Shield, Weapon, Hair, Helmet

## Raster
- Alle Ebenen: gleiche Canvas-Größe (provisorisch 128x128 px), gleicher Pivot (Fußpunkt, unten Mitte), transparenter Hintergrund, PNG, PPU 64 (Figur ca. 1,7 Welteinheiten hoch). Bei Pixelart z. B. 32x32 px mit PPU 16.
- Overlays (Rüstung, Helm, Haare, Waffe, Schild) sind pixelgenau auf den Körper ausgerichtet: gleiche Pose, gleiches Canvas, nur die Ebene selbst ist gezeichnet.
- Gezeichnet werden 3 Richtungen: Down (zum Betrachter), Up (Rücken), Side (nach rechts). Left entsteht per Spiegeln (flipX). Nichts Links/Rechts-Abhängiges zeichnen (Schrift, Logos).
- Waffe: eigene Datei je Richtung, im Canvas so platziert, dass der Griff auf dem Hand-Anker liegt.
- Haare: nur Graustufen, die Farbe kommt per Tönung (Palette in looks.json).
- Animation zuerst per Code (Wippen, Waffen-Schwung um den Hand-Anker). Später optional Frames.

## Dateinamen (klein, Unterstrich als Trenner)
<slot>_<lookId>_<direction>_<state><frame>.png
Beispiele: body_1_down_idle0.png, armor_12_side_walk2.png, hair_3_up_idle0.png
Ablage: client/Assets/Lumiis/Art/Characters/<slot>/

## Anker (pro Body-Look und Richtung, Pixel relativ zum Pivot)
hand (Schwung-Drehpunkt der Waffe), head (Namensschild). Bei flipX wird x gespiegelt.

## Zeichenreihenfolge (unten nach oben)
Down: Body, Armor, Shield, Weapon, Hair, Helmet
Up:   Weapon, Shield, Body, Armor, Hair, Helmet
Side: Shield, Body, Armor, Weapon, Hair, Helmet
Ein Helm mit hidesHair=true blendet die Haare aus.

## Herkunft und Recht (verbindlich)
- Keine Original-Assets, Screenshots oder Referenzbilder aus Metin2 oder anderen Spielen, weder im Repo noch als Eingabe für Generatoren. Keine Original-Namen für Items oder Rüstungen.
- Referenzen nur aus eigenen Skizzen, selbst erstellten Basis-Sprites und Quellen mit passender Lizenz.
- Jedes Asset wird in docs/art-provenance.md protokolliert (Datei, Werkzeug/Modell, Prompt, Seed, Datum, Lizenz).
- Designsprache statt Kopie: Tier-Logik (Stoff, Leder, Metall), Silhouetten-Regeln und Farbcodes sind erwünscht, konkrete Designs sind eigene.
