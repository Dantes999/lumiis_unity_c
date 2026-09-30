# M6c - Ausruestungs-Aufwertung (+0 bis +9)

Ziel: Angelegte Ausruestung (Waffe/Ruestung/Helm/Schild) laesst sich mit einem Schleifstein auf +1 bis +9 aufwerten. Erfolgschance sinkt mit der Stufe. OHNE Bewahrungsrolle zerstoert ein Fehlschlag das Item komplett. MIT Bewahrungsrolle (zusaetzlich verbraucht) bleibt das Item bei Fehlschlag unveraendert auf seiner aktuellen Stufe. KEIN neuer NPC, KEIN Aufwerten von Inventar-Items direkt (nur angelegte).

## 0. Review zuerst (nicht aendern, nur berichten, Datei:Zeile)
a) Wo genau berechnet RecomputeDerivedStats (M6a) Attack/Defense aus AttackBonus/DefenseBonus der ausgeruesteten Items (M5a)? Dort muss UpgradeLevel * UpgradeBonusPerLevel addiert werden.
b) Aktueller Aufbau von InventorySyncPacket/CharacterInventoryItem (M5a) und den vier Equipped*ItemId-Feldern (M5a/M5b) - wie lassen sich UpgradeLevel-Werte am saubersten ergaenzen (pro Inventarplatz UND pro Equipped-Slot)?
c) Wie sieht der aktuelle kombinierte Speicher-Kanal aus (inzwischen Position/Yang/Inventar/Ausruestung/Level/Xp/Werte/Skills) - wo fuegen sich die neuen UpgradeLevel-Felder ein, ohne dass "neuester Request ersetzt aeltere" etwas verliert?
d) Bestaetige: UseConsumableRequestPacket (M5a) lehnt Items vom Typ UpgradeMaterial/ProtectionScroll korrekt als NotConsumable ab (oder ergaenze das, falls die Pruefung aktuell zu locker ist).
e) Aktueller Wert von Protocol.Version (zuletzt 12 nach M6b, combat-visuals/escape-menu aenderten ihn nicht) - bestaetigen statt annehmen.

## 1. Daten
- ItemType (Shared) erweitert: UpgradeMaterial, ProtectionScroll.
- data/items.json: bestehende Ausruestungs-Items (Waffen/Ruestungen/Helme/Schilde) bekommen UpgradeBonusPerLevel (int, wird je nach Itemtyp auf AttackBonus bzw. DefenseBonus addiert pro Stufe - staerkere Items duerfen hoehere Werte haben als schwaechere). Zwei neue Items: "Schleifstein" (ItemType=UpgradeMaterial, kein Bonus, SellPrice 0, BuyPrice klein), "Bewahrungsrolle" (ItemType=ProtectionScroll, kein Bonus, SellPrice 0, BuyPrice deutlich hoeher als der Schleifstein - sie ist die teure Absicherung).
- data/npcs.json: ShopItemIds des Dorfhaendlers um beide neuen Items ergaenzen.
- data/upgrade.json (neu): MaxUpgradeLevel 9, SuccessChancePercent als Tabelle fuer die Uebergaenge +0->+1 bis +8->+9 (Platzhalter, absteigend: 90, 80, 70, 60, 50, 40, 30, 20, 10), WhetstoneItemId und ProtectionScrollItemId (referenzieren die IDs aus items.json).

## 2. Datenbank (Lumiis.Data)
- CharacterInventoryItem: neue Spalte UpgradeLevel (int, Default 0).
- Character: vier neue Spalten EquippedArmorUpgradeLevel/EquippedHelmetUpgradeLevel/EquippedWeaponUpgradeLevel/EquippedShieldUpgradeLevel (int, Default 0). Migration.

## 3. Shared (netstandard2.1 / C# 9)
- UpgradeItemRequestPacket (Client->Server): EquipmentSlot (Armor/Helmet/Weapon/Shield, bestehendes Enum aus M5a), UseProtectionScroll (bool).
- UpgradeItemResultPacket (Server->Client, nur an den Anfragenden): EquipmentSlot, Success (bool), Destroyed (bool), NewUpgradeLevel (int, nur aussagekraeftig wenn nicht destroyed).
- UpgradeActionFailedPacket (Server->Client, nur an den Anfragenden): Reason (enum: SlotEmpty, AlreadyMaxLevel, NoMaterial, NoProtectionScroll).
- InventorySyncPacket/EntitySpawnPacket-Appearance: pruefe anhand von 0b, ob ein neues Feld noetig ist oder ob bestehende Felder ausreichen (Appearance selbst aendert sich durch Upgrade NICHT, nur die Zahlenwerte - vermutlich reicht CharacterStatsPacket fuer die eigene Anzeige, kein Wire-Umbau der Appearance noetig, bestaetige das im Bericht).
- Protocol.Version = ermittelter Wert aus 0e + 1. Tests: Round-trip aller neuen Pakete, PacketFactory-Vollstaendigkeitstest bleibt gruen.

