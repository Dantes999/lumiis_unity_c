using System.Linq;
using Lumiis.Data;
using Lumiis.Data.Items;
using Lumiis.Data.Looks;
using Lumiis.Tests;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Lumiis.Tests.Auth;

/// <summary>Startet Lumiis.Auth in-process (kein echter Socket) für Endpunkt-Tests. Ersetzt Postgres durch
/// EF-Core-InMemory, den echten Datenkatalog durch TestLookCatalogs.CreateMinimal() und das echte
/// LUMIIS_TOKEN_SECRET durch ein festes Test-Secret - unabhängig von der Umgebung, in der dotnet test läuft.
///
/// WICHTIG: Das sind Logik-Tests gegen den InMemory-Provider, KEIN Beweis für echtes Postgres-Verhalten.
/// Der InMemory-Provider prüft z. B. keine Unique-Constraints (siehe AuthEndpoints.Register/CreateCharacter -
/// die dortige DbUpdateException-Absicherung gegen doppelte Namen unter echtem Postgres wird hier nicht
/// durchlaufen, nur der vorgelagerte AnyAsync-Check).</summary>
// AuthEntryPointMarker statt Program: Lumiis.Server hat [InternalsVisibleTo("Lumiis.Tests")] (siehe
// AssemblyInfo.cs), dadurch wäre dessen eigene (interne) Program-Klasse hier ebenfalls sichtbar - beide
// heißen im globalen Namensraum "Program" (top-level statements) und sind nicht per using/Namensraum
// unterscheidbar. Siehe AuthEntryPointMarker.cs für Details.
public sealed class AuthWebApplicationFactory : WebApplicationFactory<Lumiis.Auth.AuthEntryPointMarker>
{
    public const string TestTokenSecret = "test-only-token-secret-at-least-32-bytes";

    /// <summary>Startwaffe Id 1 (AuthEndpoints.StartingWeaponItemId) mit LookId 1 aus TestLookCatalogs.</summary>
    public const string TestItemsJson = @"{
  ""items"": [
    { ""id"": 1, ""name"": ""Testschwert"", ""itemType"": ""Weapon"", ""lookId"": 1, ""attackBonus"": 1, ""defenseBonus"": 0 }
  ]
}";

    private readonly string _inMemoryDbName = "lumiis-test-" + Guid.NewGuid();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Nicht "Development" - sonst würde Program.cs beim Start db.Database.Migrate() gegen den
        // InMemory-Provider versuchen (Migrationen sind ein Postgres/Relational-Konzept).
        builder.UseEnvironment("Testing");

        builder.ConfigureServices(services =>
        {
            // services.RemoveAll<DbContextOptions<LumiisDbContext>>() allein reicht nicht: EF Core
            // registriert AddDbContext-Konfiguration über zusätzliche, interne Wrapper-Typen
            // (IDbContextOptionsConfiguration<T>), die mehrere AddDbContext-Aufrufe für denselben
            // Kontext-Typ VERKETTEN statt zu ersetzen - ohne diesen breiteren Sweep würde die echte
            // Npgsql-Konfiguration aus Program.cs (inkl. DbConnectionOptions.BuildConnectionString(),
            // die ohne LUMIIS_DB_* wirft) beim Auflösen weiterhin mitlaufen. Deshalb pauschal alle
            // Deskriptoren entfernen, deren ServiceType LumiisDbContext referenziert.
            foreach (var descriptor in services.Where(d => d.ServiceType.FullName?.Contains(nameof(LumiisDbContext)) == true).ToList())
                services.Remove(descriptor);
            services.AddDbContext<LumiisDbContext>(options => options.UseInMemoryDatabase(_inMemoryDbName));

            services.RemoveAll<TokenService>();
            services.AddSingleton(new TokenService(TestTokenSecret));

            services.RemoveAll<LookCatalog>();
            services.AddSingleton(TestLookCatalogs.CreateMinimal());

            services.RemoveAll<ItemCatalog>();
            services.AddSingleton(ItemCatalog.LoadFromJson(TestItemsJson, TestLookCatalogs.CreateMinimal()));
        });
    }
}
