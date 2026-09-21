using System.Collections.Generic;
using System.Threading;
using Lumiis.Bot;

string host = "127.0.0.1";
int port = 7777;
int count = 1;
string namePrefix = "Bot";
int radius = 20;

for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--host":
            host = args[++i];
            break;
        case "--port":
            port = int.Parse(args[++i]);
            break;
        case "--count":
            count = int.Parse(args[++i]);
            break;
        case "--name":
            namePrefix = args[++i];
            break;
        case "--radius":
            radius = int.Parse(args[++i]);
            break;
        default:
            Console.WriteLine($"Unbekanntes Argument: {args[i]}");
            break;
    }
}

List<BotClient> bots = new List<BotClient>();
for (int i = 1; i <= count; i++)
{
    BotClient bot = new BotClient($"{namePrefix}{i}", radius);
    bot.Connect(host, port);
    bots.Add(bot);
}

using CancellationTokenSource cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cts.Cancel();
};

Console.WriteLine($"{count} Bot(s) verbinden zu {host}:{port} ...");

// Gemeinsamer Poll-Loop im Hauptthread für alle Bots.
while (!cts.Token.IsCancellationRequested)
{
    foreach (BotClient bot in bots)
        bot.Poll();

    Thread.Sleep(15);
}
