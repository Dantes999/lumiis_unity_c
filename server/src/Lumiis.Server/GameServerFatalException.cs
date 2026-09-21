using System;

namespace Lumiis.Server;

/// <summary>Wird geworfen, wenn zu viele Ticks in Folge fehlgeschlagen sind - signalisiert Program.cs, sauber mit Exit-Code 1 herunterzufahren.</summary>
public sealed class GameServerFatalException : Exception
{
    public GameServerFatalException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
