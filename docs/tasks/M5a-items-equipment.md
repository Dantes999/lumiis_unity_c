# M5a – Items, Inventar, Ausrüstung, Drops

Ziel: Ausrüstungs-Slots (Rüstung/Helm/Waffe/Schild) werden zu echten Gegenständen mit Kampfwert-Boni, die Monster droppen. Ein 20-Felder-Inventar, Anlegen/Ablegen, ein Heiltrank als Verbrauchsgegenstand. KEIN Shop (M5b), KEIN Boden-Loot (Drops gehen direkt ins Inventar), KEIN Stapeln, KEIN Upgrade-System, KEINE echte Inventar-Grafik (Debug-Panel-Stil reicht).

## 0. Review zuerst (nicht ändern, nur berichten, Datei:Zeile)
a) Alle Stellen, die aktuell Character.ArmorId/HelmetId/WeaponId/ShieldId lesen oder schreiben (Lumiis.Auth Charaktererstellung, GameServer beim Join, sonstwo) - vollständige Liste, damit beim Umbau nichts übersehen wird.
b) Wie ist der bestehende asynchrone Speicher-Mechanismus für Position/Yang (M2b/M4a) aufgebaut - lässt er sich auf "vollständiger Charakterzustand inkl. Inventar/Ausrüstung" verallgemeinern, oder ist ein zusätzlicher, separater Kanal sinnvoller? Deine Empfehlung mit Begründung.
c) Wie liest Entity.Attack/Entity.Defense aktuell (M4a) - reines Feld, das beim Join einmal gesetzt wird? Bestätige, dass eine Neuberechnung bei Equip/Unequip ohne Änderung an TryAttack selbst möglich ist.
d) Prüfe den aktuellen JoinAccepted/EntitySpawn-Aufbau: Reicht es, die Appearance-Felder (ArmorId usw.) weiterhin als LookId zu befüllen (nur die Quelle ändert sich: jetzt über ItemCatalog.LookId statt direkt aus der DB-Spalte), oder ist eine Protokolländerung am Appearance-Struct selbst nötig? Ziel: möglichst kein Bruch am bestehenden Wire-Format für Appearance.

## 1. Daten
- data/items.json: Liste von Items. Id, Name, ItemType (enum: Armor, Helmet, Weapon, Shield, Consumable). Bei Armor/Helmet/Weapon/Shield: LookId (referenziert einen bestehenden Eintrag im jeweiligen Slot aus looks.json), AttackBonus (int, meist 0 außer bei Weapon), DefenseBonus (int, meist 0 außer bei Armor/Helmet/Shield). Bei Consumable: HealAmount (int). Lege an: ein Holzschwert (kleiner AttackBonus, wird Startwaffe), 1-2 weitere Waffen, je 1-2 Rüstung/Helm/Schild, einen Heiltrank.
- data/mobs.json erweitern: Drops-Liste pro Mob-Typ, je Eintrag ItemId + ChancePercent (0-100, unabhängig gewürfelt je Eintrag, mehrere Drops pro Tod möglich). Wildhund und Waldschrat bekommen je 1-2 mögliche Drops nach deinem Ermessen.

## 2. Datenbank (Lumiis.Data)
- Migration: Character.ArmorId/HelmetId/WeaponId/ShieldId umbenennen zu EquippedArmorItemId/EquippedHelmetItemId/EquippedWeaponItemId/EquippedShieldItemId (nullable int, Item-IDs statt Look-IDs). Bestehende Werte werden dabei zurückgesetzt (NULL), das ist beabsichtigt (siehe Bericht zu 0a) - kurz im Migrationskommentar dokumentieren, warum.
- Neue Tabelle CharacterInventoryItem: Id, CharacterId (FK), SlotIndex (int, 0-19), ItemId (int, kein DB-FK, referenziert items.json). Unique-Constraint auf (CharacterId, SlotIndex).
- Lumiis.Auth, Charaktererstellung: KEIN automatisches Zuweisen von Armor/Helmet/Shield-Items mehr. EquippedWeaponItemId wird auf die Id des Holzschwerts gesetzt (Startwaffe, siehe Kontext oben), Inventar bleibt leer.

## 3. Shared (netstandard2.1 / C# 9)
- InventorySyncPacket (Server->Client, nur an den Empfänger): Liste von (SlotIndex, ItemId), plus EquippedArmorItemId/HelmetItemId/WeaponItemId/ShieldItemId (nullable, 0 = leer). Wird bei Join und nach jeder Inventaränderung neu verschickt (voller Resync, kein Delta - Inventar ist klein genug).
- EquipItemRequestPacket (Client->Server): InventorySlotIndex.
- UnequipItemRequestPacket (Client->Server): EquipmentSlot (enum: Armor, Helmet, Weapon, Shield).
- UseConsumableRequestPacket (Client->Server): InventorySlotIndex.
- InventoryActionFailedPacket (Server->Client, nur an den Anfragenden): Reason (enum: InvalidSlot, EmptySlot, WrongItemType, NotConsumable, InventoryFull, TargetAtFullHp).
- Protocol.Version erhöhen (aktuellen Wert aus 0d ermitteln, +1). Tests: Round-trip aller neuen Pakete, PacketFactory-Vollständigkeitstest bleibt grün.

