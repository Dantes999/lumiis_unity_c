using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Lumiis.Tests.Auth;

/// <summary>Logik-Tests gegen EF-Core-InMemory (siehe AuthWebApplicationFactory), kein Beweis für echtes
/// Postgres-Verhalten.</summary>
public class LoginEndpointTests : IClassFixture<AuthWebApplicationFactory>
{
    private readonly HttpClient _client;

    public LoginEndpointTests(AuthWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    private async Task RegisterAsync(string username, string password)
    {
        HttpResponseMessage response = await _client.PostAsJsonAsync("/register", new { username, password });
        response.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Login_CorrectCredentials_ReturnsTokenAndEmptyCharacterList()
    {
        await RegisterAsync("login_ok", "correct horse battery");

        HttpResponseMessage response = await _client.PostAsJsonAsync("/login", new { username = "login_ok", password = "correct horse battery" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        JsonElement body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(string.IsNullOrEmpty(body.GetProperty("token").GetString()));
        Assert.True(body.GetProperty("expiresAtUnix").GetInt64() > 0);
        Assert.Equal(0, body.GetProperty("characters").GetArrayLength());
    }

    [Fact]
    public async Task Login_WrongPassword_Returns401()
    {
        await RegisterAsync("login_wrongpw", "correct horse battery");

        HttpResponseMessage response = await _client.PostAsJsonAsync("/login", new { username = "login_wrongpw", password = "wrong" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Login_UnknownUsername_ReturnsSameResponseAsWrongPassword()
    {
        await RegisterAsync("login_known", "correct horse battery");

        HttpResponseMessage wrongPasswordResponse = await _client.PostAsJsonAsync("/login", new { username = "login_known", password = "wrong" });
        HttpResponseMessage unknownUserResponse = await _client.PostAsJsonAsync("/login", new { username = "login_does_not_exist", password = "whatever" });

        Assert.Equal(HttpStatusCode.Unauthorized, wrongPasswordResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unknownUserResponse.StatusCode);

        // Keine Unterscheidung "Name unbekannt" vs. "Passwort falsch" in der Antwort.
        string wrongPasswordBody = await wrongPasswordResponse.Content.ReadAsStringAsync();
        string unknownUserBody = await unknownUserResponse.Content.ReadAsStringAsync();
        Assert.Equal(wrongPasswordBody, unknownUserBody);
    }
}
