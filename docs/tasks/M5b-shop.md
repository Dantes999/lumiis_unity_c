# M5b – Shop (NPC-Händler, Kaufen, Verkaufen)

Ziel: Ein NPC-Händler im Dorf. Der Spieler öffnet per Rechtsklick den Shop, kauft Items aus einem festen Sortiment und verkauft Inventar-Items für Yang. NPCs sind unangreifbar und statisch. KEIN Rückkauf, KEIN Mengenkauf, KEIN Dialogsystem, KEINE Quests, KEINE Shop-Grafik über das Debug-Panel hinaus.

## 0. Review zuerst (nicht ändern, nur berichten, Datei:Zeile)
a) TryAttack erlaubt seit M4c jeden Angriff zwischen unterschiedlichen Kinds. Bestätige, dass ein Spieler damit einen NPC angreifen könnte, und schlage den saubersten Weg vor, das zu verbieten (neuer AttackFailedReason-Wert, z. B. TargetNotAttackable). Prüfe ebenfalls, ob die Ziel-Suche der aggressiven Monster (M4c) ausschließlich Spieler-Entities berücksichtigt.
b) EntitySpawnPacket hat aktuell ein MobId-Feld. Für NPCs braucht es eine Typ-ID. Empfehlung: Feld zu TypeId umbenennen (Bedeutung hängt vom EntityKind ab) oder ein zweites Feld? Deine Empfehlung mit Begründung, Ziel ist möglichst kleiner Wire-Umbau.
c) Alle Stellen, die Yang gutschreiben (Monster-Drops, Debug-Tools, sonstige): Liste. Ziel: eine gemeinsame Methode mit Deckel MaxYang.
d) Wie funktionieren die bestehenden Persistenz-Channels für Yang (M4a) und Inventar/Ausrüstung (M5a): getrennt oder gemeinsam, und wie wird die Reihenfolge/Ersetzung mehrerer Requests pro CharacterId gehandhabt? Empfehlung, wie Kauf/Verkauf Yang und Inventar in EINER Transaktion speichern (ein kombinierter Save-Request, der den VOLLSTÄNDIGEN Yang- plus Inventar- plus Ausrüstungsstand trägt, damit "neuester Request ersetzt ältere" sicher bleibt).
e) Welche World.Tick-Schleifen setzen implizit "alles, was kein Spieler ist, ist ein Monster" voraus (Wander-KI, Aggro, Respawn-Scheduling, Save-Berechtigung über CharacterId.HasValue)? Ein NPC darf nirgends wandern, kämpfen, respawnen oder gespeichert werden.

## 1. Daten
- data/items.json erweitern: BuyPrice (long) und SellPrice (long, 0 = nicht verkäuflich) pro Item. Sinnvolle Preise nach deinem Ermessen (Heiltrank günstig, Holzschwert nur Verkaufspreis klein, bessere Ausrüstung teurer), kurz im Bericht begründen.
- data/npcs.json: Liste von NPC-Typen: Id, Name, Appearance (Look-IDs aus looks.json: Body, HairStyle, HairColor, Armor, Helmet, Weapon, Shield), ShopItemIds (Liste von Item-IDs). Ein Platzhalter-Händler "Dorfhändler" mit dem Sortiment: Heiltrank, ein bis zwei Waffen, je eine Rüstung/Helm/Schild.
- data/maps/village/npcs.json: Platzierungen (Id, NpcId, PositionX/Y in Wire): der Händler auf einer begehbaren Zelle nahe der Kartenmitte (ca. 300-600 Wire-Einheiten Abstand, nicht auf einem Monster-Spawn).

