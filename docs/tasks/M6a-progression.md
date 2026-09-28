# M6a – Erfahrung, Level, Werte (ST/HT/DX/IQ), SP, Ausweichen, Speichern von HP/SP

Ziel: Der Charakter bekommt Level (1-25), Erfahrung mit Levelunterschied-Faktor, vier verteilbare Werte, SP als zweite Ressource, Ausweichen, natuerliche Regeneration, und HP/SP/XP/Level werden gespeichert (Beschluss "Weg 2": Leben ueberdauert das Ausloggen, kein Volllade-Heilen mehr beim Login). KEINE Skills (M6b), KEIN Upgrade (M6c), KEINE Lumiis-Steine (M6d), KEIN Stat-Reset, KEINE Monster-Regeneration, KEINE neuen Monstertypen. Fertigkeitspunkte werden nur gesammelt und gespeichert, nicht ausgegeben.

## 0. Review zuerst (nicht aendern, nur berichten, Datei:Zeile)
a) Alle Stellen, die Entity.MaxHp/CurrentHp/Attack/Defense lesen oder schreiben und classes.json-Werte verbrauchen (Join, Respawn, Equip/Unequip aus M5a, TryAttack, Heiltrank, Monster). Ziel: eine einzige Methode RecomputeDerivedStats, die alle abgeleiteten Werte berechnet.
b) Persistenz: Wie sind Positions-Speicherung (M2b) und der kombinierte CharacterSaveRequest (M5b) heute aufgebaut? Empfehlung, wie Level/Xp/Werte/HP/SP dort hineinpassen, ohne dass "neuester Request ersetzt aeltere" Daten verliert (jeder Request traegt den VOLLSTAENDIGEN Stand).
c) Kill-Pfad (M4a/M5a): Ist der Killer dort bekannt, sodass XP am selben Punkt wie Yang und Drops vergeben werden koennen? Gilt derselbe Pfad fuer Debug-gespawnte Monster?
d) Wo werden CurrentHp und Position beim Join und beim Spieler-Respawn (M4a) gesetzt?
e) Aufbau von TryAttack: Reihenfolge der Pruefungen, Zufallsquelle. Wo gehoert der Ausweichwurf hin, damit Cooldown-Verbrauch und reaktive Zielerfassung der Monster (M4c) unveraendert bleiben?
f) Client: Welche eigenen Werte zeigt das Overlay heute, und woher kommt jeweils der Anfangswert beim Join? (Lehre aus dem HP- und dem Yang-Fehler: jeder angezeigte Wert braucht einen Anfangswert.) Bestaetige die Reihenfolge der Join-Pakete: JoinAccepted, InventorySync, YangChanged, dann neu CharacterStats.

## 1. Daten
data/classes.json (Krieger) erweitern. Regel: Ein frischer Krieger (Level 1, Startwerte, Startwaffe) hat exakt dieselben MaxHp/Attack/Defense wie vor M6a. BaseHp, BaseAttack, BaseDefense so herleiten, dass das aufgeht.
- Startwerte (Platzhalter): BaseSt 6, BaseHt 4, BaseDx 3, BaseIq 3.
- Formeln (alle Faktoren als Zahlen in der Datei, alle Platzhalter):
  MaxHp = BaseHp + Ht*HpPerHt + (Level-1)*HpPerLevel
  MaxSp = BaseSp + Iq*SpPerIq + (Level-1)*SpPerLevel
  Attack = BaseAttack + St*AttackPerSt + Waffen-Bonus (M5a)
  Defense = BaseDefense + (Ht / HtPerDefense, ganzzahlig) + Ruestungs-Boni (M5a)
  Ausweichen in Basispunkten (100 = 1 Prozent) = min(DodgeCapBp, Dx*DodgeBpPerDx)
  Vorschlag: HpPerHt 8, HpPerLevel 10, SpPerIq 6, SpPerLevel 4, AttackPerSt 2, HtPerDefense 2, DodgeBpPerDx 50, DodgeCapBp 4000, StatPointsPerLevel 3, SkillPointsPerLevel 1, BaseSp so, dass MaxSp bei Level 1 gleich 30 ist.
