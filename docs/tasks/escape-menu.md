# Escape-Menue (Charakter wechseln, Ausloggen, Beenden) + Standalone-Build im Fenstermodus

Ziel: Escape oeffnet/schliesst ein kleines Menue mit drei Aktionen. Charakter wechseln geht zurueck zur Charakterwahl OHNE erneuten Login (Token bleibt), Ausloggen geht zurueck zum Login-Bildschirm (Token verworfen, wie ein unfreiwilliger Disconnect), Beenden schliesst die Anwendung. Zusaetzlich: Standalone-Builds starten im Fenstermodus statt Vollbild. KEINE Server-/Protokolländerung noetig.

## 0. Review zuerst (nicht aendern, nur berichten, Datei:Zeile)
a) Wird die Escape-Taste (KeyCode.Escape bzw. New Input System) aktuell irgendwo abgefragt (Debug-Test-Panel, Shop-Panel schliessen, sonstwo)? Muss das Menue Vorrang haben oder sich damit vertragen?
b) Wie sieht der bestehende Disconnect-Pfad in NetClient/LoginController aus (M1b/M2c): Unterscheidet er bereits zwischen "Verbindung verloren" und "absichtlich getrennt", oder landet jeder Disconnect gleich in LoggedOut? Fuer "Charakter wechseln" brauchen wir einen NEUEN Uebergang: absichtlich trennen, aber Token behalten, State auf CharacterSelect statt LoggedOut, Charakterliste per GET /characters (bestehender Endpunkt, Header Authorization: Bearer token) neu laden.
c) NetClient.Stop() (existiert seit M1b fuer OnDestroy/OnApplicationQuit) - laesst sich das auf Zuruf aufrufen, ohne dass OnDestroy/OnApplicationQuit nochmal denselben Cleanup doppelt ausloesen?
d) Wie ist Login.uxml/CharacterSelect.uxml eingebunden (M2c, Resources/UI, per Code geladen)? Gleicher Mechanismus soll fuer ein neues EscapeMenu.uxml gelten.
e) Welche Eingaben sollen gesperrt sein, waehrend das Menue offen ist (Bewegungsklicks, Angriff, Skills 1-4, Rechtsklick auf NPC/Monster)? Vorschlag: alle Gameplay-Eingaben pausieren, solange das Menue sichtbar ist, ausser Escape selbst zum Schliessen.

## 1. Client-Logik
- LoginController/GameClient: neue Methode SwitchCharacter() - trennt die aktuelle Spielverbindung (NetClient.Stop() bzw. Aequivalent), Token bleibt im Speicher, State auf CharacterSelect, laedt die Charakterliste per GET /characters neu. Bei 401 (Token abgelaufen): stattdessen vollstaendiger Logout-Pfad mit kurzer Meldung ("Sitzung abgelaufen, bitte erneut anmelden").
- Neue Methode Logout() (falls es sowas nicht schon fuer den unfreiwilligen Fall gibt): trennt die Verbindung, verwirft das Token, State auf LoggedOut, Benutzername bleibt vorausgefuellt (bestehendes Verhalten aus M2c).
- Neue Methode QuitGame(): Application.Quit() (im Editor stattdessen UnityEditor.EditorApplication.isPlaying = false, damit Play im Editor sauber stoppt statt den Editor selbst zu beenden).

## 2. Escape-Menue (UI Toolkit, wie Login/CharacterSelect, NICHT im Debug-Panel-Stil)
- EscapeMenu.uxml (client/Assets/Resources/UI/): drei Buttons "Charakter wechseln", "Ausloggen", "Beenden", einfacher Rahmen/Hintergrund, mittig auf dem Bildschirm.
- Nur sichtbar/aktiv waehrend State == InGame. Escape oeffnet es, Escape erneut ODER ein Schliessen-Button (optional) schliesst es wieder.
- Waehrend das Menue offen ist: Gameplay-Eingaben gemaess 0e sperren (kein Bewegungsklick, kein Angriff, keine Skills). Das bestehende Status-Overlay bleibt sichtbar (kein Verstecken der HP/Yang-Anzeige noetig).
- Klick auf die drei Buttons ruft SwitchCharacter()/Logout()/QuitGame() auf, danach Menue automatisch schliessen (bzw. es verschwindet ohnehin mit dem State-Wechsel).
- Keine Bestaetigungsdialoge ("Wirklich beenden?") - bewusst einfach gehalten fuer die Alpha.

## 3. Standalone-Build im Fenstermodus
Player Settings fuer Standalone (ProjectSettings.asset): Default Fullscreen Mode auf "Windowed" (bzw. das tatsaechlich zustaendige YAML-Feld, im Bericht nennen statt zu raten), Default-Aufloesung z. B. 1280x720, Resizable Window aktiviert falls vorhanden. Kein Aendern des Verhaltens im Editor-Play-Modus (der ist ohnehin schon ein Fenster).

## 4. Pruefung
Batchmode-Compile-Check. tools/test-client-playmode.sh: pruefen ob die 4 bestehenden Tests durch das neue Escape-Handling beeinflusst werden (z. B. falls ein Test selbst Escape simuliert oder das Login-Panel prueft, das jetzt einen weiteren Geschwister-UI-Baum hat), bei Bedarf anpassen.

## 5. Doku
CLAUDE.md: kurzer Abschnitt "Escape-Menue" (State-Uebergaenge, dass Charakter-wechseln das Token behaelt, Ausloggen nicht, keine Server-Aenderung noetig), Hinweis auf den Fenstermodus-Default fuer Builds. Kurze manuelle Test-Checkliste ergaenzen.

## Fertig, wenn
- dotnet build und dotnet test gruen (5x stabil, unveraendert - diese Aufgabe aendert keinen Server-/Shared-Code).
- Batchmode-Compile-Check und tools/test-client-playmode.sh ohne Fehler.
- Ein Commit pro Abschnitt.
