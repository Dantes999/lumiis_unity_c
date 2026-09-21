using Lumiis.Shared;

namespace Lumiis.Server.Net;

public sealed class ServerConfig
{
    public int Port = 7777;
    public int MaxPlayers = 100;
    public string ConnectionKey = Protocol.ConnectionKey;

    /// <summary>Verbindungen ohne gültiges JoinRequest werden nach dieser Wandzeit getrennt, in ms (Default 5 s).</summary>
    public int JoinTimeoutMs = 5000;

    /// <summary>Frist nach JoinRejected, bevor die Verbindung getrennt wird, damit das Paket noch ankommt, in ms (Default 1 s).</summary>
    public int RejectGraceMs = 1000;
}
