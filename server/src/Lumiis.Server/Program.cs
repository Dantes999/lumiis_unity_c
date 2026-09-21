using System.IO;
using System.Threading;
using Lumiis.Server;
using Lumiis.Server.Logging;
using Lumiis.Server.Looks;
using Lumiis.Server.Net;

ILogger logger = new ConsoleLogger();
ServerConfig config = new ServerConfig();
string dataDir = "data";

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
            dataDir = args[++i];
            break;
        default:
            logger.Warn($"Unbekanntes Argument: {args[i]}");
            break;
    }
}

LookCatalog lookCatalog;
try
{
    lookCatalog = LookCatalog.LoadFromFile(Path.Combine(dataDir, "looks.json"));
}
catch (Exception ex)
{
    logger.Error($"looks.json konnte nicht geladen werden: {ex.Message}");
    return 1;
}

using CancellationTokenSource cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cts.Cancel();
};

GameServer server = new GameServer(config, logger, lookCatalog);

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