- Regeneration (Platzhalter): RegenDelayMs 5000 (nach dem letzten Kampfereignis), RegenIntervalMs 1000, HpRegenPercent 1, SpRegenPercent 2 (vom Maximum, mindestens 1 Punkt).
- Levelaufstieg heilt HP und SP voll. Spieler-Respawn setzt HP und SP auf voll.
data/progression.json (neu): MaxLevel 25. XpToNext als explizite Tabelle fuer Level 1..24 nach der Formel (8+L)*10*L (Formel im Bericht nennen). Levelunterschied-Tabelle (Spielerlevel minus Monsterlevel, Prozent der Basis-XP): <=-4: 120, -3: 115, -2: 110, -1: 105, 0: 100, +1: 95, +2: 90, +3: 80, +4: 70, +5: 60, +6: 45, +7: 30, +8: 20, +9: 15, >=+10: 10.
data/mobs.json: neue Felder Level und BaseXp. Wildhund Level 3, BaseXp 30. Waldschrat Level 6, BaseXp 60 (Regel BaseXp = 10*Level).
XP-Gewinn = BaseXp * Prozent(Levelunterschied) / 100, ganzzahlig abgerundet, mindestens 1 solange Prozent > 0. Ab MaxLevel gibt es keine XP mehr.

## 2. Datenbank (Lumiis.Data)
Character neue Spalten: Level (int, Default 1), Xp (long, Default 0), SpentSt/SpentHt/SpentDx/SpentIq (int, Default 0), UnspentStatPoints (int, Default 0), UnspentSkillPoints (int, Default 0), CurrentHp (int?, null = voll), CurrentSp (int?, null = voll). Migration. Bestehende Charaktere behalten die Defaults. Lumiis.Auth braucht keine Aenderung.

## 3. Shared (netstandard2.1 / C# 9)
- StatKind enum (byte): St, Ht, Dx, Iq.
- CharacterStatsPacket (Server->Client, nur an den Besitzer, immer der VOLLSTAENDIGE eigene Stand): Level, Xp (long), XpToNext (long, 0 bei MaxLevel), St, Ht, Dx, Iq (jeweils Summe aus Basis und verteilten Punkten), UnspentStatPoints, UnspentSkillPoints, MaxHp, CurrentHp, MaxSp, CurrentSp, Attack, Defense, DodgeBp.
- AllocateStatPointRequestPacket (Client->Server): Stat (StatKind).
- EntityDodgedPacket (Server->Client, an die AOI-Beobachter des Ziels): AttackerEntityId, TargetEntityId.
- DebugSetLevelRequestPacket (Client->Server, nur mit --allow-debug-commands): Level.
- Protocol.Version +1 (aktuellen Wert ermitteln). Tests: Round-trip aller neuen Pakete, PacketFactory-Vollstaendigkeitstest bleibt gruen.

