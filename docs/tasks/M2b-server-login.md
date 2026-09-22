# M2b – Server verlangt Login (Protokolländerung, Positions-Speicherung)

Ziel: Der Server akzeptiert nur noch Joins mit gültigem Token und einer zugehörigen CharacterId. Appearance und Position kommen aus der Datenbank. Unity wird in dieser Aufgabe NICHT angefasst (kommt in M2c), aber der Bot bekommt einen echten Login-Flow, damit wir Ende-zu-Ende testen können.

## 0. Review zuerst (aus M2a offen gebliebene Lücken)
Berichte, dann fixe, als eigener Commit:
a) Ist POST /characters' Namens-Eindeutigkeit aktuell global oder nur pro Account? Sie MUSS global eindeutig sein (case-insensitive, wie bisher bei der In-Memory-Namensvergabe des Servers). Falls nur pro Account: Unique-Index/Constraint auf Character.Name korrigieren, Migration ergänzen, 409 bei Kollision.
b) Validiert POST /characters den Charakternamen aktuell mit NameRules (Shared, ASCII, 3-16 Zeichen, dieselbe Regel wie früher für client-seitige Namen)? Falls nicht: ergänzen, 400 bei ungültigem Namen.
dotnet build && dotnet test nach dem Fix, alle bisherigen Tests bleiben grün.

## 1. Shared (netstandard2.1 / C# 9)
- Protocol.Version auf 3.
- JoinRequestPacket: Token (string), CharacterId (int). Name-Feld entfernt.
- JoinRejectReason sauber neu: VersionMismatch=1, InvalidToken=2, TokenExpired=3, CharacterNotFound=4, CharacterAlreadyOnline=5, ServerFull=6.
- Tests: Round-trip des geänderten Pakets, PacketFactory-Vollständigkeitstest bleibt grün.

## 2. Server (Referenz auf Lumiis.Data)
- Server liest jetzt ebenfalls LUMIIS_DB_* und LUMIIS_TOKEN_SECRET (dieselben Variablen, dieselbe Fail-Fast-Regel wie bei Auth: fehlt eine, bricht der Start mit klarer Meldung ab). KEINE eigene Migration beim Start (das bleibt Sache von Auth in Development).
- Join-Ablauf: Token lokal validieren (TokenService aus Lumiis.Data, kein HTTP-Aufruf zu Auth) -> bei ungültig/abgelaufen JoinRejected(InvalidToken/TokenExpired). Charakter per CharacterId + AccountId aus dem Token laden (Eigentümerschaft prüfen!) -> bei nicht gefunden oder falschem Account JoinRejected(CharacterNotFound). Prüfen, ob diese CharacterId schon online ist (neues Dictionary<int,int> CharacterId->EntityId in World, parallel zur Namensregistrierung) -> bei ja JoinRejected(CharacterAlreadyOnline).
- World.TryJoin erweitert um characterId (int) und initiale Position (int? x, int? y, null = Kartenmitte wie bisher). World bleibt frei von EF Core/Lumiis.Data-Referenzen, bekommt nur einfache Werte übergeben. Vorhandene Namens-Eindeutigkeitsprüfung in World kann entfallen, wenn Punkt 0a das schon auf DB-Ebene sicherstellt - kurz begründen, wofür du dich entscheidest.
- Appearance kommt aus der geladenen Character-Zeile, nicht mehr aus dem FNV-1a-Hash des Namens (der Hash-Mechanismus wird entfernt, das war explizit als Übergang markiert).
- Beim Disconnect: letzte Position sofort speichern (nicht auf den nächsten periodischen Save warten), CharacterId aus dem Online-Dictionary entfernen.

## 3. Positions-Speicherung
- Channel<PositionSaveRequest> (bounded, sinnvolle Kapazität, Verhalten bei Überlauf begründen: z. B. neueste Anfrage pro CharacterId ersetzt alte). GameServer.Update() enqueued nicht-blockierend am Ende jedes Ticks für Spieler, deren letzter Save >= 30s her ist.
- Ein Hintergrund-Task konsumiert die Queue und schreibt Position per EF Core (Upsert nach CharacterId). Fehler werden geloggt, bringen den Server nicht zum Absturz (bestehende Fehlerbehandlungs-Konvention).
- Tests: Unit-Test der Speicher-Entscheidung (welche Spieler sind fällig) ohne echte DB. Ein Integrationstest mit EF-Core-InMemory-Provider, der prüft, dass nach Disconnect die Position in der (InMemory-)Datenbank steht.

## 4. Bot
- Neue Optionen --auth-url (Default http://localhost:5080), Passwort intern fest als "botpass-<name>-2026" (nur Test-Tool, kein echtes Geheimnis, das im Code stehen darf, wenn es klar als solches gekennzeichnet ist).
- Ablauf pro Bot: POST /register, bei 409 stattdessen POST /login (login-oder-registrieren). GET /characters, falls leer: POST /characters mit dem Bot-Namen, dann die erhaltene CharacterId verwenden. Danach wie bisher per LiteNetLib verbinden, aber JoinRequestPacket jetzt mit Token und CharacterId statt Name.
- Klare Log-Zeile pro Bot für jeden HTTP-Schritt (Register/Login-Ergebnis, Charakter gefunden/erstellt).

## 5. Tests
- Alle bisherigen Server-Tests (Movement, Fristen, Exceptions-Sicherheit) weiterhin ohne echte Postgres lauffähig, über EF-Core-InMemory mit vorbereiteten Test-Accounts/-Charakteren.
- Neue Integrationstests (echtes LiteNetLib auf Loopback wie bisher, DB per InMemory): gültiger Join mit Token+CharacterId gelingt, Appearance stimmt mit der DB-Zeile überein; abgelaufenes/manipuliertes Token -> JoinRejected(InvalidToken/TokenExpired); fremde CharacterId (gehört zu anderem Account) -> JoinRejected(CharacterNotFound); zweiter Join mit derselben CharacterId, während der erste noch verbunden ist -> JoinRejected(CharacterAlreadyOnline), nach Disconnect des ersten klappt ein erneuter Join wieder.
- dotnet test bleibt vollständig ohne laufendes Docker/Postgres lauffähig.

## 6. Doku
- CLAUDE.md: Server-seitige Pflicht-Umgebungsvariablen ergänzen (jetzt auch für Server, nicht nur Auth), "Bekannte Lücken" um "Positions-Speicherintervall fix bei 30s, keine Konfiguration" ergänzen, Status/Meilensteine aktualisieren (M2b abgeschlossen, M2c offen: Unity-Login-UI).

## Fertig, wenn
- cd server && dotnet build && dotnet test grün (5x stabil), ohne laufendes Postgres.
- Mit laufendem Postgres, Auth und Server: Bot -- --count 3 loggt sich per HTTP ein, legt bei Bedarf Charaktere an, joint erfolgreich, Namen und Aussehen stimmen mit dem, was in der DB steht. Erneuter Lauf von Bot --count 3 (dieselben Namen) meldet sich per Login an statt erneut zu registrieren und joint trotzdem erfolgreich.
- Zwei Bot-Prozesse mit demselben --name gleichzeitig: der zweite wird mit CharacterAlreadyOnline abgelehnt.
- Ein Commit pro Abschnitt.
