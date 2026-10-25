using Lumiis.Auth;
using Lumiis.Data;
using Lumiis.Data.Items;
using Lumiis.Data.Looks;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<LumiisDbContext>(options => options.UseNpgsql(DbConnectionOptions.BuildConnectionString()));

builder.Services.AddSingleton(_ =>
{
    string? secret = Environment.GetEnvironmentVariable("LUMIIS_TOKEN_SECRET");
    if (string.IsNullOrEmpty(secret))
        throw new InvalidOperationException("LUMIIS_TOKEN_SECRET ist nicht gesetzt. Siehe .env.example.");
    return new TokenService(secret);
});

builder.Services.AddSingleton(_ =>
{
    string dataDir = builder.Configuration["data-dir"] ?? ResolveDefaultDataDir();
    return LookCatalog.LoadFromFile(Path.Combine(dataDir, "looks.json"));
});

// Nur für die Charakterliste: ausgerüstete Item-IDs -> LookIds (siehe CharacterAppearance).
builder.Services.AddSingleton(sp =>
{
    string dataDir = builder.Configuration["data-dir"] ?? ResolveDefaultDataDir();
    return ItemCatalog.LoadFromFile(Path.Combine(dataDir, "items.json"), sp.GetRequiredService<LookCatalog>());
});

// `dotnet run --project server/src/Lumiis.Auth` (siehe CLAUDE.md) setzt bei Web-SDK-Projekten
// (Properties/launchSettings.json vorhanden) das Arbeitsverzeichnis des gestarteten Prozesses auf den
// Projektordner statt auf das Verzeichnis des Aufrufers - anders als bei den Konsolen-Apps
// Lumiis.Server/Lumiis.Bot (kein launchSettings.json, dort bleibt das Arbeitsverzeichnis das des
// Aufrufers). launchSettings.json's "workingDirectory" behebt das nicht: das wird nur von IDE-
// Debug-Launches (Rider/VS) ausgewertet, nicht von der bloßen `dotnet run`-CLI. Deshalb hier robust
// gegen beide Fälle: zuerst wie gewohnt relativ zum Aufrufer (Repo-Root), sonst relativ zum
// Projektordner drei Ebenen hoch zum Repo-Root.
static string ResolveDefaultDataDir()
{
    if (File.Exists(Path.Combine("data", "looks.json"))) return "data";

    string fromProjectDir = Path.Combine("..", "..", "..", "data");
    if (File.Exists(Path.Combine(fromProjectDir, "looks.json"))) return fromProjectDir;

    return "data"; // Existiert keins von beiden, soll die Fehlermeldung von LoadFromFile den erwarteten Pfad nennen.
}

var app = builder.Build();

// Keine Stacktraces nach außen, auch nicht in Development (deshalb explizit vor der automatischen
// Developer Exception Page konfiguriert, die ASP.NET Core sonst in Development einhängen würde).
app.UseExceptionHandler(errorApp => errorApp.Run(async context =>
{
    context.Response.ContentType = "application/json";
    context.Response.StatusCode = StatusCodes.Status500InternalServerError;
    await context.Response.WriteAsJsonAsync(new ErrorResponse("Interner Fehler."));
}));

if (app.Environment.IsDevelopment())
{
    using IServiceScope scope = app.Services.CreateScope();
    scope.ServiceProvider.GetRequiredService<LumiisDbContext>().Database.Migrate();
}
else
{
    app.Logger.LogWarning("ASPNETCORE_ENVIRONMENT ist nicht 'Development' - Migrationen werden NICHT automatisch angewendet. "
        + "Vor dem Start manuell ausführen: dotnet ef database update (im Ordner server/src/Lumiis.Auth).");
}

app.MapAuthEndpoints();

app.Run();

/// <summary>Stub für WebApplicationFactory&lt;Program&gt; (Lumiis.Tests) - bei Top-Level-Statements ist die
/// generierte Program-Klasse sonst internal und für andere Assemblies nicht sichtbar.</summary>
public partial class Program
{
}
