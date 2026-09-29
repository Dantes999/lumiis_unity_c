# M6b - Skills (Wuchtschlag, Kreisschlag, Kampfrausch, Hast)

Ziel: Vier Skills mit Stufen 0-17, investierbar über die Fertigkeitspunkte aus M6a. Tasten 1-4 sind echte Spielsteuerung (kein Debug-Feature). Skillpunkte-Verteilung bleibt im Debug-Panel wie die Statuspunkte. KEINE Skillbücher, KEIN Meister-System nach Stufe 17 (spaeter), KEINE Partikeleffekte/Animationen, KEINE Skill-Icons.

## 0. Review zuerst (nicht aendern, nur berichten, Datei:Zeile)
a) Aufbau von TryAttack (M4a/M4c): Reihenfolge der Pruefungen (Ziel existiert/lebt, Kind-Pruefung PvP/NPC, Cooldown, Reichweite), Ausweichwurf (M6a), Todesbehandlung inkl. XP/Yang/Drops (M6a/M5a), reaktive Zielerfassung (M4c). Schlage vor, wie sich das sauber in eine wiederverwendbare ResolveHit(attacker, target, damageMultiplierPercent, nowMs)-Methode ziehen laesst, die sowohl vom normalen Angriff (Multiplikator 100) als auch von Skills genutzt wird, OHNE bestehende TryAttack-Tests zu veraendern.
b) Wie ist der kombinierte Speicher-Mechanismus (M5b/M6a) aufgebaut, und wie fuegt sich eine neue Tabelle CharacterSkillLevel (analog zu CharacterInventoryItem aus M5a) dort ein?
c) RecomputeDerivedStats (M6a): wo genau wird Attack berechnet, und wo laesst sich ein optionaler Angriffs-Buff-Bonus (Prozent auf den bisherigen Attack-Wert) sauber einfuegen, ohne die bestehenden Regressionstests (Level-1-Krieger-Werte) zu veraendern?
d) Wo wird AttackReadyAtMs nach einem Angriff gesetzt (M4a), und wie laesst sich dort ein aktiver Hast-Buff (Cooldown-Reduktion in Prozent) einbauen?
e) Bestaetige: Protocol.Version aktueller Wert (zuletzt 11 laut Bot-Log), damit M6b korrekt +1 rechnet.

## 1. Daten (data/skills.json)
Vier Skills, je mit Id, Name, Type (enum: SingleStrike, AreaAttack, AttackBuff, AttackSpeedBuff), und pro Stufe 1-17 eigene Werte (Array-Eintrag pro Stufe, keine Formel):
- Wuchtschlag (SingleStrike): je Stufe SpCost, CooldownMs (Skill-eigener Cooldown, getrennt vom normalen Angriffs-Cooldown), DamageMultiplierPercent (Platzhalter: Stufe 1 ~180, Stufe 17 ~450).
- Kreisschlag (AreaAttack): je Stufe SpCost (hoeher als Wuchtschlag), CooldownMs (laenger), DamageMultiplierPercent (niedriger als Wuchtschlag, Platzhalter Stufe 1 ~100, Stufe 17 ~250). Zusaetzlich ein konstantes RadiusWire fuer den ganzen Skill (nicht pro Stufe), Platzhalter 200.
- Kampfrausch (AttackBuff): je Stufe SpCost, CooldownMs, AttackBonusPercent (Platzhalter Stufe 1 ~10, Stufe 17 ~50). Konstantes DurationMs fuer den ganzen Skill, Platzhalter 15000.
- Hast (AttackSpeedBuff): je Stufe SpCost, CooldownMs, CooldownReductionPercent (Platzhalter Stufe 1 ~2, Stufe 17 EXAKT 20, wie vom User vorgegeben). Konstantes DurationMs, Platzhalter 15000.
Alle Zahlen als klar erkennbare Platzhalter markiert (Kommentarfeld oder Doku-Hinweis), spaetere Justierung erwartet.

## 2. Datenbank (Lumiis.Data)
Neue Tabelle CharacterSkillLevel: Id, CharacterId (FK), SkillId (int, referenziert skills.json, kein DB-FK), Level (int, 1-17). Unique-Constraint auf (CharacterId, SkillId). Migration. Neue Charaktere haben keine Zeilen (alle Skills auf Stufe 0 = nicht gelernt).

