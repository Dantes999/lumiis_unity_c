using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Lumiis.Data;

/// <summary>Design-Time-Factory für `dotnet ef migrations add/database update`, aufgerufen direkt gegen
/// Lumiis.Data (Lumiis.Auth konfiguriert den DbContext zur Laufzeit selbst per DI). Nutzt bewusst
/// dieselbe Quelle wie die Laufzeit (DbConnectionOptions.BuildConnectionString()), OHNE eigenen
/// Fallback-Wert: `dotnet ef database update` verbindet sich wirklich mit Postgres (anders als
/// `migrations add`, das rein Code generiert) - ein abweichender Default hier hätte sonst mit falschen
/// Zugangsdaten verbunden, statt den echten Konfigurationsfehler zu melden. Vorher immer
/// "source tools/dev-env.sh" ausführen (siehe CLAUDE.md).</summary>
public sealed class LumiisDbContextFactory : IDesignTimeDbContextFactory<LumiisDbContext>
{
    public LumiisDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<LumiisDbContext>();
        optionsBuilder.UseNpgsql(DbConnectionOptions.BuildConnectionString());
        return new LumiisDbContext(optionsBuilder.Options);
    }
}
