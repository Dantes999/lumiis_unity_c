# Icon-Integration (Lumiis-Asset-Archiv)

Stand: 2026-10-24

Quelle: `/home/nutzer/Desktop/lumiis-assets-archiv/assets/icons`
Ziel: `client/Assets/Resources/Icons/<kategorie>/<key>.png`
Generator: `tools/import-lumiis-assets.py`

## Bestand

- `icons.json` nennt 545 Keys; 13 Dateien fehlten im Archiv
  (`sword_1/5/10/15/20/25`, `twohand_10`, `armor_warrior_1/9/18/26`, `wolf_claw`, `boar_tusk`).
  Sie wurden aus `lumiis-2d@19cd9dc:assets/icons/` nachgeladen.
- Importiert sind aktuell **546 Icons** in 34 Kategorien (545 Keys aus `icons.json` plus
  `yang_boden`); alle Manifest-Keys sind vorhanden.
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
| 20 | Kurzschwert | `sword_1` | 32×64 |
| 21 | Langschwert | `sword_10` | 32×64 |
| 22 | Streitaxt | `twohand_10` | 32×96 |
| 23 | Jagdspeer | `twohand_5` | 32×96 |
| 24 | Plattenruestung | `armor_warrior_18` | 32×64 |
| 25 | Leichte Ruestung | `armor_warrior_1` | 32×64 |
| 26 | Vollhelm | `helmet_ninja_21` | 32×32 |
| 27 | Turmschild | `shield_21` | 32×32 |
| 35 | Heiltrank mittel | `red_m` | 32×32 |
| 36 | Heiltrank gross | `red_l` | 32×32 |

Die nachgeladenen Low-Level-Varianten (`sword_1`/`sword_20`/`twohand_10`/`armor_warrior_1`)
stehen jetzt für frühe Item-Tiers zur Verfügung.

## Offene Punkte

- Weitere Item-Typen mit `iconKey` versorgen (Material, Buch, Geiststein, Truhe,
  Schlüssel, Karte, Teleport, Schmuck, Schuhe, Kostüm, Pferd); erste Ausrüstungs- und
  Trank-Erweiterung ist umgesetzt.
- Optional: Sprite-Atlas, sobald Profiling es rechtfertigt.
- Windows-Check nach dem Import (keine `:`-Dateien mehr).
