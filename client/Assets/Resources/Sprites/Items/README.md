# Waffen-Sprites (Paper Doll)

Hier liegen die statischen Waffen-PNGs, die `CharacterView.SetWeapon` über
`CachingSpriteProvider.LoadWeaponSprite` lädt. Der Schlüssel kommt aus `data/items.json`
(`spriteKey`, ohne Dateiendung), z. B. `weapon_2_wood` -> `weapon_2_wood.png`.

Aktuell enthalten:
- `weapon_2_wood.png` (Holzschwert)
- `weapon_2_iron.png` (Eisenschwert)
- `weapon_3_axe.png` (Kriegsaxt)

Die drei Dateien sind bewusst triviale, per `tools/generate-placeholder-weapons.py`
erzeugte Platzhalter (128x128, PPU 64, Pivot am Griff). Sie sind keine Kunst aus einem
anderen Spiel; Herkunft steht in `docs/art-provenance.md`.

Regeln (siehe `docs/sprite-spec.md`):

- Eine Datei je Waffe und Richtung, Pivot am Griff, transparent, gleiche Canvas-Größe wie die Figur.
- Fehlt eine Datei, fällt der Client sichtbar auf den generierten Platzhalter zurück
  (`PlaceholderSpriteProvider`); das Spiel bleibt lauffähig.
- Die Pixel-Korrektur `pivotOffset` aus `items.json` wird zusätzlich auf den Griff angewendet.
- Keine Original-Assets/Referenzbilder aus anderen Spielen. Jede echte Datei hier mit Werkzeug,
  Prompt, Seed, Datum und Lizenz in `docs/art-provenance.md` eintragen.
