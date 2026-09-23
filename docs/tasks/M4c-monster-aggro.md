# M4c – Monster greifen an (Aggro, Verfolgung, Leine)

Ziel: Monster können den Spieler angreifen. Aggressive Mobs (Wildhund) greifen von sich aus an, sobald der Spieler in Reichweite kommt. Passive Mobs (Waldschrat) schlagen nur zurück, wenn sie zuerst getroffen werden, verfolgen danach aber genauso hartnäckig. Beide geben auf und kehren zurück, wenn der Spieler zu weit von ihrem Ankerpunkt wegläuft. Zusätzlich: mehr Spawn-Punkte in Spawn-Nähe des Spielers.

## 0. Review zuerst (nicht ändern, nur berichten, Datei:Zeile)
a) Unterstützt TryAttack (M4a) aktuell einen Monster-als-Angreifer-Aufruf, oder geht der Code von "Angreifer ist immer ein Spieler" aus (z. B. der PvP-Check "Ziel ist Spieler -> ablehnen")? Wie lässt sich das am saubersten verallgemeinern, ohne die bestehende Spieler-Angriffslogik/-Tests zu verändern? Deine Empfehlung mit Begründung.
b) Prüfe die Paketfelder von EntityDamagedPacket/EntityDiedPacket: sind sie wirklich generisch (Attacker/Target als beliebige EntityId), oder gibt es eine Stelle, die "Angreifer ist Spieler" voraussetzt? Bestätige, ob Protocol.Version unverändert bleiben kann.
c) Wie unterscheidet sich intern ein debug-gespawntes Monster (SpawnPointId == null, aus den Dev-Tools) von einem Katalog-Monster in Bezug auf seinen Wander-Ankerpunkt (M3c)? Hat es überhaupt einen Anker, den man als Leinen-Zentrum nutzen kann? Falls nicht: wo müsste einer herkommen (z. B. die Position beim Spawnen selbst)?
d) Wie ist der bestehende Wander-KI-Zustand pro Monster aktuell modelliert (State-Feld vs. mehrere nullable Felder)? Baue die Verfolgungs-/Angriffslogik in diesem bestehenden Muster weiter, keine parallele zweite Zustandsverwaltung.

## 1. Mehr Spawn-Punkte (data/maps/village/spawns.json)
Ergänze 5-8 weitere Spawn-Punkte (Mix aus Wildhund/Waldschrat) innerhalb von ca. 200-400 Wire-Einheiten um die Kartenmitte (50000,50000), auf laut collision.bin begehbaren Zellen. Bestehende Punkte (auch die aus den Dev-Tools) bleiben unverändert.

## 2. Daten (data/mobs.json erweitern)
Neue Felder pro Mob-Typ, in derselben Form wie in classes.json: MaxHp, Attack, Defense, MeleeRangeWire, AttackCooldownMs (analog zum Spieler-Krieger aus M4a). Zusätzlich: Aggressive (bool), AggroRangeWire (int, nur relevant wenn Aggressive true), LeashRangeWire (int, Distanz vom Anker, ab der das Ziel aufgegeben wird). Wildhund: Aggressive=true, kleiner AggroRangeWire, etwas größere Leine (er ist der wilde). Waldschrat: Aggressive=false (AggroRangeWire unbenutzt/0), ähnliche Leine. Konkrete Zahlen nach deinem Ermessen, im Bericht kurz begründen.

