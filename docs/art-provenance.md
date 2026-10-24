# Art Provenance

Protokoll für jedes Grafik-Asset im Repo (siehe docs/sprite-spec.md, Abschnitt "Herkunft und Recht").
Platzhalter-Grafik (zur Laufzeit generierte Formen) braucht keinen Eintrag - erst echte Kunst-Assets.

| Datei | Werkzeug/Modell | Prompt | Seed | Datum | Lizenz/Bedingungen |
|-------|------------------|--------|------|-------|---------------------|
| client/Assets/Resources/Sprites/Items/weapon_2_wood.png | `tools/generate-placeholder-weapons.py` (Pillow, eigene Formen) | "einfaches Holzschwert, flache Pixelform, kein fremdes Design" | deterministisch (Skript) | 2026-10-24 | Eigenes Werk, frei im Projekt verwendbar |
| client/Assets/Resources/Sprites/Items/weapon_2_iron.png | `tools/generate-placeholder-weapons.py` (Pillow, eigene Formen) | "einfaches Eisenschwert, flache Pixelform, kein fremdes Design" | deterministisch (Skript) | 2026-10-24 | Eigenes Werk, frei im Projekt verwendbar |
| client/Assets/Resources/Sprites/Items/weapon_3_axe.png | `tools/generate-placeholder-weapons.py` (Pillow, eigene Formen) | "einfache Kriegsaxt, flache Pixelform, kein fremdes Design" | deterministisch (Skript) | 2026-10-24 | Eigenes Werk, frei im Projekt verwendbar |
| client/Assets/Resources/Icons/** (533 PNGs) | `lumiis-2d` (altes Lumiis-Projekt), Export aus eigenem Zeichencode; Import via `tools/import-lumiis-assets.py` | "eigene, prozedurale Pixelart-Symbole; visuelle Anlehnung an Metin2-Wiki-Icons dokumentiert, keine Originaldateien" | deterministisch (Code) | Export 2026-10-20, Import 2026-10-24 | Eigenes Werk aus Lumiis-2D; keine Gameforge-Dateien; Stil-Anlehnung dokumentiert, Freigabe durch Projektverantwortlichen empfohlen |
| client/Assets/Resources/Characters/{body,head,hair,armor,weapon} (53 PNGs) | `lumiis`-Referenzprojekt (`packages/client/public/assets`), eigene Lumiis-Pixelart | "modulare Charakter-Sheets (48x48, 9 Spalten, 3 Ansichten), eigene Lumiis-Figuren" | deterministisch (Referenz-Assets) | 2026-10-24 | Eigenes Werk aus dem Lumiis-Referenzprojekt; im Projekt verwendbar |