## 4. Server: Logik (World, kein Netzwerk, testbar)
- UpgradeCatalog laden/validieren: Tabellenlaenge passend zu MaxUpgradeLevel, Prozentwerte 0-100, referenzierte Item-IDs existieren und haben den richtigen ItemType. Startabbruch mit klarer Meldung bei Verstoss.
- TryUpgrade(characterState, equipmentSlot, useProtectionScroll, nowMs) -> Ergebnis oder Fehlgrund:
  - Slot ist belegt (sonst SlotEmpty), aktuelle Stufe < MaxUpgradeLevel (sonst AlreadyMaxLevel).
  - Schleifstein im Inventar vorhanden (Suche nach WhetstoneItemId, sonst NoMaterial). Falls useProtectionScroll: Bewahrungsrolle vorhanden (sonst NoProtectionScroll) - ALLES pruefen BEVOR irgendetwas konsumiert wird.
  - Wuerfeln (injizierbare Zufallsquelle) gegen SuccessChancePercent[aktuelle Stufe].
  - Schleifstein aus dem Inventar entfernen (immer). Bewahrungsrolle entfernen falls verwendet (immer, unabhaengig vom Ausgang).
  - Erfolg: UpgradeLevel des Equipped-Slots +1, RecomputeDerivedStats.
  - Fehlschlag MIT Schutzrolle: keine Aenderung an Slot/Stufe.
  - Fehlschlag OHNE Schutzrolle: Equipped-Slot komplett leeren (ItemId=null, UpgradeLevel=0), RecomputeDerivedStats (Bonus faellt weg).
  - Rueckgabe: Erfolg/Fehlgrund bzw. Success/Destroyed/NewUpgradeLevel.
- TryAddItemToInventory/Equip/Unequip (M5a) muessen UpgradeLevel beim Verschieben zwischen Inventar und Ausruestung mitnehmen (ein Item behaelt seine Stufe egal ob angelegt oder im Inventar) - im Bericht bestaetigen, dass das mit deiner Empfehlung aus 0b so umgesetzt ist.

## 5. Server: Netzwerk und Persistenz
- UpgradeItemRequestPacket-Handler: ruft TryUpgrade auf. Ergebnis (Erfolg oder Fehlschlag, beides kein Netzwerkfehler): UpgradeItemResultPacket an den Spieler, PLUS InventorySyncPacket (Materialverbrauch, ggf. zerstoertes Item) und CharacterStatsPacket (falls sich Attack/Defense geaendert hat) an denselben Spieler. Ungueltige Anfrage (0 Vorbedingung nicht erfuellt): UpgradeActionFailedPacket, Verbindung bleibt bestehen.
- Persistenz: neue UpgradeLevel-Felder ueber denselben kombinierten Save-Kanal, SOFORT nach einem abgeschlossenen Aufwertungsversuch (nicht periodisch, das Ergebnis ist zu wichtig um es zu verlieren).

## 6. Client (Debug-Panel-Stil, KEINE echte Grafik)
- Ausruestungs-Bereich (M5a/M5b) erweitern: pro Slot Anzeige "+N" neben dem Itemnamen, Knopf "Aufwerten" (nur aktiv wenn Slot belegt und Stufe < 9), ein Haekchen "Bewahrungsrolle verwenden" pro Slot (rein clientseitige Vorpruefung ob genug Rollen im Inventar sind fuer die Aktivierung, Server validiert final).
- Overlay-Meldungen (deutsch): Erfolg ("Schwert ist jetzt +3!"), Fehlschlag mit Schutzrolle ("Aufwertung fehlgeschlagen, durch die Bewahrungsrolle gerettet."), Fehlschlag ohne Schutzrolle ("Aufwertung fehlgeschlagen - Schwert wurde zerstoert!"), sowie deutsche Texte je UpgradeActionFailedReason.
- Batchmode-Compile-Check, tools/test-client-playmode.sh bleibt gruen.

## 7. Tests
- UpgradeCatalog: Laden, Validierungsfehler.
- TryUpgrade (kein Netzwerk, injizierbare Zeit/Zufall): erfolgreicher Aufwertungsversuch erhoeht Stufe und Attack/Defense um den erwarteten Betrag; leerer Slot abgelehnt; Stufe 9 abgelehnt; kein Schleifstein abgelehnt; Schutzrolle angefordert aber nicht vorhanden abgelehnt (dabei wird NICHTS verbraucht - expliziter Test); erzwungener Fehlschlag OHNE Schutzrolle zerstoert das Item (Slot leer, Attack/Defense faellt zurueck); erzwungener Fehlschlag MIT Schutzrolle laesst Stufe/Item unveraendert, Schleifstein UND Rolle trotzdem verbraucht; ein Item behaelt seine Stufe beim Ablegen ins Inventar und erneuten Anlegen.
- Integration (LiteNetLib Loopback, DB InMemory): erfolgreicher Aufwertungsversuch liefert UpgradeItemResult, InventorySync, CharacterStats mit erhoehtem Attack; erzwungener Fehlschlag ohne Schutzrolle liefert Destroyed=true und ein InventorySync das den leeren Slot zeigt. Regressionstest: Stufe ueberlebt Disconnect und Rejoin.
- Alle bisherigen Tests bleiben gruen.

## 8. Doku
CLAUDE.md: neuer Abschnitt "Ausruestungs-Aufwertung" (Ablauf, Erfolgstabelle als Platzhalter markiert, Zerstoerungsregel klar benannt), Status/Meilensteine aktualisieren (M6c abgeschlossen, M6d offen: Lumiis-Steine).

## Fertig, wenn
- dotnet build und dotnet test gruen (5x stabil), Batchmode-Compile-Check und tools/test-client-playmode.sh ohne Fehler.
- Mit laufendem Auth+Server+Bot: ein Aufwertungsversuch (erzwungener Erfolg per Test-Seed) zeigt im Log die neue Stufe und den erhoehten Angriffswert.
- Ein Commit pro Abschnitt.
