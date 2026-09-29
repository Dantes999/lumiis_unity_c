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

// Nur eine Karte "village" in M3a (siehe docs/tasks/M3a-collision.md) - Mehrkarten-Unterstützung ist
// bewusst kein Ziel hier, der Pfad ist deshalb noch hartkodiert. GameConstants.MapWidth/Height sind in
// 1/100-Einheiten, collision.bin zählt in Zellen (1 Zelle = 1 Weltenheit) - daher /100.
string collisionMapPath = Path.Combine(config.DataDir, "maps", "village", "collision.bin");
CollisionMap collisionMap;
try
{
    collisionMap = CollisionMap.LoadFromFile(collisionMapPath, GameConstants.MapWidth / 100, GameConstants.MapHeight / 100);
}
catch (Exception ex)
{
    logger.Error($"Serverstart fehlgeschlagen (Kollisionskarte): {ex.Message}");
    return 1;
}

// Ebenfalls nur "village" hartkodiert (siehe Kommentar oben). SpawnCatalog braucht MobCatalog+CollisionMap
// zur Validierung (jede MobId muss existieren, jede Position begehbar sein).
MobCatalog mobCatalog;
SpawnCatalog spawnCatalog;
try
{
    mobCatalog = MobCatalog.LoadFromFile(Path.Combine(config.DataDir, "mobs.json"));
    spawnCatalog = SpawnCatalog.LoadFromFile(Path.Combine(config.DataDir, "maps", "village", "spawns.json"), mobCatalog, collisionMap);
}
catch (Exception ex)
{
    logger.Error($"Serverstart fehlgeschlagen (Mob-/Spawn-Daten): {ex.Message}");
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

// Ebenfalls nur "village" hartkodiert (siehe Kommentar oben). NpcSpawnCatalog braucht NpcCatalog+
// CollisionMap zur Validierung (analog zu SpawnCatalog für Monster). looks.json wird hier erneut geladen
// (bewusst eine eigene, kleine Instanz statt Wiederverwendung der obigen - günstig, hält die Datenblöcke
// unabhängig voneinander, siehe Item-Daten-Block).
NpcCatalog npcCatalog;
NpcSpawnCatalog npcSpawnCatalog;
try
{
    LookCatalog npcLookCatalog = LookCatalog.LoadFromFile(Path.Combine(config.DataDir, "looks.json"));
    npcCatalog = NpcCatalog.LoadFromFile(Path.Combine(config.DataDir, "npcs.json"), itemCatalog, npcLookCatalog);
    npcSpawnCatalog = NpcSpawnCatalog.LoadFromFile(Path.Combine(config.DataDir, "maps", "village", "npcs.json"), npcCatalog, collisionMap);
}
catch (Exception ex)
{
    logger.Error($"Serverstart fehlgeschlagen (NPC-Daten): {ex.Message}");
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

using CancellationTokenSource cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cts.Cancel();
};

GameServer server = new GameServer(config, logger, tokenService, dbContextFactory, collisionMap: collisionMap, mobCatalog: mobCatalog, spawnCatalog: spawnCatalog, classCatalog: classCatalog, itemCatalog: itemCatalog, npcCatalog: npcCatalog, npcSpawnCatalog: npcSpawnCatalog, progressionCatalog: progressionCatalog, skillCatalog: skillCatalog);

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
    if (File.Exists(Path.Combine("data", "maps", "village", "collision.bin"))) return "data";

    string fromServerDir = Path.Combine("..", "data");
    if (File.Exists(Path.Combine(fromServerDir, "maps", "village", "collision.bin"))) return fromServerDir;

    return "data"; // Existiert keins von beiden, soll die Fehlermeldung von CollisionMap.LoadFromFile den erwarteten Pfad nennen.
}
