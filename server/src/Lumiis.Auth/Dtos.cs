namespace Lumiis.Auth;

// System.Text.Json serialisiert diese Records in ASP.NET Core standardmäßig als camelCase
// (accountId, token, expiresAtUnix, characters, id, name, error) - genau die auf der Leitung
// erwarteten Feldnamen, ohne eigene JsonPropertyName-Attribute.

public sealed record RegisterRequest(string? Username, string? Password);

public sealed record RegisterResponse(int AccountId);

public sealed record LoginRequest(string? Username, string? Password);

public sealed record LoginResponse(string Token, long ExpiresAtUnix, IReadOnlyList<CharacterSummary> Characters);

/// <summary>Eintrag der Charakterliste. Seit dem Charakterauswahl-Vorschau-Fix zusätzlich Level/Karte und das
/// aufgelöste Aussehen (LookIds, wie EntitySpawn/JoinAccepted) plus die ausgerüsteten Item-IDs/Waffenstufe,
/// damit der Client die Vorschau mit denselben Sprites (spriteKey der Waffe) zeichnen kann wie im Spiel.</summary>
public sealed record CharacterSummary(int Id, string Name, int Level, string MapId, AppearanceDto Appearance, EquipmentDto Equipment);

/// <summary>LookIds je Slot - gleiche Bedeutung wie Lumiis.Shared.Net.Appearance auf der Leitung.</summary>
public sealed record AppearanceDto(int BodyId, int HairStyleId, int HairColorId, int ArmorId, int HelmetId, int WeaponId, int ShieldId);

/// <summary>Ausgerüstete Item-IDs (0 = leer) und Aufwertungsstufen (0-9).</summary>
public sealed record EquipmentDto(int ArmorItemId, int HelmetItemId, int WeaponItemId, int ShieldItemId,
    int ArmorUpgradeLevel, int HelmetUpgradeLevel, int WeaponUpgradeLevel, int ShieldUpgradeLevel);

/// <summary>GET /characters: Top-Level-JSON-Arrays kann Unitys JsonUtility nicht parsen, deshalb
/// eingepackt statt eines rohen Arrays.</summary>
public sealed record CharacterListResponse(IReadOnlyList<CharacterSummary> Characters);

/// <summary>BodyId/HairStyleId/HairColorId optional (null = erster gültiger Eintrag aus looks.json, wie bisher).
/// Werden gegen looks.json validiert (HairColorId 1..Anzahl hairColors), sonst 400.</summary>
public sealed record CreateCharacterRequest(string? Name, int? BodyId = null, int? HairStyleId = null, int? HairColorId = null);

public sealed record ErrorResponse(string Error);
