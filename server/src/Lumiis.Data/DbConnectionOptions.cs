using System;
using Npgsql;

namespace Lumiis.Data;

/// <summary>Baut den Postgres-Verbindungsstring aus Umgebungsvariablen (siehe .env.example). Getrennt von
/// LumiisDbContext, damit Tests (EF-Core-InMemory) nie versehentlich hierüber laufen.</summary>
public static class DbConnectionOptions
{
    private const string DefaultHost = "localhost";
    private const string DefaultPort = "5432";

    public static string BuildConnectionString()
    {
        string host = Environment.GetEnvironmentVariable("LUMIIS_DB_HOST") ?? DefaultHost;
        string portText = Environment.GetEnvironmentVariable("LUMIIS_DB_PORT") ?? DefaultPort;
        string? name = Environment.GetEnvironmentVariable("LUMIIS_DB_NAME");
        string? user = Environment.GetEnvironmentVariable("LUMIIS_DB_USER");
        string? password = Environment.GetEnvironmentVariable("LUMIIS_DB_PASSWORD");

        if (!int.TryParse(portText, out int port))
            throw new InvalidOperationException($"LUMIIS_DB_PORT ist keine gültige Zahl: '{portText}'.");
        if (string.IsNullOrEmpty(name))
            throw new InvalidOperationException("LUMIIS_DB_NAME ist nicht gesetzt. Siehe .env.example.");
        if (string.IsNullOrEmpty(user))
            throw new InvalidOperationException("LUMIIS_DB_USER ist nicht gesetzt. Siehe .env.example.");
        if (string.IsNullOrEmpty(password))
            throw new InvalidOperationException("LUMIIS_DB_PASSWORD ist nicht gesetzt. Siehe .env.example.");

        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = host,
            Port = port,
            Database = name,
            Username = user,
            Password = password
        };
        return builder.ConnectionString;
    }
}
