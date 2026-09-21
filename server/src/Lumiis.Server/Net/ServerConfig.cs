using Lumiis.Shared;

namespace Lumiis.Server.Net;

public sealed class ServerConfig
{
    public int Port = 7777;
    public int MaxPlayers = 100;
    public string ConnectionKey = "lumiis";

    /// <summary>Verbindungen ohne gültiges JoinRequest werden nach dieser Zahl Ticks getrennt (Default 5 s).</summary>
    public int JoinTimeoutTicks = Protocol.TickRate * 5;

    /// <summary>Frist nach JoinRejected, bevor die Verbindung getrennt wird, damit das Paket noch ankommt (Default 1 s).</summary>
    public int RejectGraceTicks = Protocol.TickRate;
}
