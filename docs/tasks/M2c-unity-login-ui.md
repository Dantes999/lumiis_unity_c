# M2c – Unity-Login und Charakterwahl (UI Toolkit)

Ziel: Die bisherige Debug-Login-Box (freies Namensfeld, direkt JoinRequest) wird durch einen echten Ablauf ersetzt: Login/Registrierung per HTTP gegen Lumiis.Auth, Charakterliste, Charakter wählen oder anlegen, danach Verbindung zum Server mit Token und CharacterId. Das bestehende Debug-Test-Panel (Testboard, Sortierfeld, Slot-Wechsler aus M1c) bleibt unverändert und wird erst NACH erfolgreichem Join sichtbar, wie bisher.

## 0. Kontrakt-Fix zuerst (Lumiis.Auth)
GET /characters liefert aktuell ein rohes JSON-Array. Unitys JsonUtility kann Top-Level-Arrays nicht parsen. Ändere die Antwort zu { "characters": [...] }, passe die bestehenden WebApplicationFactory-Tests entsprechend an. dotnet build && dotnet test, danach eigener Commit.

## 1. UI Toolkit, Inhalte (client/Assets/Resources/UI/)
- lumiis.uss: minimale, funktionale Gestaltung (lesbar, keine Design-Arbeit, das kommt später mit dem Art-Stil).
- Login.uxml: Felder Benutzername/Passwort, Buttons "Anmelden" und "Registrieren", Fehlertext-Label, ausklappbarer Bereich "Erweitert" mit Auth-URL (Default http://127.0.0.1:5080), Server-Host (Default 127.0.0.1), Server-Port (Default 7777) - Vorbelegung aus den bisherigen Debug-Feldern übernehmen.
- CharacterSelect.uxml: Liste der Charaktere (Name, auswählbar), Feld für neuen Charakternamen plus Button "Charakter erstellen", Button "Abmelden" (zurück zu Login).

## 2. LoginController (C#, code-first, ein UIDocument, das per Bootstrap erzeugt wird und zwischen den beiden UXML-Inhalten umschaltet)
- Zustände: LoggedOut -> LoggingIn -> CharacterSelect -> Connecting -> InGame -> (Disconnect) zurück zu LoggedOut.
- HTTP-Aufrufe über UnityWebRequest gegen Lumiis.Auth (POST /register, POST /login, GET /characters, POST /characters), Header Authorization: Bearer <token> nach dem Login. Nutze async/await mit UnityEngine.Awaitable (Unity 6), falls das mit SendWebRequest() nicht sauber funktioniert, wähle einen Callback/TaskCompletionSource-Ansatz und begründe kurz, wofür du dich entschieden hast.
- Clientseitige Vorprüfung von Benutzername/Passwort (Länge, wie NameRules) vor dem Netzwerk-Aufruf, verbindliche Prüfung bleibt aber immer der Server.
- Fehleranzeige: HTTP-Fehler (401 falsches Passwort, 409 Name vergeben, Netzwerkfehler zu Auth nicht erreichbar) UND JoinRejectReason (VersionMismatch, InvalidToken, TokenExpired, CharacterNotFound, CharacterAlreadyOnline, ServerFull) als verständlicher deutscher Text im UI, nicht nur im Log.
- Letzter Benutzername (NICHT Passwort, NICHT Token) wird per PlayerPrefs gemerkt und beim nächsten Start vorausgefüllt, reine Komfortfunktion.
- Token wird nur im Speicher gehalten, nicht auf die Platte geschrieben. Nach einem Disconnect (auch unfreiwillig) zurück zu LoggedOut, Passwort-Feld leer, Benutzername bleibt.
- NetClient.Connect bekommt Token und CharacterId statt Name, ruft JoinRequestPacket entsprechend dem M2b-Protokoll auf.

## 3. Aufräumen
- Die alte Debug-Login-Box (freies Namensfeld, direkter Connect) wird entfernt, nicht nur verdeckt.
- Prüfe, ob NetClient/GameClient noch anderweitig einen Namen vom User erwartet, der jetzt aus dem gewählten Charakter kommen muss.

## 4. Prüfung
Batchmode-Compile-Check wie bisher. Da UI Toolkit/UnityWebRequest im Zusammenspiel nur im Play-Modus wirklich sichtbar wird, keine Unity-Testautomatisierung neu aufsetzen, stattdessen eine kurze, konkrete Checkliste ans Ende von CLAUDE.md für den manuellen Test durch den User (siehe unten, du ergänzt sie).

## 5. Doku
CLAUDE.md: Abschnitt Client aktualisieren (Login-Ablauf ersetzt die alte Debug-Box), Checkliste für den manuellen Test ergänzen, Status/Meilensteine (M2 komplett nach dieser Aufgabe), "Bekannte Lücken" um "kein Token-Refresh, Session endet nach 30 Minuten" ergänzen.

## Fertig, wenn
- cd server && dotnet build && dotnet test grün (5x stabil).
- Batchmode-Compile-Check ohne Fehler.
- Ein Commit pro Abschnitt.
