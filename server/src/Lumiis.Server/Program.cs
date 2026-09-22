using System.Threading;
using Lumiis.Data;
using Lumiis.Server;
using Lumiis.Server.Logging;
using Lumiis.Server.Net;
using Microsoft.EntityFrameworkCore;

ILogger logger = new ConsoleLogger();
ServerConfig config = new ServerConfig();

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
        default:
            logger.Warn($"Unbekanntes Argument: {args[i]}");
            break;
    }
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

GameServer server = new GameServer(config, logger, tokenService, dbContextFactory);

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