## 4. Server: Item-Katalog und Kernlogik (World, kein Netzwerk, testbar)
- ItemCatalog laden/validieren (analog zu MobCatalog/ClassCatalog): eindeutige IDs, LookId muss im jeweiligen Slot von looks.json existieren, Startabbruch mit klarer Meldung bei Verstoß.
- Inventar-Repräsentation an Entity/Character-Laufzeitobjekt: Dictionary<int SlotIndex, int ItemId> oder Array[20], deine Wahl, mit Tests abgesichert.
- TryEquip(characterState, inventorySlotIndex) -> Ergebnis: prüft Slot belegt, ItemType passt zu einem Ausrüstungs-Slot, führt den Tausch durch (siehe Kontext: Item aus Inventarplatz X in Ausrüstung, bisheriges Ausrüstungsitem nach X zurück), berechnet danach Attack/Defense neu (BaseAttack/BaseDefense aus classes.json + Summe aller Boni).
- TryUnequip(characterState, equipmentSlot, freeInventorySlotIndex oder automatische erste freie Stelle) -> Ergebnis: benötigt einen freien Inventarplatz, bei keinem freien Platz InventoryFull. Attack/Defense neu berechnen.
- TryUseConsumable(characterState, inventorySlotIndex, nowMs) -> Ergebnis: nur wenn ItemType=Consumable, Ziel ist der Nutzer selbst, heilt CurrentHp um HealAmount (gedeckelt bei MaxHp), entfernt Item aus dem Slot. TargetAtFullHp falls CurrentHp bereits MaxHp.
- TryAddItemToInventory(characterState, itemId) -> bool (Erfolg/kein freier Platz): erste freie Slot-Nummer verwenden.

## 5. Server: Drops bei Monstertod
In der bestehenden Todeslogik (M4a/M4c): nach dem bisherigen Yang-Gewinn zusätzlich die Drops-Liste des Mob-Typs durchgehen, je Eintrag unabhängig würfeln (injizierbare Zufallsquelle wie überall im Projekt), bei Erfolg TryAddItemToInventory beim tötenden Spieler. Bei vollem Inventar: Drop geht verloren, InventoryActionFailedPacket(InventoryFull) NUR als Info an den Spieler (kein harter Fehler, der Kill selbst bleibt gültig).

## 6. Server: Netzwerk-Anbindung
- Bei Join: InventorySyncPacket an den neuen Spieler (Inventar + Ausrüstung aus der DB geladen). JoinAccepted/EntitySpawn: Appearance-Felder für Armor/Helmet/Weapon/Shield werden jetzt über ItemCatalog aus den EquippedXItemId-Spalten abgeleitet (LookId nachschlagen), nicht mehr direkt aus der DB-Spalte gelesen (siehe 0d).
- EquipItemRequestPacket/UnequipItemRequestPacket/UseConsumableRequestPacket-Handler: rufen die entsprechende World-Methode auf. Erfolg: InventorySyncPacket an den Spieler selbst, UND weil sich die Appearance geändert haben kann (neue Waffe/Rüstung sichtbar) ein aktualisiertes Paket an alle AOI-Beobachter (prüfe, ob dafür ein bestehendes Paket reicht oder ein kleines AppearanceChangedPacket nötig ist, dein Ermessen, im Bericht begründen). Fehlschlag: InventoryActionFailedPacket nur an den Anfragenden, Verbindung bleibt bestehen.
- Persistenz: Inventar- und Ausrüstungsänderungen werden asynchron gespeichert, entsprechend der Empfehlung aus 0b.

## 7. Client (minimal, Debug-Panel-Stil, KEINE echte Inventar-Grafik)
- Erweiterung des bestehenden Debug-Test-Panels (aus M1c/M2c/Dev-Tools): ein Bereich "Inventar" mit 20 Zeilen/Buttons (Slot-Nummer + Item-Name oder "leer"), Klick auf einen belegten Slot: Kontextaktion je nach ItemType (Ausrüstung -> anlegen, Consumable -> benutzen). Ein Bereich "Ausrüstung" zeigt die vier Slots mit aktuellem Item-Namen oder "leer" und einen Knopf zum Ablegen.
- Overlay-Erweiterung: kurze deutsche Fehlermeldung bei InventoryActionFailed.
- Batchmode-Compile-Check.

## 8. Tests
- ItemCatalog: Laden/Validierung (unbekannte LookId etc.).
- TryEquip/TryUnequip/TryUseConsumable/TryAddItemToInventory (kein Netzwerk): erfolgreicher Tausch, Attack/Defense ändert sich korrekt nach Equip/Unequip, Unequip ohne freien Platz schlägt fehl, Consumable heilt korrekt und wird verbraucht, Consumable bei vollem Leben schlägt fehl, Inventar voll -> kein weiteres Item aufnehmbar.
- Drop-Logik: mit fester Zufallsquelle - erwarteter Drop bei Erfolgswurf, kein Drop bei Fehlwurf, voller Inventar-Fall.
- Integration (LiteNetLib Loopback): Bot joint, bekommt InventorySyncPacket mit der Startwaffe ausgerüstet; Bot tötet genug Monster, bis ein Drop-Erfolg eintritt (feste Zufallsquelle im Testserver für Determinismus), InventorySyncPacket zeigt das neue Item; Bot rüstet es aus, Attack/Defense-Änderung wirkt sich im nächsten Kampf sichtbar aus (höherer/niedrigerer Schaden in EntityDamaged).
- Alle bisherigen Tests bleiben grün.

## 9. Doku
CLAUDE.md: neuer Abschnitt "Items und Inventar" (Datenformat, Slot-Modell, was bewusst fehlt: Shop/Boden-Loot/Stapeln/Upgrades), Status/Meilensteine aktualisieren (M5a abgeschlossen, M5b offen: Shop).

## Fertig, wenn
- dotnet build && dotnet test grün (5x stabil), Batchmode-Compile-Check ohne Fehler.
- Mit laufendem Auth+Server+Bot: neuer Charakter joint mit Holzschwert ausgerüstet; nach genug Kills zeigt das Log einen Drop und danach ein sichtbar verändertes InventorySync; Ausrüsten eines Items ändert im nächsten Angriff messbar den Schaden.
- Ein Commit pro Abschnitt.
