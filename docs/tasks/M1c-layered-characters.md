# M1c – Ebenen-Charaktere (Paper Doll) mit Platzhaltern

Ziel: Charaktere werden im Client aus Ebenen zusammengesetzt (Körper, Rüstung, Schild, Waffe, Haare, Helm). Der Server schickt nur Appearance-IDs. Alles mit Platzhalter-Grafik (farbige Formen), damit Zeichenreihenfolge, Spiegeln, Anker, Tönung und Datenformat bewiesen sind, bevor Kunst existiert. Lies zuerst docs/sprite-spec.md.

## 1. Daten und Shared
- data/looks.json (Schema unten) mit Platzhalter-Einträgen: 1 Body, 3 Haarstile, Haarfarben-Palette (8), 4 Rüstungen, 2 Helme (einer mit hidesHair), 4 Waffen, 2 Schilde. settings: canvasPx 128, ppu 64.
- Shared (C# 9): `Appearance` (BodyId, HairStyleId, HairColorId, ArmorId, HelmetId, WeaponId, ShieldId; 0 = keine Ebene, außer Body; Write/Read; Gleichheit). `Facing` (Down, Up, Side plus FlipX). `FacingMath.FromDelta(dx, dy, previous)`: dominante Achse mit Hysterese gegen Flackern, dx < 0 ergibt Side mit FlipX, dx = dy = 0 behält previous.
- Tests: Appearance Round-trip, FacingMath (alle Quadranten, Hysterese, Nullvektor).

## 2. Server
- `LookCatalog` (System.Text.Json, case-insensitive) lädt und validiert data/looks.json beim Start: eindeutige IDs je Slot, Palette nicht leer, hidesHair nur bei Helmen. Fehler bedeuten Abbruch beim Start mit klarer Meldung. Datenpfad per `--data-dir` (Default: data/ im Repo-Root).
- Appearance-Vergabe beim Join deterministisch aus dem Namen mit stabilem Hash (FNV-1a, NICHT string.GetHashCode), nur gültige IDs aus dem Katalog. Später kommt sie aus der DB (M2).
- Protokoll: EntitySpawnPacket bekommt Appearance. Protocol.Version = 2. Round-trip-Test, Integrationstest (B sieht die Appearance von A), Bot loggt sie. Es gibt keine Client->Server-Pakete für Appearance.

## 3. Client (client/Assets/Lumiis/Scripts/, code-first)
- Kopierschritt in Lumiis.Shared.csproj erweitern: data/looks.json nach client/Assets/StreamingAssets/lumiis-data/ (git-ignoriert wie Plugins).
- `LookCatalogClient`: lädt looks.json per JsonUtility (Wrapper-Klassen, feste Felder statt Dictionary).
- `ISpriteProvider`: liefert Sprite für (Slot, LookId, Facing, State, Frame). `PlaceholderSpriteProvider` erzeugt zur Laufzeit farbige Formen auf dem Canvas (Körper: Rumpf plus Kopf, Rüstung: Rumpf-Overlay, Haare: graue Kappe, Helm: Kappe, Waffe: langes schmales Rechteck am Griff, Schild: Scheibe), je Richtung leicht verschieden. Später kommt ein `AtlasSpriteProvider`, der echte PNGs lädt und bei fehlender Datei auf den Platzhalter zurückfällt.
- `CharacterView` ersetzt das Quadrat der EntityView für Spieler: ein Kind-SpriteRenderer pro Slot. Sortierung je Facing nach der Tabelle der Spec, flipX bei Facing.FlipX (Anker-x gespiegelt), Waffe dreht um den Hand-Anker, Haare mit Tönung aus der Palette, Haare aus bei Helm mit hidesHair. Figuren-übergreifende Y-Sortierung: sortingOrder = Basis aus Y (weiter unten = weiter vorne) mal 10 plus Slot-Rang, damit sich Ebenen zweier Figuren nicht mischen.
- `CharacterAnimator`: prozedural. Idle-Atmen (ca. 1 % Skalierung), Gehen (Wippen ca. 0,05 Einheiten plus Waffen-Schwung). Bewegung und Richtung kommen aus ClientWorld. Facing per FacingMath.
- Debug, nur Editor und Development-Build: F2 Testboard (Raster: Body mit allen Rüstungen, je 3 Facings plus gespiegelt), F3 bis F8 wechseln lokal Haarstil, Haarfarbe, Rüstung, Helm, Waffe, Schild der eigenen Figur (rein lokal, wird nicht gesendet).
- Kompilier-Check im Batchmode wie zuvor.

## 4. Doku
- CLAUDE.md: Konvention "Keine Original-Assets, Referenzbilder oder Namen aus Metin2 oder anderen Spielen. Herkunft jedes Assets in docs/art-provenance.md." Abschnitte Client, Aktuelle Aufgabe und Status aktualisieren.
- docs/art-provenance.md anlegen (Tabelle: Datei | Werkzeug/Modell | Prompt | Seed | Datum | Lizenz/Bedingungen), vorerst leer.

## Schema data/looks.json (Vorgabe)
{
  "settings": { "canvasPx": 128, "ppu": 64 },
  "hairColors": ["#3b2a1a", "#8a5a2b"],
  "looks": [
    { "id": 1, "slot": "Body", "name": "Basis", "hidesHair": false,
      "anchors": { "down": { "hand": [20, 40], "head": [0, 100] }, "up": { "hand": [-20, 40], "head": [0, 100] }, "side": { "hand": [10, 42], "head": [0, 100] } } },
    { "id": 1, "slot": "Helmet", "name": "Platzhalter-Helm", "hidesHair": true }
  ]
}
Slots: Body, Hair, Armor, Helmet, Weapon, Shield. IDs sind je Slot eindeutig. Anker (Pixel relativ zum Pivot) nur bei Body.

## Fertig, wenn
- cd server && dotnet build && dotnet test grün (5x stabil), Compile-Check ohne Fehler.
- Demo: Server plus Bot --count 6: verschiedene Looks, Figuren drehen sich beim Laufen (Down, Up, Side, links gespiegelt), Waffe wechselt die Ebene, Helm verdeckt Haare (Testboard), Schwingen beim Gehen.
- Ein Commit pro Abschnitt.
