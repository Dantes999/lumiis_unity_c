using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Lumiis.Data;

/// <summary>Design-Time-Factory für `dotnet ef migrations add/database update`, aufgerufen direkt gegen
/// Lumiis.Data (Lumiis.Auth konfiguriert den DbContext zur Laufzeit selbst per DI). EF verbindet für
/// migrations/Codegenerierung nicht wirklich - echte Werte kommen zur Laufzeit aus der Umgebung.</summary>
public sealed class LumiisDbContextFactory : IDesignTimeDbContextFactory<LumiisDbContext>
{
    public LumiisDbContext CreateDbContext(string[] args)
    {
        string connectionString;
        try
        {
            connectionString = DbConnectionOptions.BuildConnectionString();
        }
        catch (System.InvalidOperationException)
        {
            connectionString = "Host=localhost;Port=5432;Database=lumiis;Username=lumiis;Password=lumiis";
        }

        var optionsBuilder = new DbContextOptionsBuilder<LumiisDbContext>();
        optionsBuilder.UseNpgsql(connectionString);
        return new LumiisDbContext(optionsBuilder.Options);
    }
}