## 3. Shared (netstandard2.1 / C# 9)
- SkillType enum (byte): SingleStrike, AreaAttack, AttackBuff, AttackSpeedBuff.
- BuffType enum (byte): AttackBuff, AttackSpeedBuff.
- UseSkillRequestPacket (Client->Server): SkillId, TargetEntityId (0 = kein Ziel/Selbstziel).
- UseSkillFailedPacket (Server->Client, nur an den Anfragenden): Reason (enum: NotLearned, NotEnoughSp, OnCooldown, TargetRequired, TargetNotFound, TargetAlreadyDead, OutOfRange, TargetIsPlayer, TargetNotAttackable).
- ActiveBuffsChangedPacket (Server->Client, nur an den Besitzer): Liste von (BuffType, RemainingMs). Leere Liste = keine aktiven Buffs. KEINE absoluten Zeitstempel (Projekt-Konvention: keine Client-Uhrensynchronisation).
- AllocateSkillPointRequestPacket (Client->Server): SkillId.
- CharacterStatsPacket (M6a) erweitern: Liste von (SkillId, Level) fuer alle Skills mit Level > 0 (analog zum Rest des Pakets: vollstaendiger eigener Stand).
- Protocol.Version = ermittelter Wert aus 0e + 1. Tests: Round-trip aller neuen/geaenderten Pakete, PacketFactory-Vollstaendigkeitstest bleibt gruen.

## 4. Server: Logik (World, kein Netzwerk, testbar)
- SkillCatalog laden/validieren (analog zu anderen Katalogen): 17 Stufen pro Skill, positive Werte, Startabbruch mit klarer Meldung bei Verstoss.
- ResolveHit gemaess Empfehlung aus 0a extrahieren (WICHTIG: bestehende TryAttack-Tests muessen unveraendert gruen bleiben - das ist ein Regressionstest fuer die Extraktion selbst).
- TryUseSkill(userEntityId, skillId, targetEntityId, nowMs) -> Ergebnis oder Fehlgrund:
  - Skill existiert, Charakter hat ihn gelernt (Level > 0), genug SP, Skill-eigener Cooldown bereit (pro Skill EIGENER Tracker, vier unabhaengige Cooldowns pro Charakter, nicht ein globaler).
  - SingleStrike: TargetEntityId erforderlich, Reichweite wie beim normalen Angriff (MeleeRangeWire), dann ResolveHit mit dem stufenabhaengigen DamageMultiplierPercent.
  - AreaAttack: kein Ziel noetig, findet alle gueltigen Ziele (anderer Kind als der Nutzer, lebendig) innerhalb RadiusWire um den Nutzer, ruft ResolveHit fuer jedes Ziel einzeln auf (jedes Ziel weicht unabhaengig aus).
  - AttackBuff/AttackSpeedBuff: kein Ziel noetig, setzt den passenden Buff-Zustand (Typ, ExpiresAtMs intern, Wirkungsstaerke aus der Stufe) am Nutzer, ersetzt einen ggf. schon aktiven Buff desselben Typs (kein Stapeln).
  - Bei Erfolg: SP abziehen, Skill-Cooldown setzen.
- Buff-Zustand: pro aktivem Buff Typ, Wirkungsstaerke, ExpiresAtMs. World.Tick entfernt abgelaufene Buffs. Buffs ueberleben KEINEN Disconnect (beim Verbindungsende geloescht, keine Persistenz).
- RecomputeDerivedStats (M6a) erweitern: nach der bisherigen Attack-Berechnung, falls Kampfrausch aktiv, AttackBonusPercent addieren (gerundet). Aufruf von RecomputeDerivedStats bei Buff-Start UND Buff-Ende (damit der Wert sofort korrekt ist).
- Angriffs-Cooldown-Berechnung (0d): falls Hast aktiv, effektiver Cooldown = Basis * (100 - CooldownReductionPercent) / 100, sonst unveraendert.

## 5. Server: Netzwerk und Persistenz
- UseSkillRequestPacket-Handler: ruft TryUseSkill auf. Erfolg bei Schadens-Skills: EntityDamaged/EntityDied/EntityDodged wie beim normalen Angriff (ResolveHit liefert das bereits). Erfolg bei Buff-Skills: ActiveBuffsChangedPacket an den Nutzer selbst, PLUS falls sich Attack/Defense geaendert hat: dieselbe Benachrichtigung wie bei Equip/Unequip (M5a) - pruefe, ob CharacterStatsPacket dafuer reicht oder ob ein leichteres Paket sinnvoller ist, dein Ermessen. Fehlschlag: UseSkillFailedPacket nur an den Anfragenden, Verbindung bleibt bestehen.
- Bei Buff-Ablauf (World.Tick): ActiveBuffsChangedPacket (aktualisierte/leere Liste) an den betroffenen Spieler, plus Stats-Update falls noetig.
- AllocateSkillPointRequest-Handler: gleiche Konvention wie AllocateStatPoint aus M6a (ungueltige Anfragen werden verworfen und geloggt, Server antwortet mit aktuellem CharacterStatsPacket, keine Trennung, kein Fehlerpaket). Braucht UnspentSkillPoints > 0 UND aktuelle Stufe < 17.
- Beim Join: CharacterStatsPacket (jetzt mit Skillstufen) wird ohnehin schon gesendet (M6a), keine Aenderung an der Reihenfolge noetig, nur am Inhalt.
- Persistenz: Skillstufen ueber denselben kombinierten Save-Mechanismus wie Statuspunkte, SOFORT bei Punkteverteilung (nicht periodisch).

