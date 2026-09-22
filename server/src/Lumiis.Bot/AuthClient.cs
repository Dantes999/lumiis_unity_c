using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;

namespace Lumiis.Bot;

/// <summary>HTTP-Client für Lumiis.Auth: meldet einen Bot an (Login-oder-Registrieren) und sorgt dafür, dass
/// er mindestens einen Charakter hat. Reines Test-Tool - das Passwort ist absichtlich fest und im Code
/// sichtbar, kein echtes Geheimnis.</summary>
public sealed class AuthClient
{
    private readonly HttpClient _http;

    public AuthClient(string authUrl)
    {
        _http = new HttpClient { BaseAddress = new Uri(authUrl.TrimEnd('/') + "/") };
    }

    /// <summary>Register (bei 409 stattdessen Login), dann sicherstellen, dass ein Charakter existiert
    /// (erster aus GET /characters, sonst POST /characters mit dem Bot-Namen). Gibt Token und CharacterId
    /// zurück, direkt für JoinRequestPacket nutzbar.</summary>
    public async Task<(string Token, int CharacterId)> LoginOrRegisterAndEnsureCharacterAsync(string name)
    {
        string password = $"botpass-{name}-2026"; // Nur Test-Tool, kein echtes Geheimnis.

        string token = await RegisterOrLoginAsync(name, password);
        int characterId = await EnsureCharacterAsync(token, name);
        return (token, characterId);
    }

    private async Task<string> RegisterOrLoginAsync(string name, string password)
    {
        HttpResponseMessage registerResponse = await _http.PostAsJsonAsync("register", new { username = name, password });
        if (registerResponse.StatusCode == HttpStatusCode.Created)
        {
            JsonElement body = await registerResponse.Content.ReadFromJsonAsync<JsonElement>();
            Console.WriteLine($"[{name}] registriert (accountId={body.GetProperty("accountId").GetInt32()})");
        }
        else if (registerResponse.StatusCode == HttpStatusCode.Conflict)
        {
            Console.WriteLine($"[{name}] Benutzername bereits vergeben, melde stattdessen an");
        }
        else
        {
            throw new InvalidOperationException($"POST /register für {name} fehlgeschlagen: {(int)registerResponse.StatusCode}");
        }

        HttpResponseMessage loginResponse = await _http.PostAsJsonAsync("login", new { username = name, password });
        if (!loginResponse.IsSuccessStatusCode)
            throw new InvalidOperationException($"POST /login für {name} fehlgeschlagen: {(int)loginResponse.StatusCode}");

        JsonElement loginBody = await loginResponse.Content.ReadFromJsonAsync<JsonElement>();
        Console.WriteLine($"[{name}] angemeldet");
        return loginBody.GetProperty("token").GetString()!;
    }

    private async Task<int> EnsureCharacterAsync(string token, string name)
    {
        HttpResponseMessage getResponse = await SendAuthorizedAsync(HttpMethod.Get, "characters", token, content: null);
        if (!getResponse.IsSuccessStatusCode)
            throw new InvalidOperationException($"GET /characters für {name} fehlgeschlagen: {(int)getResponse.StatusCode}");

        JsonElement body = await getResponse.Content.ReadFromJsonAsync<JsonElement>();
        JsonElement characters = body.GetProperty("characters"); // GET /characters: { "characters": [...] } statt rohem Array (siehe M2c)
        if (characters.GetArrayLength() > 0)
        {
            int existingId = characters[0].GetProperty("id").GetInt32();
            Console.WriteLine($"[{name}] Charakter gefunden (id={existingId})");
            return existingId;
        }

        HttpResponseMessage createResponse = await SendAuthorizedAsync(HttpMethod.Post, "characters", token, JsonContent.Create(new { name }));
        if (!createResponse.IsSuccessStatusCode)
            throw new InvalidOperationException($"POST /characters für {name} fehlgeschlagen: {(int)createResponse.StatusCode}");

        JsonElement created = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        int createdId = created.GetProperty("id").GetInt32();
        Console.WriteLine($"[{name}] Charakter erstellt (id={createdId})");
        return createdId;
    }

    private async Task<HttpResponseMessage> SendAuthorizedAsync(HttpMethod method, string path, string token, HttpContent? content)
    {
        var request = new HttpRequestMessage(method, path) { Content = content };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await _http.SendAsync(request);
    }
}
