# M1-Fixes (Ergebnis des Architektur-Reviews)

Kontext: Review von M1. Kein Architekturwechsel, nur Härtung. Ein Commit pro Punkt, nach jedem Punkt dotnet build und dotnet test.

## 1. Exceptions dürfen den Server nie beenden
- Jeder INetEventListener-Callback (OnConnectionRequest, OnPeerConnected, OnNetworkReceive, OnPeerDisconnected, OnNetworkError) fängt Exceptions vollständig ab. Reader.Recycle() im finally.
- Fehler in einem Paket-Handler: mit Stacktrace loggen, nur diese Verbindung hart trennen, Server läuft weiter.
- Zusätzlich ein Try/Catch um einen kompletten Tick in Update()/Run(): loggen und weiterlaufen. Bei 50 aufeinanderfolgenden fehlerhaften Ticks sauber herunterfahren (Fail-fast) mit Exit-Code 1.
- Program.cs: Startfehler (z. B. Port belegt) und unerwartete Exceptions loggen und mit Exit-Code 1 beenden, kein ungefangener Absturz mit Stacktrace-Dump.
- Tests über einen kleinen Testpunkt (z. B. internes Delegate, das in einem Handler oder im World-Tick eine Exception wirft): Der Server läuft weiter, nur die betroffene Verbindung wird getrennt, andere Clients laufen ungestört weiter.

## 2. Fristen in Wandzeit statt in Ticks
- Ticks gelten nur für die Simulation. Join-Timeout und Reject-Frist werden in Millisekunden gemessen: ServerConfig.JoinTimeoutMs (Default 5000) und RejectGraceMs (Default 1000) ersetzen die Tick-Varianten.
- GameServer bekommt eine injizierbare Uhr (Func<long> nowMs, Default: monotone Stopwatch-Zeit). Fristen sind Zeitstempel in ms.
- Unit-Tests der Fristen laufen mit Fake-Uhr (sofort, deterministisch). Integrationstests nutzen die echte Uhr mit kleinen Werten (z. B. RejectGraceMs = 100).
- Die Catch-up-Begrenzung (maxCatchUpTicks = 5, Rückstand verwerfen) bleibt unverändert.
- CLAUDE.md, Konventionen: "Fristen in Wandzeit (ms) mit injizierbarer Uhr, Ticks nur für die Simulation."

## 3. Kleine Härtung
- Protocol.MaxPacketBytes = 1200: Eingehende Pakete, die größer sind, werden vor dem Decode verworfen und die Verbindung getrennt (nicht erst ein großes Array allokieren).
- NameRules: nur ASCII-Buchstaben, Ziffern und Unterstrich, 3-16 Zeichen (verhindert Homoglyph-Namen). Tests anpassen.
- CLAUDE.md, Konventionen: "World und Entities werden nur vom Game-Loop-Thread angefasst. Hintergrundarbeit (z. B. DB) übergibt Ergebnisse per Queue an den Loop."
- CLAUDE.md, neuer Abschnitt "Bekannte Lücken": AOI (M3), Rate-Limit pro Verbindung (M8), Authentifizierung (M2).

## Fertig, wenn
- dotnet build ohne Fehler, dotnet test grün und 5x hintereinander stabil.
- Status in CLAUDE.md aktualisiert.
