namespace Lumiis.Auth;

// System.Text.Json serialisiert diese Records in ASP.NET Core standardmäßig als camelCase
// (accountId, token, expiresAtUnix, characters, id, name, error) - genau die auf der Leitung
// erwarteten Feldnamen, ohne eigene JsonPropertyName-Attribute.

public sealed record RegisterRequest(string? Username, string? Password);

public sealed record RegisterResponse(int AccountId);

public sealed record LoginRequest(string? Username, string? Password);

public sealed record LoginResponse(string Token, long ExpiresAtUnix, IReadOnlyList<CharacterSummary> Characters);

public sealed record CharacterSummary(int Id, string Name);

/// <summary>GET /characters: Top-Level-JSON-Arrays kann Unitys JsonUtility nicht parsen, deshalb
/// eingepackt statt eines rohen Arrays.</summary>
public sealed record CharacterListResponse(IReadOnlyList<CharacterSummary> Characters);

public sealed record CreateCharacterRequest(string? Name);

public sealed record ErrorResponse(string Error);
