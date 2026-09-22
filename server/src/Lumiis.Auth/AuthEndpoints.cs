using Lumiis.Data;
using Lumiis.Data.Looks;
using Lumiis.Shared;
using Lumiis.Shared.Net;
using Microsoft.EntityFrameworkCore;

namespace Lumiis.Auth;

public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this WebApplication app)
    {
        app.MapPost("/register", Register);
        app.MapPost("/login", Login);
        app.MapGet("/characters", GetCharacters);
        app.MapPost("/characters", CreateCharacter);
    }

    private static async Task<IResult> Register(RegisterRequest request, LumiisDbContext db)
    {
        if (!UsernameRules.IsValid(request.Username) || !PasswordHasher.IsValidPassword(request.Password))
            return Results.Json(new ErrorResponse("Ungültiger Benutzername oder Passwort."), statusCode: StatusCodes.Status400BadRequest);

        bool taken = await db.Accounts.AnyAsync(a => a.Username == request.Username);
        if (taken)
            return Results.Json(new ErrorResponse("Benutzername bereits vergeben."), statusCode: StatusCodes.Status409Conflict);

        (byte[] hash, byte[] salt, int iterations) = PasswordHasher.Hash(request.Password!);
        var account = new Account
        {
            Username = request.Username!,
            PasswordHash = hash,
            PasswordSalt = salt,
            PasswordIterations = iterations,
            CreatedAtUtc = DateTime.UtcNow
        };
        db.Accounts.Add(account);

        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // Deckt die Race Condition zwischen dem AnyAsync-Check oben und diesem Insert ab (doppelter
            // Username bei echtem Postgres dank Unique-Index) - der EF-Core-InMemory-Provider in den Tests
            // kennt keine Unique-Constraints, dieser Zweig läuft dort nicht.
            return Results.Json(new ErrorResponse("Benutzername bereits vergeben."), statusCode: StatusCodes.Status409Conflict);
        }

        return Results.Json(new RegisterResponse(account.Id), statusCode: StatusCodes.Status201Created);
    }

    private static async Task<IResult> Login(LoginRequest request, LumiisDbContext db, TokenService tokenService)
    {
        Account? account = string.IsNullOrEmpty(request.Username)
            ? null
            : await db.Accounts.SingleOrDefaultAsync(a => a.Username == request.Username);

        // Keine Unterscheidung "Name unbekannt" vs. "Passwort falsch" in der Antwort.
        if (account == null || string.IsNullOrEmpty(request.Password)
            || !PasswordHasher.Verify(request.Password, account.PasswordHash, account.PasswordSalt, account.PasswordIterations))
        {
            return Results.Json(new ErrorResponse("Ungültiger Benutzername oder Passwort."), statusCode: StatusCodes.Status401Unauthorized);
        }

        (string token, long expiresAtUnix) = tokenService.IssueWithExpiry(account.Id);
        List<CharacterSummary> characters = await db.Characters
            .Where(c => c.AccountId == account.Id)
            .Select(c => new CharacterSummary(c.Id, c.Name))
            .ToListAsync();

        return Results.Json(new LoginResponse(token, expiresAtUnix, characters));
    }

    private static async Task<IResult> GetCharacters(HttpContext context, LumiisDbContext db, TokenService tokenService)
    {
        if (!TryAuthenticate(context, tokenService, out int accountId))
            return Unauthorized();

        List<CharacterSummary> characters = await db.Characters
            .Where(c => c.AccountId == accountId)
            .Select(c => new CharacterSummary(c.Id, c.Name))
            .ToListAsync();

        return Results.Json(characters);
    }

    private static async Task<IResult> CreateCharacter(
        HttpContext context, CreateCharacterRequest request, LumiisDbContext db, TokenService tokenService, LookCatalog lookCatalog)
    {
        if (!TryAuthenticate(context, tokenService, out int accountId))
            return Unauthorized();

        if (request.Name == null || !NameRules.IsValid(request.Name))
            return Results.Json(new ErrorResponse("Ungültiger Charaktername."), statusCode: StatusCodes.Status400BadRequest);

        bool taken = await db.Characters.AnyAsync(c => c.Name == request.Name);
        if (taken)
            return Results.Json(new ErrorResponse("Charaktername bereits vergeben."), statusCode: StatusCodes.Status409Conflict);

        Appearance appearance = lookCatalog.CreateDefaultAppearance();
        var character = new Character
        {
            AccountId = accountId,
            Name = request.Name,
            BodyId = appearance.BodyId,
            HairStyleId = appearance.HairStyleId,
            HairColorId = appearance.HairColorId,
            ArmorId = appearance.ArmorId,
            HelmetId = appearance.HelmetId,
            WeaponId = appearance.WeaponId,
            ShieldId = appearance.ShieldId,
            CreatedAtUtc = DateTime.UtcNow
        };
        db.Characters.Add(character);

        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException) // siehe Kommentar in Register() - Race Condition bei echtem Postgres.
        {
            return Results.Json(new ErrorResponse("Charaktername bereits vergeben."), statusCode: StatusCodes.Status409Conflict);
        }

        return Results.Json(new CharacterSummary(character.Id, character.Name), statusCode: StatusCodes.Status201Created);
    }

    private static IResult Unauthorized()
    {
        return Results.Json(new ErrorResponse("Nicht angemeldet oder Token ungültig/abgelaufen."), statusCode: StatusCodes.Status401Unauthorized);
    }

    /// <summary>Header "Authorization: Bearer &lt;token&gt;" - eigenes, minimales Schema (siehe TokenService),
    /// kein Standard-JWT-Middleware nötig.</summary>
    private static bool TryAuthenticate(HttpContext context, TokenService tokenService, out int accountId)
    {
        accountId = 0;
        string? header = context.Request.Headers.Authorization;
        const string prefix = "Bearer ";
        if (string.IsNullOrEmpty(header) || !header.StartsWith(prefix, StringComparison.Ordinal))
            return false;

        return tokenService.TryValidate(header[prefix.Length..], out accountId);
    }
}
