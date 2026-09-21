using System.Threading;
using Lumiis.Server;
using Lumiis.Server.Logging;
using Lumiis.Server.Net;

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

using CancellationTokenSource cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cts.Cancel();
};

GameServer server = new GameServer(config, logger);

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
