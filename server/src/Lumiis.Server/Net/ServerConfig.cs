using Lumiis.Shared;

namespace Lumiis.Server.Net;

public sealed class ServerConfig
{
    public int Port = 7777;
    public int MaxPlayers = 100;
    public string ConnectionKey = Protocol.ConnectionKey;

    /// <summary>Basispfad für Spieldaten (aktuell nur data/maps/&lt;map&gt;/collision.bin, siehe
    /// CLAUDE.md "Kartenkollision"), relativ zum Arbeitsverzeichnis des Prozesses.</summary>
    public string DataDir = "data";

    /// <summary>Verbindungen ohne gültiges JoinRequest werden nach dieser Wandzeit getrennt, in ms (Default 5 s).</summary>
    public int JoinTimeoutMs = 5000;

    /// <summary>Sobald ein JoinRequest mit gültigem Token angenommen wurde, läuft der Charakter-Lookup asynchron
    /// gegen die DB. Die Frist wird dann auf diesen Wert (ab Annahme) verlängert, damit ein langsamer erster
    /// DB-Zugriff (EF-Core-Kaltstart, Verbindungsaufbau) den Join nicht abbricht (Default 30 s).</summary>
    public int JoinLookupTimeoutMs = 30000;

    /// <summary>Frist nach JoinRejected, bevor die Verbindung getrennt wird, damit das Paket noch ankommt, in ms (Default 1 s).</summary>
    public int RejectGraceMs = 1000;

    /// <summary>Default aus - schaltet DebugSpawnMonsterRequestPacket frei (siehe CLAUDE.md "Dev-Tools").
    /// Nur für die lokale Entwicklung gedacht, nie in einem "echten" Serverstart aktivieren.</summary>
    public bool AllowDebugCommands = false;

    /// <summary>Optional (--start-map &lt;id&gt;, nur lokale Tests): jeder Join landet auf dieser Karte an deren
    /// Startposition, unabhängig von der gespeicherten MapId/Position (z. B. die Testwiese). null = normal.</summary>
    public string? ForcedStartMapId = null;
}