## 4. Server: Logik (World, kein Netzwerk, testbar)
- ClassCatalog erweitern, ProgressionCatalog neu, MobCatalog um Level/BaseXp. Validierung mit klarer Meldung beim Start (Tabellenlaenge, MaxLevel, negative Werte).
- RecomputeDerivedStats(entity): einzige Stelle, die MaxHp/MaxSp/Attack/Defense/DodgeBp aus Klasse, Level, Werten und Ausruestung berechnet. Aufruf bei Join, Levelaufstieg, Punkteverteilung, Equip/Unequip. Equip/Unequip veraendern nur Attack/Defense.
- Regeln bei Aenderung von MaxHp: Levelaufstieg heilt voll. Beim Verteilen eines HT-Punktes steigt CurrentHp um dieselbe Differenz wie MaxHp (gleiches fuer SP bei IQ).
- AwardXp(spieler, monsterLevel, baseXp): berechnet den Gewinn, wendet mehrere Levelaufstiege in einer Schleife an, stoppt bei MaxLevel (Xp bleibt dann 0). Pro Levelaufstieg: +StatPointsPerLevel, +SkillPointsPerLevel, volle Heilung, RecomputeDerivedStats.
- TryAllocateStatPoint(spieler, stat): braucht UnspentStatPoints > 0, erhoeht den passenden Spent-Wert, RecomputeDerivedStats.
- Ausweichen in TryAttack (Ergebnis "Dodged"): Hat das Ziel DodgeBp > 0, wird gewuerfelt (injizierbare Zufallsquelle, Wert 0..9999 kleiner als DodgeBp). Bei Ausweichen: kein Schaden, der Cooldown des Angreifers wird trotzdem verbraucht, die reaktive Zielerfassung der Monster (M4c) laeuft trotzdem. Monster haben DodgeBp 0.
- Regeneration: pro Spieler LastCombatAtMs (gesetzt, wenn er Schaden nimmt, austeilt oder einem Treffer ausweicht). World.Tick prueft einmal pro RegenIntervalMs: lebt der Spieler und ist seit RegenDelayMs kein Kampfereignis mehr, dann HP und SP anheben (gedeckelt). Tote regenerieren nicht, Monster in M6a ebenfalls nicht. Injizierbare Zeitquelle.
- Login-Regeln (Join): CurrentHp/CurrentSp aus der Datenbank. null bedeutet voll. Ein Wert kleiner oder gleich 0 (Ausloggen im Tod) gilt als wiederbelebt: HP und SP voll, Position Kartenmitte. Ein Wert ueber dem berechneten Maximum wird auf das Maximum begrenzt.

## 5. Server: Netzwerk und Speichern
- Beim Join: CharacterStatsPacket an den Spieler, nach JoinAccepted, InventorySync und YangChanged.
- CharacterStatsPacket wird erneut gesendet nach: XP-Gewinn, Levelaufstieg, Punkteverteilung, Equip/Unequip, Heiltrank, Respawn und Regeneration (bei Regeneration nur bei tatsaechlicher Aenderung, hoechstens einmal pro RegenIntervalMs). Schaden am eigenen Charakter laeuft weiter ueber EntityDamaged (traegt die HP schon).
- Kill-Pfad: XP am selben Punkt wie Yang und Drops (0c), auch fuer Debug-gespawnte Monster, danach CharacterStatsPacket.
- Handler AllocateStatPointRequest: ungueltige Anfragen (keine Punkte, unbekannter Stat) werden verworfen und geloggt, der Server antwortet mit dem aktuellen CharacterStatsPacket. Keine Trennung, kein neues Fehlerpaket.
- Ausweichen: Statt EntityDamaged geht EntityDodgedPacket an die AOI-Beobachter des Ziels.
- Speichern: Level, Xp, Spent*, Unspent*, CurrentHp, CurrentSp gehen in denselben kombinierten Save-Mechanismus (Empfehlung aus 0b), asynchron. Zeitpunkte: periodisch wie die Position (alle 30 Sekunden), beim Disconnect, SOFORT bei Levelaufstieg und Punkteverteilung. Nicht bei jedem Treffer oder Tick.
- Debug (nur mit --allow-debug-commands, sonst still verworfen wie bei Debug-Spawn und Debug-Yang): DebugSetLevelRequest setzt Level = L (1..MaxLevel), Xp = 0, Spent* = 0, UnspentStatPoints = (L-1)*StatPointsPerLevel, UnspentSkillPoints = (L-1)*SkillPointsPerLevel, HP und SP voll, sendet CharacterStats und speichert. Das dient auch als Stat-Reset zum Testen.

