# Icon-Integration (Lumiis-Asset-Archiv)

Stand: 2026-10-24

Quelle: `/home/nutzer/Desktop/lumiis-assets-archiv/assets/icons`
Ziel: `client/Assets/Resources/Icons/<kategorie>/<key>.png`
Generator: `tools/import-lumiis-assets.py`

## Bestand

- 533 PNGs im Archiv; `icons.json` nennt 545 Keys. 13 Dateien fehlen im Archiv
  (u. a. `sword_1`, `sword_5`, `sword_10`, `sword_15`, `sword_20`, `sword_25`,
  `twohand_10`, `armor_warrior_1/9/18/26`, `wolf_claw`, `boar_tusk`).
  Sie liegen in `lumiis-2d@19cd9dc:assets/icons/` und können bei Bedarf nachgeladen werden.
- Importiert sind aktuell **533 Icons** in 34 Kategorien; `yang_boden` liegt unter `misc`.
- 48 Buch-Icons hießen im Archiv `book:*.png` (unter Windows unzulässig). Der Import
  saniert sie zu `book_*.png`; der ursprüngliche Key bleibt als `sourceKey` im Manifest.
- Format: 32×32, 32×64, 32×96 (selten); RGBA, transparenter Hintergrund.

## Provenance

Die Icons stammen aus dem alten Lumiis-Referenzprojekt (`lumiis-2d`), nicht als
Gameforge-Dateien. Sie wurden dort aus eigenem Zeichencode exportiert; die
visuelle Anlehnung an Metin2-Wiki-Icons ist in den Referenzkommentaren dokumentiert.
Eintrag in `docs/art-provenance.md` als **eigenes, codegeneriertes Werk aus Lumiis-2D**
mit Hinweis auf die dokumentierte Stil-Anlehnung. Vor einer Veröffentlichung sollte
der Projektverantwortliche das bewusst freigeben.

## Import

```bash
python3 tools/import-lumiis-assets.py
```

Das Skript kopiert Icons und Charakter-Sheets, saniert Dateinamen, erzeugt das
`icon-manifest.json` und deterministische `.meta`-Dateien (Default-Textur,
Point-Filter, keine Kompression). Das Manifest ist JsonUtility-freundlich:

```json
{ "icons": [ { "key": "weapon/sword_30", "sourceKey": "sword_30",
  "category": "weapon", "file": "weapon/sword_30.png",
  "resource": "Icons/weapon/sword_30" } ] }
```

## Runtime

- `ItemIconCatalog.LoadFromResources()` lädt `Icons/icon-manifest` und Texturen lazy.
- `ItemEntryJson.iconKey` verbindet `data/items.json` mit dem Manifest.
- `GameClient.DrawItemIcon` zeigt Icons in Ausrüstung, Inventar und Shop.
- `AssetTestLab` hat eine paginierte Icon-Galerie (64 Icons/Seite, Kategorie-Filter).
- Icons werden als `Texture2D` geladen (kein Sprite-Atlas nötig); hohe Icons werden
  in UI Toolkit per `ScaleMode.ScaleToFit` und in IMGUI seitenverhältnistreu gezeichnet.

## Mapping der aktuellen 8 Items

| ID | Item | iconKey | Größe |
|---:|---|---|---|
| 1 | Holzschwert | `sword_30` | 32×64 |
| 2 | Eisenschwert | `sword_40` | 32×64 |
| 3 | Kriegsaxt | `twohand_20` | 32×96 |
| 4 | Lederruestung | `armor_ninja_1` | 32×64 |
| 5 | Kettenhemd | `armor_warrior_34` | 32×64 |
| 6 | Lederkappe | `helmet_ninja_1` | 32×32 |
| 7 | Rundschild | `shield_1` | 32×32 |
| 8 | Heiltrank | `red_s` | 32×32 |

Wenn die 13 fehlenden Dateien nachgeladen werden, können die Low-Level-Varianten
(`sword_1`/`sword_20`/`twohand_10`/`armor_warrior_1`) bevorzugt werden.

## Offene Punkte

- 13 fehlende Archivdateien nachladen (nicht blockierend für die 8 Items).
- Weitere Item-Typen mit `iconKey` versorgen (Material, Buch, Geiststein, Truhe,
  Schlüssel, Karte, Teleport, Schmuck, Schuhe, Kostüm, Pferd).
- Optional: Sprite-Atlas, sobald Profiling es rechtfertigt.
- Windows-Check nach dem Import (keine `:`-Dateien mehr).
