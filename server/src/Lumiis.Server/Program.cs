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
server.Start();
try
{
    server.Run(cts.Token);
}
finally
{
    server.Stop();
}
