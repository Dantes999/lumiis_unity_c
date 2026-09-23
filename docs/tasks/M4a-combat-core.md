# M4a – Kampf-Grundlagen (Angriff, Schaden, Sterben, Yang)

Ziel: Der Spieler kann ein sichtbares Monster angreifen. Server berechnet Schaden serverautoritativ, Monster können sterben und geben Yang, der Spieler selbst kann sterben und wird automatisch respawnt. KEIN Monster-Respawn in diesem Schritt (kommt in M4b, RespawnSeconds aus M3c wird hier noch nicht ausgewertet), KEIN XP/Level, KEIN PvP, KEINE echten Health-Bars (Debug-Overlay-Text reicht).

## 0. Review zuerst (nicht ändern, nur berichten, Datei:Zeile)
a) Wie ist der aktuelle Ablauf, wenn ein Monster despawnt (M3c) - reicht der bestehende Mechanismus, um ein gestorbenes Monster sofort zu entfernen, oder gibt es Annahmen (z. B. "Monster despawnen nie"), die das M4a hier bricht?
b) Wie liest World aktuell Konstanten/Katalogdaten (Vorbild: LookCatalog, MobCatalog) - folge demselben Muster für eine neue ClassCatalog.
c) Bestätige: Wird CharacterId zuverlässig pro Spieler-Entity mitgeführt (aus M3c-Fund bekannt), sodass sich Yang beim Tod eines Monsters korrekt dem tötenden Spieler zuordnen lässt?

## 1. Daten
data/classes.json: Liste von Klassen (aktuell nur "Krieger"): MaxHp, Attack, Defense, MeleeRangeWire, AttackCooldownMs. mobs.json (M3c) erweitern: MaxHp, Attack, Defense, YangMin, YangMax pro Mob-Typ.

## 2. Datenbank (Lumiis.Data)
Character bekommt eine neue Spalte Yang (long, Default 0), Migration erstellen. KEINE Spalten für Leben/Angriff/Verteidigung (siehe Kontext oben, die sind Server-Konstanten aus classes.json). Lumiis.Auth: Charaktererstellung setzt Yang auf 0, unverändert sonst.

## 3. Shared (netstandard2.1 / C# 9)
- AttackRequestPacket (Client->Server): TargetEntityId.
- EntityDamagedPacket (Server->Client): AttackerEntityId, TargetEntityId, Damage, TargetHpAfter, TargetMaxHp.
- EntityDiedPacket (Server->Client): EntityId.
- AttackFailedPacket (Server->Client, nur an den Angreifer): Reason (enum: TargetNotFound, TargetAlreadyDead, OutOfRange, OnCooldown, TargetIsPlayer).
- YangChangedPacket (Server->Client, nur an den Empfänger): NewTotal.
- Protocol.Version auf 5.
- Tests: Round-trip aller neuen Pakete, PacketFactory-Vollständigkeitstest bleibt grün.

## 4. Server: Kampf-Logik (World, kein Netzwerk, testbar)
- ClassCatalog laden/validieren (analog zu MobCatalog).
- Entity bekommt CurrentHp, MaxHp, Attack, Defense (bei Spielern aus ClassCatalog gesetzt beim Join, bei Monstern aus MobCatalog beim Spawnen), AttackReadyAtMs (Cooldown-Tracking, injizierbare Zeitquelle wie an anderer Stelle im Projekt).
- TryAttack(attackerEntityId, targetEntityId, nowMs) -> Ergebnis (Erfolg mit Schaden/NeuesHp/GestorbenFlag, oder Fehlgrund). Prüft in dieser Reihenfolge: Ziel existiert, Ziel lebt, Ziel ist kein Spieler (PvP in M4a ablehnen), Cooldown des Angreifers, Distanz (quadrierte Distanz wie bei AOI, gegen MeleeRangeWire aus der Klasse des Angreifers). Schaden = max(1, AngreiferAttack - ZielDefense). Bei Erreichen von 0 Leben: Ziel als tot markieren, bei Monstern zusätzlich Yang-Betrag (zufällig zwischen YangMin/YangMax, injizierbare Zufallsquelle) zurückgeben, den der Aufrufer dem Angreifer gutschreibt.
- Spielertod: kein Despawn. Stattdessen RespawnAtMs setzen (nowMs + 3000), CurrentHp bleibt 0 bis zum Respawn. World.Tick prüft fällige Spieler-Respawns: Position auf Kartenmitte, CurrentHp auf MaxHp, EntityKind/Appearance unverändert.
- Monstertod: sofortiges Entfernen aus World (wie ein normaler Despawn), KEIN Respawn-Scheduling in M4a (das TODO für M4b klar markieren).