## 6. Client
- Echte Tasten 1-4 (NICHT hinter #if UNITY_EDITOR/DEVELOPMENT_BUILD, das ist Spielsteuerung): sendet UseSkillRequestPacket fuer den jeweiligen Skill. Bei SingleStrike: nutzt das aktuell per Rechtsklick anvisierte Ziel (falls vorhanden), sonst UseSkillFailedPacket(TargetRequired) abwarten und Meldung zeigen. Bei den anderen drei Skills: TargetEntityId=0.
- Overlay-Erweiterungen: aktive Buffs mit lokal heruntergezaehlter Restzeit (aus ActiveBuffsChangedPacket, RemainingMs bei Empfang gespeichert, danach lokal mit Time.deltaTime dekrementiert), Skill-Cooldowns (visuell, z. B. Text "Wuchtschlag: 2.3s"), deutsche Fehlermeldungen je UseSkillFailedReason.
- Debug-Test-Panel (weiterhin #if UNITY_EDITOR/DEVELOPMENT_BUILD): neuer Bereich "Skills" neben "Charakter", zeigt alle vier Skills mit aktueller Stufe (0-17) und einen "Punkt investieren"-Knopf pro Skill (nur aktiv bei UnspentSkillPoints > 0 und Stufe < 17), analog zum Statuspunkte-Bereich aus M6a.
- Batchmode-Compile-Check, tools/test-client-playmode.sh muss gruen bleiben (ggf. anpassen, falls die vier Debug-Slot-Cycler-Bereiche durch den neuen Skill-Bereich im Layout kollidieren).

## 7. Tests
- SkillCatalog: Laden, Validierungsfehler.
- ResolveHit-Extraktion: ALLE bestehenden TryAttack-Tests (M4a/M4c/M6a) bleiben unveraendert gruen - das ist der Beweis, dass die Extraktion verhaltensgleich war.
- TryUseSkill je Skill-Typ (kein Netzwerk, injizierbare Zeit/Zufall): nicht gelernt abgelehnt, zu wenig SP abgelehnt, Skill-eigener Cooldown abgelehnt, vier Skill-Cooldowns unabhaengig voneinander; Wuchtschlag: Schaden mit korrektem Multiplikator, ausser Reichweite abgelehnt, kein Ziel abgelehnt; Kreisschlag: trifft mehrere Ziele im Radius, laesst Ziele ausserhalb/tote/eigene-Kind-Ziele aus, jedes Ziel weicht unabhaengig aus; Kampfrausch: Attack steigt sofort um den erwarteten Betrag, faellt nach Ablauf zurueck, kein Stapeln bei erneutem Cast; Hast: effektiver Angriffs-Cooldown sinkt um den erwarteten Prozentsatz, insbesondere Stufe 17 = exakt 20 Prozent, faellt nach Ablauf zurueck.
- AllocateSkillPoint: ohne Punkte abgelehnt, bei Stufe 17 abgelehnt, sonst wirksam.
- Integration (LiteNetLib Loopback, DB InMemory): Punkt in einen Skill investieren, CharacterStats zeigt die neue Stufe; Wuchtschlag gegen ein Monster ausloesen, EntityDamaged mit erhoehtem Schaden gegenueber dem normalen Angriff kommt an; Kreisschlag gegen mehrere Monster, mehrere EntityDamaged-Pakete kommen an; Kampfrausch ausloesen, ActiveBuffsChanged kommt an, danach ein normaler Angriff zeigt erhoehten Schaden; Hast ausloesen und pruefen, dass ein zweiter Angriff frueher moeglich ist als ohne Buff (Fake-Zeit). Regressionstest: Skillstufen ueberleben Disconnect und Rejoin.
- Alle bisherigen Tests bleiben gruen.

## 8. Doku
CLAUDE.md: neuer Abschnitt "Skills" (die vier Skills mit Kurzbeschreibung, Stufen 0-17, alle Zahlen sind Platzhalter mit Fundort in skills.json, keine Persistenz von Buffs/Cooldowns, spaeter: Meister-System nach Stufe 17). Status/Meilensteine aktualisieren (M6b abgeschlossen, M6 damit komplett, M7 offen: Sozial/Chat/Gruppe laut urspruenglichem Fahrplan, oder Upgrade/Lumiis-Steine falls wir bei M6c/M6d bleiben - NICHT selbst entscheiden, als offene Frage markieren).

## Fertig, wenn
- dotnet build und dotnet test gruen (5x stabil), Batchmode-Compile-Check und tools/test-client-playmode.sh ohne Fehler.
- Mit laufendem Auth+Server+Bot (fuer den allgemeinen Verbindungstest) UND gezielten Integrationstests fuer die Skill-Mechanik selbst (Bot muss nicht zwingend Skills nutzen koennen, die LiteNetLib-Tests aus Abschnitt 7 sind der Nachweis).
- Ein Commit pro Abschnitt.
