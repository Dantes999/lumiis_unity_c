using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Lumiis.Tests.Auth;

/// <summary>Logik-Tests gegen EF-Core-InMemory (siehe AuthWebApplicationFactory), kein Beweis für echtes
/// Postgres-Verhalten.</summary>
public class RegisterEndpointTests : IClassFixture<AuthWebApplicationFactory>
{
    private readonly HttpClient _client;

    public RegisterEndpointTests(AuthWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Register_ValidData_Returns201WithAccountId()
    {
        HttpResponseMessage response = await _client.PostAsJsonAsync("/register", new { username = "register_ok", password = "correct horse battery" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        JsonElement body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.GetProperty("accountId").GetInt32() > 0);
    }

    [Fact]
    public async Task Register_DuplicateUsername_Returns409()
    {
        await _client.PostAsJsonAsync("/register", new { username = "register_dup", password = "correct horse battery" });

        HttpResponseMessage response = await _client.PostAsJsonAsync("/register", new { username = "register_dup", password = "another password" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Theory]
    [InlineData("ab", "correct horse battery")] // Username zu kurz (UsernameRules: min. 3)
    [InlineData("register_invalid1", "short")] // Passwort zu kurz (PasswordHasher: min. 8)
    [InlineData("register invalid", "correct horse battery")] // Leerzeichen im Username
    public async Task Register_InvalidData_Returns400(string username, string password)
    {
        HttpResponseMessage response = await _client.PostAsJsonAsync("/register", new { username, password });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
