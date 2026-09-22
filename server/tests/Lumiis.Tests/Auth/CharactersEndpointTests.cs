using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Lumiis.Tests.Auth;

/// <summary>Logik-Tests gegen EF-Core-InMemory (siehe AuthWebApplicationFactory), kein Beweis für echtes
/// Postgres-Verhalten - insbesondere prüft der InMemory-Provider keine Unique-Constraints, die
/// DbUpdateException-Absicherung in AuthEndpoints.CreateCharacter läuft hier nicht.</summary>
public class CharactersEndpointTests : IClassFixture<AuthWebApplicationFactory>
{
    private readonly HttpClient _client;

    public CharactersEndpointTests(AuthWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    private async Task<string> RegisterAndLoginAsync(string username)
    {
        await _client.PostAsJsonAsync("/register", new { username, password = "correct horse battery" });
        HttpResponseMessage loginResponse = await _client.PostAsJsonAsync("/login", new { username, password = "correct horse battery" });
        JsonElement body = await loginResponse.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("token").GetString()!;
    }

    private async Task<HttpResponseMessage> GetCharactersAsync(string? token)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/characters");
        if (token != null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await _client.SendAsync(request);
    }

    private async Task<HttpResponseMessage> CreateCharacterAsync(string? token, string name)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/characters") { Content = JsonContent.Create(new { name }) };
        if (token != null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await _client.SendAsync(request);
    }

    [Fact]
    public async Task GetCharacters_WithoutToken_Returns401()
    {
        HttpResponseMessage response = await GetCharactersAsync(token: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetCharacters_ResponseIsObjectWithCharactersProperty_NotRawArray()
    {
        // Unitys JsonUtility kann Top-Level-JSON-Arrays nicht parsen (siehe M2c) - die Antwort muss ein
        // Objekt mit einer "characters"-Eigenschaft sein, kein rohes Array.
        string token = await RegisterAndLoginAsync("chars_contract");

        HttpResponseMessage response = await GetCharactersAsync(token);

        JsonElement body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(JsonValueKind.Object, body.ValueKind);
        Assert.True(body.TryGetProperty("characters", out JsonElement characters));
        Assert.Equal(JsonValueKind.Array, characters.ValueKind);
    }

    [Fact]
    public async Task GetCharacters_EmptyInitially_Returns200WithEmptyArray()
    {
        string token = await RegisterAndLoginAsync("chars_empty");

        HttpResponseMessage response = await GetCharactersAsync(token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        JsonElement body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0, body.GetProperty("characters").GetArrayLength());
    }

    [Fact]
    public async Task PostCharacters_WithoutToken_Returns401()
    {
        HttpResponseMessage response = await CreateCharacterAsync(token: null, name: "Egal");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PostCharacters_ValidName_Returns201AndThenAppearsInGet()
    {
        string token = await RegisterAndLoginAsync("chars_create");

        HttpResponseMessage createResponse = await CreateCharacterAsync(token, "TestHeld");

        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        JsonElement created = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("TestHeld", created.GetProperty("name").GetString());
        Assert.True(created.GetProperty("id").GetInt32() > 0);

        HttpResponseMessage getResponse = await GetCharactersAsync(token);
        JsonElement getBody = await getResponse.Content.ReadFromJsonAsync<JsonElement>();
        JsonElement characters = getBody.GetProperty("characters");
        Assert.Equal(1, characters.GetArrayLength());
        Assert.Equal("TestHeld", characters[0].GetProperty("name").GetString());
    }

    [Fact]
    public async Task PostCharacters_AssignsAppearanceFromLookCatalog()
    {
        // TestLookCatalogs.CreateMinimal() hat genau 1 Body/Armor/Helmet/Weapon/Shield/Hair-Eintrag (Id 1)
        // und 2 Haarfarben - CreateDefaultAppearance() muss also überall 1 zuweisen.
        string token = await RegisterAndLoginAsync("chars_appearance");

        HttpResponseMessage createResponse = await CreateCharacterAsync(token, "Ausgestattet");
        JsonElement created = await createResponse.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        Assert.True(created.GetProperty("id").GetInt32() > 0);
        // Appearance selbst ist nicht Teil der Response (nur id/name, siehe CharacterSummary) - die
        // eigentliche Zuweisung ist bereits in LookCatalogTests.CreateDefaultAppearance_* abgedeckt.
    }

    [Fact]
    public async Task PostCharacters_DuplicateName_Returns409()
    {
        string tokenA = await RegisterAndLoginAsync("chars_dup_a");
        string tokenB = await RegisterAndLoginAsync("chars_dup_b");
        (await CreateCharacterAsync(tokenA, "GleicherName")).EnsureSuccessStatusCode();

        HttpResponseMessage response = await CreateCharacterAsync(tokenB, "GleicherName");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task PostCharacters_DuplicateNameDifferentCase_Returns409()
    {
        // Global eindeutig UND case-insensitiv - wie bisher bei der In-Memory-Namensvergabe des Servers.
        string tokenA = await RegisterAndLoginAsync("chars_dup_case_a");
        string tokenB = await RegisterAndLoginAsync("chars_dup_case_b");
        (await CreateCharacterAsync(tokenA, "MixedCase")).EnsureSuccessStatusCode();

        HttpResponseMessage response = await CreateCharacterAsync(tokenB, "mixedcase");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task PostCharacters_InvalidName_Returns400()
    {
        string token = await RegisterAndLoginAsync("chars_invalid_name");

        HttpResponseMessage response = await CreateCharacterAsync(token, "a"); // zu kurz (NameRules: min. 3)

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
