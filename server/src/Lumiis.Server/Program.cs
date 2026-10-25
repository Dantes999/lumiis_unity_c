using System.Threading;
using Lumiis.Data;
using Lumiis.Data.Classes;
using Lumiis.Data.Items;
using Lumiis.Data.Looks;
using Lumiis.Data.Maps;
using Lumiis.Data.Mobs;
using Lumiis.Data.Npcs;
using Lumiis.Data.Progression;
using Lumiis.Data.Skills;
using Lumiis.Data.Upgrade;
using Lumiis.Server;
using Lumiis.Server.Logging;
using Lumiis.Server.Net;
using Lumiis.Shared;
using Microsoft.EntityFrameworkCore;

ILogger logger = new ConsoleLogger();
ServerConfig config = new ServerConfig();

string? explicitDataDir = null;

for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--port":
            config.Port = int.Parse(args[++i]);
            break;
        case "--max-players":
            config.MaxPlayers = int.Parse(args[++i]);
            break;
        case "--data-dir":
            explicitDataDir = args[++i];
            break;
        case "--allow-debug-commands":
            config.AllowDebugCommands = true;
            break;
        default:
            logger.Warn($"Unbekanntes Argument: {args[i]}");
            break;
    }
}

config.DataDir = explicitDataDir ?? ResolveDefaultDataDir();

// Multi-Map (docs/map-import-plan.md): mobs.json zuerst (jede Karten-Spawn-Position verweist auf eine
// MobId), die Karten selbst lädt gleich MapCatalog aus data/maps/index.json - inklusive collision.bin,
// spawns.json, npcs.json, portals.json und optional map.json je Kartenordner.
MobCatalog mobCatalog;
try
{
    mobCatalog = MobCatalog.LoadFromFile(Path.Combine(config.DataDir, "mobs.json"));
}
catch (Exception ex)
{
    logger.Error($"Serverstart fehlgeschlagen (Mob-Daten): {ex.Message}");
    return 1;
}

ClassCatalog classCatalog;
try
{
    classCatalog = ClassCatalog.LoadFromFile(Path.Combine(config.DataDir, "classes.json"));
}
catch (Exception ex)
{
    logger.Error($"Serverstart fehlgeschlagen (Klassen-Daten): {ex.Message}");
    return 1;
}

// M6a: XP-Tabelle/Levelunterschieds-Prozente, siehe CLAUDE.md Abschnitt "Fortschritt: Level, XP, Werte".
ProgressionCatalog progressionCatalog;
try
{
    progressionCatalog = ProgressionCatalog.LoadFromFile(Path.Combine(config.DataDir, "progression.json"));
}
catch (Exception ex)
{
    logger.Error($"Serverstart fehlgeschlagen (Fortschritts-Daten): {ex.Message}");
    return 1;
}

// M6b: vier Skills mit Stufen 1-17, siehe CLAUDE.md Abschnitt "Skills" - keine Querverweise gegen andere
// Kataloge nötig (anders als items.json/looks.json), reine Validierung in sich (17 Stufen, positive Werte).
SkillCatalog skillCatalog;
try
{
    skillCatalog = SkillCatalog.LoadFromFile(Path.Combine(config.DataDir, "skills.json"));
}
catch (Exception ex)
{
    logger.Error($"Serverstart fehlgeschlagen (Skill-Daten): {ex.Message}");
    return 1;
}

// looks.json wird nur zur Validierung von items.json gebraucht (jedes Ausrüstungs-Item braucht eine
// existierende LookId im passenden Slot, siehe ItemCatalog.LoadFromJson) - der Server selbst löst Looks
// seit M2b nicht mehr aus einem Katalog auf (Appearance kommt aus der DB-Zeile bzw. seit M5a zusätzlich
// über ItemCatalog aus den EquippedXItemId-Spalten, siehe GameServer.ToAppearance).
ItemCatalog itemCatalog;
try
{
    LookCatalog lookCatalog = LookCatalog.LoadFromFile(Path.Combine(config.DataDir, "looks.json"));
    itemCatalog = ItemCatalog.LoadFromFile(Path.Combine(config.DataDir, "items.json"), lookCatalog);
}
catch (Exception ex)
{
    logger.Error($"Serverstart fehlgeschlagen (Item-Daten): {ex.Message}");
    return 1;
}

// M6c: Erfolgstabelle + Verbrauchsgüter-IDs für die Ausrüstungs-Aufwertung - referenziert items.json
// (Schleifstein/Bewahrungsrolle müssen existieren und den richtigen ItemType haben), deshalb erst nach
// dem ItemCatalog.
UpgradeCatalog upgradeCatalog;
try
{
    upgradeCatalog = UpgradeCatalog.LoadFromFile(Path.Combine(config.DataDir, "upgrade.json"), itemCatalog);
}
catch (Exception ex)
{
    logger.Error($"Serverstart fehlgeschlagen (Aufwertungs-Daten): {ex.Message}");
    return 1;
}

// NPC-Typen (Sortimente) global; die Platzierungen je Karte kommen aus MapCatalog (maps/<id>/npcs.json).
// looks.json wird hier erneut geladen (bewusst eine eigene, kleine Instanz statt Wiederverwendung der obigen -
// günstig, hält die Datenblöcke unabhängig voneinander, siehe Item-Daten-Block).
NpcCatalog npcCatalog;
try
{
    LookCatalog npcLookCatalog = LookCatalog.LoadFromFile(Path.Combine(config.DataDir, "looks.json"));
    npcCatalog = NpcCatalog.LoadFromFile(Path.Combine(config.DataDir, "npcs.json"), itemCatalog, npcLookCatalog);
}
catch (Exception ex)
{
    logger.Error($"Serverstart fehlgeschlagen (NPC-Daten): {ex.Message}");
    return 1;
}

