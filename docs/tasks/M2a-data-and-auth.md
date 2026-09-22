# M2a – Datenbank und Auth-Dienst (Registrierung, Login, Charakterliste)

Ziel: Account- und Charakterverwaltung als eigener Dienst, per curl testbar. Der laufende Spielbetrieb (Server, Bot, Client, Protokoll) wird NICHT verändert. Alle bisherigen 133 Tests bleiben grün. Kein Zugriff auf Unity in dieser Aufgabe.

## Kontext für später (jetzt nur beachten, nicht umsetzen)
M2b baut die Server-Seite: JoinRequestPacket bekommt Token und CharacterId (statt Name), der Server validiert das Token lokal per Signatur (kein Netzwerk-Aufruf zu Auth), lädt den Charakter, prüft Eigentümerschaft (CharacterId gehört zu AccountId), verhindert doppeltes Einloggen desselben Charakters, speichert die Position periodisch und beim Disconnect. Baue deshalb JETZT: das Token trägt NUR die AccountId (keine CharacterId), damit ein Account zwischen Charakteren wählen kann, ohne sich neu einzuloggen.

## 1. Docker Compose und Lumiis.Data (Projekt-Grundgerüst)
- docker-compose.yml im Repo-Root: ein Service "postgres" (Image postgres:16), Port 5432, benanntes Volume, Umgebungsvariablen POSTGRES_USER/POSTGRES_PASSWORD/POSTGRES_DB aus einer .env-Datei (Vorlage .env.example committen, .env selbst gitignoren).
- dotnet new classlib -n Lumiis.Data -o server/src/Lumiis.Data -f net10.0, zur Solution hinzufügen, Referenz auf Lumiis.Shared (für NameRules und den Appearance-Typ, NICHT umgekehrt). NuGet: Npgsql.EntityFrameworkCore.PostgreSQL, Microsoft.EntityFrameworkCore.Design.
- Entities: Account (Id int PK, Username string eindeutig, PasswordHash byte[], PasswordSalt byte[], PasswordIterations int, CreatedAtUtc), Character (Id int PK, AccountId FK, Name string, BodyId/HairStyleId/HairColorId/ArmorId/HelmetId/WeaponId/ShieldId int, PositionX int?, PositionY int?, CreatedAtUtc). Username-Regeln: wiederverwende NameRules aus Shared oder erstelle ein eigenes, ähnlich strenges Pendant (ASCII, 3-32 Zeichen), mit Tests.
- LumiisDbContext, Verbindungsstring aus Umgebungsvariablen (LUMIIS_DB_HOST Default localhost, LUMIIS_DB_PORT Default 5432, LUMIIS_DB_NAME, LUMIIS_DB_USER, LUMIIS_DB_PASSWORD ohne Default, Startabbruch mit klarer Meldung, wenn PASSWORD fehlt).
- EF-Core-Tool lokal reproduzierbar einrichten (dotnet new tool-manifest, dotnet tool install dotnet-ef), erste Migration erzeugen.

## 2. Passwort-Hashing (in Lumiis.Data)
- PBKDF2-HMAC-SHA256 über Rfc2898DeriveBytes.Pbkdf2 (kein NuGet-Paket nötig), 600.000 Iterationen (aktuelle OWASP-Mindestempfehlung), 16 Byte kryptografisch zufälliges Salt pro Nutzer, 32 Byte Ausgabe. Iterationszahl wird pro Account-Zeile gespeichert (spätere Erhöhung ohne Breaking Change). Konstantzeit-Vergleich beim Verifizieren.
- Passwort-Mindestlänge 8 Zeichen, keine weiteren Komplexitätsregeln (aktuelle Empfehlungen bevorzugen Länge vor Komplexitätstheater).
- Tests: Hash und Verify Round-trip, falsches Passwort schlägt fehl, zwei Hashes desselben Passworts unterscheiden sich (Salt wirkt), Konstantzeit-Vergleich mit Werten unterschiedlicher Länge sicher.