## 6. Client (Debug-Panel-Stil, KEINE echte Charakter-Grafik)
- ClientWorld: ApplyCharacterStats(...) ohne UnityEngine. Die eigenen HP werden ueber die BESTEHENDE gemeinsame ApplyHp gesetzt, es gibt keine zweite Ablage fuer HP. Alle anderen Werte liegen in einem CharacterState-Objekt.
- Debug-Test-Panel, neuer Bereich "Charakter": Level, "XP x/y" (bei MaxLevel "MAX"), HP, SP, ST/HT/DX/IQ mit "+"-Knopf (nur aktiv bei UnspentStatPoints > 0), Statuspunkte, Fertigkeitspunkte (nur Anzeige), Angriff, Verteidigung, Ausweichen in Prozent. Nur Editor und Development-Build. Dazu ein Level-Cycler mit Knopf "Level setzen" (sendet DebugSetLevelRequest).
- Overlay-Meldungen (deutsch): "Level Up! Level N" (erkannt am steigenden Level), "Ausgewichen!" (EntityDodged mit dem eigenen Charakter als Ziel), "Verfehlt!" (EntityDodged mit dem eigenen Charakter als Angreifer).
- Batchmode-Compile-Check. tools/test-client-playmode.sh muss gruen bleiben.

## 7. Tests
- Kataloge: Laden und Validierungsfehler (falsche Tabellenlaenge, negative Werte, MaxLevel).
- Formeln (kein Netzwerk): Level-1-Krieger hat exakt die bisherigen MaxHp/Attack/Defense (Regressionstest gegen die Werte aus M4a); jede Formel mit ein bis zwei Beispielen; Dodge-Deckel greift.
- XP: jede Zeile der Levelunterschied-Tabelle inklusive Extremwerte, Mindestens-1-Regel, mehrere Levelaufstiege in einem Kill, kein XP-Gewinn bei MaxLevel, Punktevergabe pro Levelaufstieg.
- Punkteverteilung: ohne Punkte abgelehnt, mit Punkten wirksam, HT-Punkt erhoeht CurrentHp um dieselbe Differenz wie MaxHp.
- Ausweichen (feste Zufallsquelle): DodgeBp 10000 weicht immer aus, keine Schadenspakete, Cooldown verbraucht, Monster-Zielerfassung greift trotzdem; DodgeBp 0 weicht nie aus.
- Regeneration (Fake-Zeit): keine Regeneration innerhalb von RegenDelayMs nach einem Kampfereignis, danach schrittweise, nie ueber dem Maximum, Tote nicht.
- Login-Regeln: gespeicherte HP 82 ergibt 82 beim Join. null ergibt voll. Kleiner oder gleich 0 ergibt voll und Kartenmitte. Ueber dem Maximum wird begrenzt.
- Integration (LiteNetLib Loopback, DB per InMemory): (1) Nach dem Join kommt CharacterStats mit den gespeicherten Werten. (2) Kill eines Monsters ergibt CharacterStats mit mehr XP, bei kleinen Test-XP-Tabellen ein Levelaufstieg. (3) AllocateStatPoint aendert Werte, ungueltige Anfrage bringt nur den aktuellen Stand zurueck. (4) Regressionstest Persistenz: Level, Xp, verteilte Punkte, HP und SP ueberleben Disconnect und Rejoin exakt. (5) EntityDodged kommt bei erzwungenem Ausweichen an. (6) DebugSetLevel wirkt nur mit dem Flag.
- ClientWorld-Test ohne Unity fuer ApplyCharacterStats (inklusive der Regel: eigene HP nur ueber ApplyHp).
- Alle bisherigen Tests bleiben gruen.

## 8. Doku
CLAUDE.md: neuer Abschnitt "Fortschritt: Level, XP, Werte" (Formeln, alle Platzhalterzahlen und ihr Ort in den Daten, Regenerations- und Login-Regeln, was bewusst fehlt: Skills, Upgrade, Stat-Reset, Monster-Regeneration, Monster fuer hoehere Level). Dev-Tools um "Level setzen" ergaenzen. Status und Meilensteine aktualisieren (M6a abgeschlossen, M6b offen: Skills).

## Fertig, wenn
- dotnet build und dotnet test gruen (5x stabil), Batchmode-Compile-Check und tools/test-client-playmode.sh ohne Fehler.
- Mit laufendem Auth+Server+Bot: Kill ergibt XP, Levelaufstieg laeuft durch, ein Charakter, der mit 82 HP ausgeloggt hat, ist beim naechsten Login bei 82 HP.
- Ein Commit pro Abschnitt.
