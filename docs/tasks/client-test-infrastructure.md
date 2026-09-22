# Client-Testinfrastruktur (Login-Logik entkoppeln, PlayMode-Tests automatisieren)

Ziel: Zwei Ebenen automatischer Prüfung für den Unity-Client, damit UI-Bugs wie in der M2c-Login-Debugging-Runde (NullReferenceException durch rootVisualElement-Timing, zwei gleichzeitig sichtbare UI-Wurzeln, verwaiste OnGUI-Box) künftig ohne manuelles Klicken auffallen. Anlass: alle drei Bugs bestanden den Batchmode-Compile-Check, waren aber Laufzeitfehler.

## 1. LoginFlow (reines C#, kein UnityEngine)
- IAuthClient-Interface: Register/Login/GetCharacters/CreateCharacter als Task-Methoden, mit einfachen Ergebnis-Typen (Erfolg/Fehlercode/Daten), keine UnityEngine- oder UnityWebRequest-Typen im Vertrag.
- LoginFlow: hält Zustand (LoggedOut, LoggingIn, CharacterSelect, Connecting, InGame), Methoden wie TryRegisterAsync/TryLoginAsync/SelectCharacter/CreateCharacterAsync, validiert Eingaben (wie bisher, 3-32 Zeichen ASCII) VOR jedem Aufruf von IAuthClient, übersetzt Fehler (401/409/Netzwerkfehler/JoinRejectReason) in die bestehenden deutschen Texte. Keine UnityEngine-Referenz in dieser Datei.
- UnityWebRequestAuthClient: die einzige Implementierung von IAuthClient, die tatsächlich UnityWebRequest nutzt, bleibt dünn (nur HTTP-Aufruf plus Mapping in die einfachen Ergebnis-Typen), keine eigene Logik.
- LoginController (MonoBehaviour) wird auf reines UI-Verdrahten reduziert: hält eine LoginFlow-Instanz, verbindet Button-Klicks mit LoginFlow-Methoden, aktualisiert UI-Elemente anhand von LoginFlow.State/ErrorMessage. NetClient.Connect-Aufruf (Token/CharacterId) bleibt wie in M2c, nur der Aufrufer ändert sich.
- Lumiis.Tests.csproj: Compile-Include für LoginFlow.cs und IAuthClient.cs (analog zum bestehenden Compile-Include von ClientWorld), damit sie per dotnet test ohne Unity geprüft werden.
- Tests (xUnit, mit einer FakeAuthClient-Implementierung): ungültiger Username/Passwort wird abgelehnt, OHNE dass IAuthClient aufgerufen wird; erfolgreiche Registrierung/Login führt zum erwarteten Zustand; 401/409/Netzwerkfehler/jeder JoinRejectReason-Wert führen zum erwarteten Fehlertext und Zustand; Charakter wählen/erstellen und der Übergang zu Connecting/InGame.

## 2. Unity PlayMode-Tests
- Neues Assembly client/Assets/Lumiis/Tests/PlayMode/ mit eigener .asmdef (Referenzen: UnityEngine.TestRunner, UnityEditor.TestRunner als testOnly, die Haupt-Client-Assembly), Test-Framework-Paket prüfen/ergänzen falls nötig.
- LoginController bekommt einen Weg, im Test eine andere IAuthClient-Instanz zu bekommen als im normalen Spielbetrieb (z. B. optionaler Konstruktor-/Setup-Parameter), damit PlayMode-Tests ohne laufendes Auth/Postgres auskommen.
- Mindestens diese [UnityTest]-Coroutine-Tests: (a) nach dem Start ist username-field sichtbar (resolvedStyle.display, Width/Height > 0) - das hätte den rootVisualElement-Bug gefangen; (b) zu jedem Zeitpunkt ist höchstens eine von Login-Wurzel/Charakterwahl-Wurzel sichtbar - das hätte den Duplicate-Roots-Bug gefangen; (c) Klick auf Registrieren ohne Eingabe zeigt den Validierungstext, kein NullReferenceException in der Konsole (LogAssert.NoUnexpectedReceived o. ä.); (d) erfolgreicher Login (Fake liefert Erfolg) wechselt sichtbar zur Charakterwahl.
- Beweise, dass die Tests wirklich scheitern können: baue vorübergehend einen der drei vorher gefundenen Bugs absichtlich wieder ein (z. B. SetActive-Reihenfolge vertauschen), zeig, dass der passende Test rot wird, mach die Änderung rückgängig, zeig grün. Das gehört in den Bericht, nicht in einen Commit.
- tools/test-client-playmode.sh (analog zu tools/build-client.sh): ruft Unity im Batchmode mit -runTests -testPlatform PlayMode auf, gibt den Exit-Code weiter. Verifiziere den tatsächlichen Exit-Code bei einem absichtlich roten Testlauf (nicht davon ausgehen).

## 3. Doku
CLAUDE.md: neuer Abschnitt zur Teststrategie (wann dotnet test reicht, wann tools/test-client-playmode.sh nötig ist), Befehl dokumentieren, Konvention "Neue Login-/UI-Logik zuerst in LoginFlow (testbar), UI-Verdrahtung bleibt dünn" ergänzen.

## Fertig, wenn
- dotnet build && dotnet test grün (5x stabil), inklusive der neuen LoginFlow-Tests.
- tools/test-client-playmode.sh läuft durch, alle PlayMode-Tests grün, UND du hast im Bericht gezeigt, dass mindestens ein Test bei absichtlich wiedereingebautem Bug tatsächlich rot wird.
- Ein Commit pro Abschnitt.