## 2. Shared (netstandard2.1 / C# 9)
- EntityKind: Npc ergänzen. EntitySpawnPacket: Typ-ID gemäß Empfehlung aus 0b.
- GameConstants: NpcInteractRangeWire (Vorschlag 300), MaxYang (Vorschlag 2_000_000_000).
- AttackFailedReason: neuer Wert (0a).
- Neue Pakete: OpenShopRequestPacket (Client->Server: NpcEntityId). ShopContentPacket (Server->Client, nur an den Anfragenden: NpcEntityId, Liste von (ItemId, BuyPrice)). BuyItemRequestPacket (Client->Server: NpcEntityId, ItemId). SellItemRequestPacket (Client->Server: NpcEntityId, InventorySlotIndex). ShopActionFailedPacket (Server->Client, nur an den Anfragenden: Reason-Enum: NpcNotFound, NotAShop, OutOfRange, ItemNotInStock, NotEnoughYang, InventoryFull, InvalidSlot, EmptySlot, ItemNotSellable, YangCapReached). DebugGiveYangRequestPacket (Client->Server: Amount, nur wirksam mit --allow-debug-commands, sonst still verworfen wie beim Debug-Spawn).
- Erfolgreiche Käufe/Verkäufe nutzen die bestehenden InventorySyncPacket und YangChangedPacket (kein neues Erfolgspaket).
- Protocol.Version +1 (tatsächlichen aktuellen Wert ermitteln). Tests: Round-trip aller neuen Pakete, PacketFactory-Vollständigkeitstest bleibt grün.

## 3. Server: Kataloge und Spawning
- NpcCatalog (analog zu MobCatalog/ItemCatalog): lädt/validiert npcs.json. Jede ShopItemId muss in ItemCatalog existieren UND BuyPrice > 0 haben, jede Look-ID im jeweiligen Slot von looks.json. Startabbruch mit klarer Meldung inkl. betroffenem NPC/Item.
- NpcSpawnCatalog: lädt/validiert data/maps/village/npcs.json: NpcId existiert, Position laut CollisionMap begehbar.
- Beim Serverstart je Platzierung eine Entity mit Kind=Npc anlegen (EntityId aus dem üblichen Zähler, Name und Appearance aus dem NPC-Typ). Sie hat keinen Wander-Zustand, keine HP-Logik für Kämpfe, keine SpawnPointId, keine CharacterId. Sichtbarkeit läuft unverändert über den AOI-Diff aus M3b.

## 4. Server: Shop-Logik (World, kein Netzwerk, testbar)
- TryOpenShop(playerEntityId, npcEntityId) -> Ergebnis oder Fehlgrund: NPC existiert und ist Npc-Kind, hat ein Sortiment, Distanz <= NpcInteractRangeWire (quadrierte Distanz mit long, wie bei AOI/TryAttack).
- TryBuy(playerEntityId, npcEntityId, itemId): prüft in dieser Reihenfolge NPC/Reichweite (wie oben, JEDES Mal erneut), Item im Sortiment, genug Yang, freier Inventarplatz. Erfolg: Yang abziehen, Item über TryAddItemToInventory (M5a) einfügen.
- TrySell(playerEntityId, npcEntityId, inventorySlotIndex): NPC/Reichweite, Slot gültig und belegt, SellPrice > 0. Erfolg: Slot leeren, Yang gutschreiben über die gemeinsame Methode mit Deckel (0c), bei Überschreitung von MaxYang Abbruch mit YangCapReached (nichts verändern).
- Alle Yang-Gutschriften im Projekt laufen ab jetzt über die gemeinsame Methode mit MaxYang-Deckel (auch Monster-Drops und Debug-Tools, siehe 0c).
- TryAttack: Ziel mit Kind=Npc wird abgelehnt (0a). Bestehende Tests bleiben unverändert grün.

## 5. Server: Netzwerk und Persistenz
- Handler für OpenShop/Buy/Sell: rufen die World-Methoden auf. OpenShop-Erfolg: ShopContentPacket nur an den Anfragenden. Buy/Sell-Erfolg: InventorySyncPacket und YangChangedPacket an den Spieler selbst. Fehlschlag: ShopActionFailedPacket nur an den Anfragenden, Verbindung bleibt bestehen, loggen.
- Persistenz: Kauf und Verkauf speichern Yang UND Inventar/Ausrüstung in EINER Datenbanktransaktion (kombinierter Save-Request laut Empfehlung 0d, mit vollständigem Stand). Im Bericht kurz beschreiben, wie du das eingebaut hast.
- Debug-Handler für DebugGiveYangRequestPacket (nur mit --allow-debug-commands): schreibt Amount über die gemeinsame Yang-Methode gut und meldet YangChangedPacket. Ohne das Flag: still verworfen, nur geloggt.