## 5. Server: Netzwerk-Anbindung
- AttackRequestPacket-Handler: ruft World.TryAttack auf. Erfolg: EntityDamagedPacket an alle AOI-Beobachter des Ziels (Angreifer ist wegen Nahkampfreichweite < AOI-Radius immer eingeschlossen, im Bericht kurz bestätigen). Bei Tod zusätzlich EntityDiedPacket an dieselben Beobachter; bei Monstertod danach EntityDespawnPacket; bei Yang-Gewinn YangChangedPacket NUR an den Angreifer (asynchrones Speichern wie bei Position in M2b, gleicher Channel-Mechanismus oder ein neuer nach demselben Muster). Fehlschlag: AttackFailedPacket NUR an den Angreifer, loggen, Verbindung bleibt bestehen.
- Spieler-Respawn (aus World.Tick fällig): EntityMove oder ein einfacher Teleport-Mechanismus (kurze Diskussion im Bericht: gibt es schon ein Paket für "Position hart setzen ohne Bewegung", oder reicht ein EntityStop an der neuen Position? Entscheide dich für die einfachere Variante, die zum bestehenden Protokoll passt) an die AOI-Beobachter der respawnten Entity.

## 6. Client (minimal, Debug-Overlay, keine neue Grafik)
- Rechtsklick auf ein sichtbares Monster sendet AttackRequestPacket mit dessen EntityId (Bounds-Check auf der MonsterView reicht, kein echtes Collider-System nötig).
- Debug-Overlay: eigenes Leben (Aktuell/Max), falls ein Ziel anvisiert ist dessen Name und Leben, kurze Fehlermeldungen bei AttackFailed (deutscher Text je Reason), "Du bist gestorben, Respawn in Kürze..." bei eigenem Tod, aktueller Yang-Betrag.
- Batchmode-Compile-Check.

## 7. Tests
- ClassCatalog: Laden/Validierung.
- TryAttack (kein Netzwerk, injizierbare Zeit/Zufall): Schaden korrekt berechnet, Mindestschaden 1 auch bei hoher Verteidigung, außer Reichweite abgelehnt, Cooldown abgelehnt, totes Ziel abgelehnt, Spieler-Ziel abgelehnt, Monster stirbt bei 0 Leben und liefert Yang im erwarteten Bereich, Spieler stirbt bei 0 Leben und respawnt nach der konfigurierten Zeit mit vollem Leben an der Kartenmitte.
- Integration (LiteNetLib Loopback): Bot A greift Bot B (als Testkonstellation, obwohl PvP im echten Spiel abgelehnt wird - falls das dem Test im Weg steht, nutze stattdessen einen im Test direkt in World eingefügten Monster-Stand-in) - oder besser: Bot greift ein echtes, per SpawnCatalog geladenes Monster in Testreichweite an, EntityDamaged kommt bei Angreifer und einem zusätzlichen Beobachter an, wiederholte Angriffe bis Monster stirbt, EntityDied dann EntityDespawn kommen an, YangChanged kommt nur beim Angreifer an.
- Alle bisherigen Tests bleiben grün.

## 8. Doku
CLAUDE.md: neuer Abschnitt "Kampf" (Formel, was bewusst fehlt: Respawn/XP/PvP/echte Health-Bars), Status/Meilensteine aktualisieren (M4a abgeschlossen, M4b offen: Monster-Respawn und Feinschliff).

## Fertig, wenn
- dotnet build && dotnet test grün (5x stabil).
- Batchmode-Compile-Check ohne Fehler.
- Mit laufendem Auth+Server+Bot in Reichweite eines Monsters (Bot braucht eine neue, einfache Angriffsfunktion oder ein manuelles Test-Kommando - dein Ermessen, wie im Bericht begründet): Log zeigt Schaden, irgendwann Tod des Monsters, Yang-Gutschrift.
- Ein Commit pro Abschnitt.