// Multi-Map: index.json + alle Kartenordner, fail-fast validiert (duplizierte IDs, blockierte Start-/
// Portal-/Spawn-Positionen, unbekannte Portalziele, Ankunft in einem Portal der Zielkarte).
MapCatalog mapCatalog;
try
{
    mapCatalog = MapCatalog.LoadFromDirectory(Path.Combine(config.DataDir, "maps"), mobCatalog, npcCatalog);
}
catch (Exception ex)
{
    logger.Error($"Serverstart fehlgeschlagen (Karten-Daten): {ex.Message}");
    return 1;
}

// M6d: Lumiis-Stein-Wellen. Die MobIds werden gegen mobCatalog geprueft, damit ein Tippfehler in
// metins.json den Serverstart abbricht statt still keine Wellen zu erzeugen.
MetinCatalog metinCatalog;
try
{
    metinCatalog = MetinCatalog.LoadFromFile(Path.Combine(config.DataDir, "metins.json"));
    metinCatalog.ValidateAgainst(mobCatalog);
}
catch (Exception ex)
{
    logger.Error($"Serverstart fehlgeschlagen (Lumiis-Stein-Daten): {ex.Message}");
    return 1;
}

// Dieselben Variablen, dieselbe Fail-Fast-Regel wie Lumiis.Auth (siehe CLAUDE.md): fehlt eine, bricht der
// Start mit klarer Meldung ab. Keine eigene Migration hier (bleibt Sache von Auth in Development).
string connectionString;
string? tokenSecret = Environment.GetEnvironmentVariable("LUMIIS_TOKEN_SECRET");
TokenService tokenService;
try
{
    connectionString = DbConnectionOptions.BuildConnectionString();
    tokenService = new TokenService(tokenSecret ?? throw new InvalidOperationException("LUMIIS_TOKEN_SECRET ist nicht gesetzt. Siehe .env.example."));
}
catch (Exception ex)
{
    logger.Error($"Serverstart fehlgeschlagen (Umgebungsvariablen): {ex.Message}");
    return 1;
}

DbContextOptions<LumiisDbContext> dbOptions = new DbContextOptionsBuilder<LumiisDbContext>()
    .UseNpgsql(connectionString)
    .Options;
Func<LumiisDbContext> dbContextFactory = () => new LumiisDbContext(dbOptions);

// DB vorwärmen: der erste EF-Core-Zugriff baut das Modell auf und öffnet die erste Npgsql-Verbindung - das
// kann mehrere Sekunden dauern und traf sonst den ersten Join nach dem Serverstart. Fehler hier brechen den
// Start nicht ab (Joins melden DB-Probleme ohnehin einzeln), sie werden nur gewarnt.
try
{
    var warmupWatch = System.Diagnostics.Stopwatch.StartNew();
    using LumiisDbContext warmupDb = dbContextFactory();
    _ = warmupDb.Characters.AsNoTracking().Include(c => c.InventoryItems).Include(c => c.SkillLevels)
        .Where(c => c.Id == -1).ToList();
    logger.Info($"Datenbank vorgewärmt ({warmupWatch.ElapsedMilliseconds} ms).");
}
catch (Exception ex)
{
    logger.Warn($"Datenbank-Vorwärmen fehlgeschlagen (Server startet trotzdem): {ex.Message}");
}

using CancellationTokenSource cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cts.Cancel();
};

GameServer server = new GameServer(config, logger, tokenService, dbContextFactory, mobCatalog: mobCatalog, classCatalog: classCatalog, itemCatalog: itemCatalog, npcCatalog: npcCatalog, progressionCatalog: progressionCatalog, skillCatalog: skillCatalog, mapCatalog: mapCatalog, upgradeCatalog: upgradeCatalog, metinCatalog: metinCatalog, useGroundDrops: true, requireBlacksmithForUpgrade: true);

try
{
    server.Start();
}
catch (Exception ex)
{
    logger.Error($"Serverstart fehlgeschlagen: {ex}");
    return 1;
}

try
{
    server.Run(cts.Token);
}
catch (Exception ex)
{
    logger.Error($"Server wird wegen eines unerwarteten Fehlers beendet: {ex}");
    server.Stop();
    return 1;
}

server.Stop();
return 0;

// Lumiis.Server ist eine Konsolen-App ohne launchSettings.json (anders als Lumiis.Auth, siehe dessen
// Program.cs-Kommentar) - "dotnet run --project ..." ändert das Arbeitsverzeichnis hier NICHT auf den
// Projektordner, es bleibt das des Aufrufers. Trotzdem zwei plausible Aufrufer-Verzeichnisse: Repo-Root
// (CLAUDE.md-Konvention "dotnet run --project server/src/Lumiis.Server") oder server/ (wenn jemand vorher
// dorthin wechselt und "dotnet run --project src/Lumiis.Server" tippt) - beide werden hier geprüft, statt
// nur den Repo-Root-Fall stillschweigend anzunehmen.
static string ResolveDefaultDataDir()
{
    if (File.Exists(Path.Combine("data", "maps", "index.json"))) return "data";

    string fromServerDir = Path.Combine("..", "data");
    if (File.Exists(Path.Combine(fromServerDir, "maps", "index.json"))) return fromServerDir;

    return "data"; // Existiert keins von beiden, soll die Fehlermeldung von MapCatalog den erwarteten Pfad nennen.
}