## 3. Server: Zielerfassung und Verfolgung (World, kein Netzwerk, testbar)
- Pro Tick, für jedes Monster ohne aktuelles Ziel: falls Aggressive, nächstgelegenen lebenden Spieler innerhalb AggroRangeWire suchen (quadrierte Distanz wie bei AOI/TryAttack), bei Fund als Ziel setzen. Passive Monster erhalten ihr Ziel ausschließlich reaktiv (siehe Punkt 4).
- Pro Tick, für jedes Monster MIT Ziel: Zielspieler existiert/lebt noch? Wenn nicht (gestorben, getrennt) -> Ziel fallen lassen, zurück zum Wander-Zustand. Distanz vom Ankerpunkt zum AKTUELLEN Standort des Monsters (nicht zum Spieler) über LeashRangeWire? -> Ziel fallen lassen, Monster bewegt sich zurück zum Anker (bestehende Bewegungslogik, Ziel=Anker), danach zurück zum Wander-Zustand. Sonst: innerhalb MeleeRangeWire? -> angreifen falls Cooldown bereit (gleicher Mechanismus wie Spieler-Cooldown aus M4a, aus AttackCooldownMs des Mob-Typs). Außerhalb MeleeRangeWire? -> Bewegungsziel auf die AKTUELLE Position des Zielspielers setzen (jeden Tick neu, gleiche Kollisions-Klemmung wie überall).
- Solange ein Ziel aktiv ist, pausiert die normale Wander-Zufallsbewegung (M3c) für dieses Monster, sie läuft erst nach Zielverlust weiter.

## 4. Server: Reaktive Zielerfassung bei Treffer (passive Mobs)
In TryAttack (oder der verallgemeinerten Version aus Punkt 0a): wenn ein Spieler ein lebendes Monster trifft und dieses Monster noch kein aktives Ziel hat, setze den Angreifer als Ziel des Monsters (gilt für aggressive UND passive Mobs gleichermaßen - ein Wildhund, der von hinten angegriffen wird, dreht sich natürlich auch um).

## 5. Server: Monster greift Spieler an
Nutze die verallgemeinerte Angriffslogik aus Punkt 0a: Schaden = max(1, MonsterAttack - SpielerDefense), führt bei 0 Leben zum bestehenden Spieler-Tod/Respawn-Mechanismus aus M4a (unverändert). EntityDamagedPacket/EntityDiedPacket an alle AOI-Beobachter, exakt wie beim umgekehrten Fall in M4a.

## 6. Tests
- Unit (kein Netzwerk, injizierbare Zeit): aggressives Monster erfasst Spieler innerhalb AggroRangeWire, ignoriert ihn außerhalb; passives Monster greift NICHT von sich aus an, auch bei Spieler direkt daneben; passives Monster erfasst Ziel nach einem Treffer und verfolgt danach; Monster im Nahkampfbereich greift im Cooldown-Rhythmus an; Monster außerhalb Nahkampfbereich, aber innerhalb Leine, bewegt sich auf den Spieler zu; Monster jenseits LeashRangeWire vom Anker gibt Ziel auf und bewegt sich zurück; Ziel verschwindet (Spieler stirbt/simulierter Disconnect) -> Monster fällt zurück in Wander-Zustand; Spieler stirbt durch Monsterschaden -> bestehender Respawn-Mechanismus greift unverändert.
- Integration (LiteNetLib Loopback): Bot bleibt in der Nähe eines Wildhund-Spawns stehen -> bekommt irgendwann EntityDamaged als Ziel, ohne selbst anzugreifen. Bot greift einen Waldschrat an und bleibt dann stehen -> bekommt danach ebenfalls EntityDamaged, ohne erneut selbst anzugreifen.
- Alle bisherigen Tests bleiben grün.

## 7. Doku
CLAUDE.md: Abschnitt "Kampf" um Monster-Aggro/Verfolgung/Leine ergänzen, Status/Meilensteine aktualisieren (M4 vollständig inkl. Gegenwehr, M5 offen: Items).

## Fertig, wenn
- dotnet build && dotnet test grün (5x stabil).
- Mit laufendem Auth+Server+Bot: Bot bleibt stehen, ein Wildhund in der Nähe greift ihn von sich aus an (Log zeigt EntityDamaged mit dem Wildhund als Attacker); Bot greift einen Waldschrat an und bleibt stehen, der Waldschrat schlägt zurück.
- Ein Commit pro Abschnitt.