## 3. Session-Token (in Lumiis.Data)
- Format: base64url(JSON: {"aid": accountId, "exp": unixSeconds}) + "." + base64url(HMACSHA256-Signatur über den ersten Teil, UTF8). Signier-Geheimnis aus Umgebungsvariable LUMIIS_TOKEN_SECRET (mind. 32 Byte, KEIN Default, Startabbruch mit klarer Meldung, wenn sie fehlt oder zu kurz ist). Gültigkeit 30 Minuten (Konstante, benannt).
- TokenService: Issue(accountId) -> string, TryValidate(string) -> (bool ok, int accountId), lehnt manipulierte Signatur, abgelaufenes Token und Kauderwelsch ab, wirft nie.
- Tests: Round-trip, manipulierte Signatur, abgelaufenes Token (mit injizierbarer Uhr wie beim Server), kaputte/leere Eingabe.

## 4. LookCatalog-Umzug
- LookCatalog (Laden und Validieren von looks.json) von Lumiis.Server nach Lumiis.Data verschieben. Lumiis.Server referenziert es von dort weiter, Verhalten unverändert, bestehende Server-Tests bleiben grün.

## 5. Lumiis.Auth (ASP.NET Core Minimal API, eigener Prozess)
- dotnet new webapi -n Lumiis.Auth -o server/src/Lumiis.Auth (Minimal API, keine Controller), zur Solution hinzufügen, Referenzen auf Lumiis.Data und Lumiis.Shared. Lokal HTTP (kein HTTPS-Redirect), Port konfigurierbar (Default 5080).
- In Development beim Start automatisch db.Database.Migrate() (nur wenn ASPNETCORE_ENVIRONMENT=Development), sonst nur loggen, dass Migrationen ausstehen.
- POST /register { username, password } -> 201 { accountId } | 409 bei vergebenem Namen | 400 bei ungültigem Namen/Passwort.
- POST /login { username, password } -> 200 { token, expiresAtUnix, characters: [{ id, name }] } | 401 bei falschen Daten (keine Unterscheidung "Name unbekannt" vs. "Passwort falsch" in der Antwort).
- GET /characters, Header "Authorization: Bearer <token>" -> 200 [{ id, name }] | 401 bei ungültigem/abgelaufenem Token.
- POST /characters, Header "Authorization: Bearer <token>", Body { name } -> 201 { id, name } | 401 | 400 bei ungültigem Namen | 409 bei doppeltem Charakternamen. Aussehen wird automatisch aus dem LookCatalog vergeben (z. B. erster gültiger Eintrag je Slot), keine Auswahl in M2a.
- Fehlerantworten als knappes JSON { error: "..." }, keine Stacktraces nach außen.

## 6. Tests
- Business-Logik (Registrierung, Login, Eindeutigkeit, Charaktererstellung) über Microsoft.AspNetCore.Mvc.Testing (WebApplicationFactory) mit EF-Core-InMemory-Provider, damit dotnet test ohne laufendes Postgres durchläuft. Kennzeichne diese Tests klar als Logik-Tests gegen InMemory, nicht als Beweis für echtes Postgres-Verhalten.
- Lumiis.Tests.csproj bekommt Referenzen auf Lumiis.Data und Lumiis.Auth.
- Alle bisherigen 133 Tests bleiben unverändert grün.

## 7. Doku
- CLAUDE.md: neue Projekte Lumiis.Data/Lumiis.Auth im Architekturteil, Befehle (docker compose up -d, dotnet ef database update, dotnet run --project server/src/Lumiis.Auth), Pflicht-Umgebungsvariablen (LUMIIS_DB_PASSWORD, LUMIIS_TOKEN_SECRET, mit .env.example), "Bekannte Lücken" ergänzen: kein Passwort-Reset/E-Mail-Verifizierung, Auth läuft nur über HTTP (kein TLS) bis zu einem echten Deploy. Status/Aktuelle Aufgabe aktualisieren, M2 in M2a/M2b/M2c aufgeteilt.

## Fertig, wenn
- cd server && dotnet build && dotnet test grün (5x stabil), OHNE dass Postgres laufen muss.
- docker compose up -d startet Postgres, dotnet ef database update (im Auth-Projektordner) wendet die Migration an.
- Mit laufendem Auth-Dienst (dotnet run --project server/src/Lumiis.Auth) funktionieren Register, Login (liefert Token), GET/POST /characters mit dem Token manuell per curl.
- Server, Bot und Unity-Client funktionieren unverändert wie vor M2a (nichts an Protokoll oder Spielbetrieb geändert).
- Ein Commit pro Abschnitt.
