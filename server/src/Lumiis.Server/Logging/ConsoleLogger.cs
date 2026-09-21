using System;

namespace Lumiis.Server.Logging;

/// <summary>Kleine eigene Konsolen-Logger-Klasse (Zeitstempel, Level), kein Framework.</summary>
public sealed class ConsoleLogger : ILogger
{
    public void Info(string message) => Write("INFO", message);
    public void Warn(string message) => Write("WARN", message);
    public void Error(string message) => Write("ERROR", message);

    private static void Write(string level, string message)
    {
        Console.WriteLine($"{DateTime.Now:HH:mm:ss.fff} [{level}] {message}");
    }
}
