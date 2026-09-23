# Dev-Tools: Näher gelegene Spawns + Debug-Monster-Knopf

Ziel: Kampf-Testing ohne minutenlanges Laufen. Zwei unabhängige Teile.

## 1. Mehr Spawn-Punkte (data/maps/village/spawns.json)
Ergänze 3-4 zusätzliche Spawn-Punkte (Mix aus Wildhund/Waldschrat) innerhalb von ca. 300-500 Wire-Einheiten um die Kartenmitte (50000,50000), auf laut collision.bin begehbaren Zellen (prüfen wie in M3c). Bestehende Spawn-Punkte bleiben unverändert. Serverstart validiert wie gehabt (SpawnCatalog).

## 2. Debug-Spawn-Befehl (nur Entwicklungsmodus)
- Server: neue Startoption --allow-debug-commands (Default aus/false). Ohne diese Option wird der neue Befehl unten komplett ignoriert/abgelehnt, als gäbe es ihn nicht.
- Shared: DebugSpawnMonsterRequestPacket (Client->Server): MobId (int). Protocol.Version auf 6. Round-trip-Test.
- Server-Handler: nur wirksam, wenn --allow-debug-commands aktiv ist (sonst AttackFailed-artige Ablehnung oder stilles Verwerfen, dein Ermessen, im Bericht begründen). Spawnt ein Monster des angegebenen MobId-Typs an der aktuellen Position des Absenders plus einem kleinen zufälligen Versatz (z. B. 100-300 Wire-Einheiten, auf begehbare Zelle geklemmt/verschoben). WICHTIG: Dieses Monster ist NICHT an einen SpawnPoint aus spawns.json gebunden - stirbt es, passiert genau das, was auch sonst beim Monstertod passiert (aktuell: entfernen, kein Respawn, siehe M4a). Stelle sicher, dass diese Kennzeichnung "kein SpawnPoint" so gebaut ist, dass sie mit dem kommenden M4b (Monster-Respawn pro SpawnPoint) nicht kollidiert - kurz im Bericht erklären, wie du das unterscheidbar machst (z. B. nullable SpawnPointId an der Entity).
- Client: Debug-Testpanel (aus M1c/M2c, #if UNITY_EDITOR || DEVELOPMENT_BUILD) bekommt einen neuen Bereich "Monster spawnen": ein "<"/">"-Cycler für den Mob-Typ (aus dem geladenen MobCatalog/looks-artigen Client-Katalog) plus einen Knopf "Spawnen", der DebugSpawnMonsterRequestPacket sendet.
- tools/dev-env.sh bzw. die Doku-Beispielaufrufe für den lokalen Server ergänzen um --allow-debug-commands, klar als "nur lokale Entwicklung" gekennzeichnet.

## 3. Tests
- SpawnCatalog lädt die erweiterte spawns.json ohne Fehler.
- Serverseitig: Debug-Spawn-Handler ohne --allow-debug-commands hat keine Wirkung (kein Monster erscheint); mit aktivierter Option erscheint ein Monster nahe der Absenderposition, ist normal angreifbar (nutzt bestehende TryAttack-Logik unverändert), sein Tod triggert KEIN Respawn-Scheduling (da kein SpawnPointId).
- Alle bisherigen Tests bleiben grün.

## 4. Doku
CLAUDE.md: kurzer Abschnitt "Dev-Tools" (--allow-debug-commands, wofür, wie starten), Hinweis dass dies nie in einem "echten" Serverstart aktiv sein soll.

## Fertig, wenn
- dotnet build && dotnet test grün (5x stabil), Batchmode-Compile-Check ohne Fehler.
- Server OHNE --allow-debug-commands: Debug-Spawn-Knopf im Client hat sichtbar keine Wirkung.
- Server MIT --allow-debug-commands: Knopf spawnt sofort ein Monster direkt neben dem Spieler, angreifbar wie gewohnt.
- Ein Commit pro Abschnitt.
