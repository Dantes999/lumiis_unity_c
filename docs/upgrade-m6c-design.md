# M6c – Ausrüstungs-Aufwertung (+0…+9): Design-Notiz

Kurzfassung der Umsetzung; die verbindliche Beschreibung steht in `CLAUDE.md` im Abschnitt
„Ausruestungs-Aufwertung“.

## Datenfluss

1. Client sendet `UpgradeItemRequestPacket { EquipmentSlot, UseProtectionScroll }` (Protocol.Version 16).
2. `GameServer.HandleUpgradeItemRequest` ruft `World.TryUpgrade` auf (Game-Loop-Thread, kein Netzwerk,
   injizierbare `Random`-Quelle).
3. `World.TryUpgrade` prüft in dieser Reihenfolge: Katalog vorhanden, Slot belegt, Stufe < 9,
   Schleifstein vorhanden, ggf. Bewahrungsrolle vorhanden. **Jede** dieser Vorprüfungen endet ohne
   Verbrauch (`UpgradeActionFailedPacket`).
4. Erst dann: Erfolgswurf `_random.Next(100) < successChancePercent[aktuelle Stufe]`.
   Schleifstein immer entfernen; Bewahrungsrolle bei Verwendung immer entfernen.
   - Erfolg: Stufe +1, `RecomputeDerivedStats`.
   - Fehlschlag mit Rolle: Stufe/Item unverändert.
   - Fehlschlag ohne Rolle: Ausrüstungs-Slot leeren (Item zerstört), `RecomputeDerivedStats`.
5. Server antwortet mit `UpgradeItemResultPacket` + `InventorySyncPacket` + `CharacterStatsPacket`,
   bei Zerstörung zusätzlich `AppearanceChangedPacket` an alle AOI-Beobachter, plus deutsche
   `ChatMessagePacket(System)`. Danach sofortiger `CharacterSaveRequest` (voller Snapshot).

## Datenmodell

- `CharacterInventoryItem.UpgradeLevel` (0-9, Default 0).
- `Character.EquippedArmor/Helmet/Weapon/ShieldUpgradeLevel` (0-9, Default 0).
- `Entity.InventoryUpgradeLevels[]` parallel zu `Entity.Inventory[]`; vier
  `Equipped...UpgradeLevel`-Felder. `TryEquip`/`TryUnequip` verschieben ItemId und Stufe gemeinsam.
- `ItemDefinition.UpgradeBonusPerLevel`; Waffe addiert den Bonus auf Attack, Armor/Helmet/Shield auf
  Defense (`World.RecomputeDerivedStats`).
- `UpgradeCatalog` (`data/upgrade.json`) validiert Maximalstufe 9, genau 9 Chancenwerte 0-100 und die
  beiden referenzierten Item-IDs samt ItemType.

## Wire-Format

- `InventorySyncPacket`: pro Slot `(byte SlotIndex, int ItemId, byte UpgradeLevel)`, danach vier
  `Equipped...ItemId` (int) und vier `Equipped...UpgradeLevel` (byte).
- `UpgradeItemResultPacket`: `byte EquipmentSlot, byte Success, byte Destroyed, byte NewUpgradeLevel`.
- `UpgradeActionFailedPacket`: `byte Reason` (SlotEmpty/AlreadyMaxLevel/NoMaterial/NoProtectionScroll).
- `EntitySpawnPacket`/`Appearance` bleiben unverändert: Aufwertung ändert keine Looks.

## Bewusste Limitations / offene Punkte

- Nur **angelegte** Ausrüstung ist aufwertbar; Inventar-Items nicht (wie in der Aufgabe gefordert).
- Kein Stapeln: jeder Inventarplatz trägt genau ein Item, also ein Schleifstein/eine Rolle pro Platz.
- Noch keine globalen Item-Instanz-IDs; die Stufe hängt an Slot+ItemId. Für Handel/Anti-Dupe ist eine
  spätere Instanz-ID-Schicht nötig.
- Erfolgstabelle und `upgradeBonusPerLevel` sind Platzhalter/Balance-Werte.
- Kein grafisches Aufwertungsfenster; die UI ist im bestehenden Debug-/Test-Panel (Editor/
  Development-Build) umgesetzt, inklusive +N-Anzeige, Aufwerten-Knopf und Bewahrungsrollen-Checkbox.
- Die Migration `AddUpgradeLevels` ist generiert, aber in dieser Umgebung nicht gegen echtes Postgres
  angewendet (`dotnet ef database update` nach `source tools/dev-env.sh`).
- Kein neuer NPC; der bestehende Dorfhändler verkauft Schleifstein und Bewahrungsrolle.