## 6. Client (Debug-Panel-Stil, KEINE echte Shop-Grafik)
- NPC-Darstellung: Bei EntitySpawn mit Kind=Npc wird dieselbe CharacterView wie bei Spielern verwendet (Aussehen aus dem Spawn-Paket, Facing Down, Idle-Animation), der Name in anderer Farbe (z. B. grün). Debug-Entity-Liste zeigt "NPC".
- Rechtsklick auf einen NPC sendet OpenShopRequestPacket (Bounds-Check wie beim Monster-Angriff). Rechtsklick auf ein Monster bleibt Angriff.
- Shop-Panel (Debug-Panel-Stil, neuer Bereich): Titel mit NPC-Name, Liste der Waren (Name, Preis, Knopf "Kaufen"), darunter die eigenen Inventar-Slots mit Verkaufspreis (Knopf "Verkaufen", nur bei SellPrice > 0), aktueller Yang-Stand, Knopf "Schließen". Das Panel schließt sich außerdem von selbst, wenn sich die Figur weiter als NpcInteractRangeWire vom NPC entfernt (reine Komfortfunktion, der Server prüft ohnehin jede Aktion).
- Overlay: deutsche Fehlermeldungen je ShopActionFailed-Reason und für den neuen AttackFailed-Wert.
- Debug-Test-Panel: Knopf "+1000 Yang" (nur Editor/Development-Build), sendet DebugGiveYangRequestPacket.
- Batchmode-Compile-Check.

## 7. Tests
- NpcCatalog/NpcSpawnCatalog: Laden, Validierungsfehler (unbekannte Item-ID, BuyPrice 0 im Sortiment, unbekannte Look-ID, blockierte Position) mit klarer Meldung.
- TryBuy/TrySell (kein Netzwerk): Kauf erfolgreich (Yang abgezogen, Item im ersten freien Slot), zu wenig Yang, Inventar voll, Item nicht im Sortiment, außer Reichweite, Ziel ist kein NPC; Verkauf erfolgreich (Slot leer, Yang gutgeschrieben), Item ohne SellPrice, leerer Slot, ungültiger Slot, MaxYang-Deckel; TryAttack auf NPC abgelehnt; NPC wandert/greift/respawnt/speichert nicht; aggressive Monster ignorieren NPCs.
- Integration (LiteNetLib Loopback, DB per InMemory): Bot joint nahe dem NPC, sieht ihn per AOI (EntitySpawn Kind=Npc), OpenShop liefert ShopContent, Kauf mit ausreichend Yang (per Test-Seed) liefert InventorySync und YangChanged, Verkauf ebenso, Kauf außer Reichweite wird abgelehnt. REGRESSION: nach Kauf, Disconnect und erneutem Join sind Yang UND Inventar exakt wie vor dem Disconnect (Inventar-Include-Fehler aus M5a darf nicht zurückkehren).
- Alle bisherigen Tests bleiben grün.

## 8. Doku
CLAUDE.md: Abschnitt "Shop und NPCs" (Datenformate, Reichweitenregel, kombinierte Transaktion, was bewusst fehlt), Dev-Tools-Abschnitt um "+1000 Yang" ergänzen, Status/Meilensteine aktualisieren (M5 komplett, M6 offen: Skills und Metin-Kern).

## Fertig, wenn
- dotnet build && dotnet test grün (5x stabil), Batchmode-Compile-Check ohne Fehler.
- Mit laufendem Auth+Server+Bot: Bot in NPC-Nähe sieht den NPC, öffnet den Shop, kauft und verkauft, Log zeigt InventorySync/YangChanged, ein Kauf außer Reichweite scheitert mit klarem Grund.
- Ein Commit pro Abschnitt.
