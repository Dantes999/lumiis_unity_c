using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Lumiis.Shared.Net;

namespace Lumiis.Data.Parties;

/// <summary>Bonus einer Gruppenrolle in Prozent auf den bereits ausrüstungs-/buffverstärkten Wert.</summary>
public readonly record struct PartyRoleDefinition(PartyRole Role, string Name, int AttackPercent, int DefensePercent);

/// <summary>Gruppenregeln aus data/party.json (Protocol.Version 20): Größe, XP-Anteil, wie lange ein offline
/// gegangenes Mitglied in der Gruppe bleibt und die Boni je Rolle. Fail-fast validiert wie die übrigen
/// Kataloge.</summary>
public sealed class PartyCatalog
{
    private readonly Dictionary<PartyRole, PartyRoleDefinition> _roles;

    private PartyCatalog(int maxMembers, int xpSharePercent, long offlineRemoveAfterMs, Dictionary<PartyRole, PartyRoleDefinition> roles)
    {
        MaxMembers = maxMembers;
        XpSharePercent = xpSharePercent;
        OfflineRemoveAfterMs = offlineRemoveAfterMs;
        _roles = roles;
    }

    public int MaxMembers { get; }
    public int XpSharePercent { get; }
    public long OfflineRemoveAfterMs { get; }
    public IReadOnlyCollection<PartyRoleDefinition> Roles => _roles.Values;

    /// <summary>Default ohne party.json (bestehende Tests): bisheriges Verhalten (8 Mitglieder, 50 % XP),
    /// 10 Minuten Offline-Frist, keine Rollen-Boni.</summary>
    public static PartyCatalog Default { get; } = new(8, 50, 600_000, new Dictionary<PartyRole, PartyRoleDefinition>());

    public bool TryGetRole(PartyRole role, out PartyRoleDefinition definition) => _roles.TryGetValue(role, out definition);

    public static PartyCatalog LoadFromFile(string path)
    {
        string json;
        try
        {
            json = File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new PartyCatalogException($"party.json konnte nicht gelesen werden ({path}): {ex.Message}");
        }

        return LoadFromJson(json);
    }

    public static PartyCatalog LoadFromJson(string json)
    {
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        PartyCatalogDto? data;
        try
        {
            data = JsonSerializer.Deserialize<PartyCatalogDto>(json, options);
        }
        catch (JsonException ex)
        {
            throw new PartyCatalogException($"party.json ist kein gültiges JSON: {ex.Message}");
        }

        if (data == null)
            throw new PartyCatalogException("party.json ist leer.");
        if (data.MaxMembers < 2 || data.MaxMembers > byte.MaxValue)
            throw new PartyCatalogException($"party.json: maxMembers {data.MaxMembers} muss zwischen 2 und 255 liegen.");
        if (data.XpSharePercent < 0 || data.XpSharePercent > 100)
            throw new PartyCatalogException($"party.json: xpSharePercent {data.XpSharePercent} liegt außerhalb von 0-100.");
        if (data.OfflineRemoveAfterSeconds < 0)
            throw new PartyCatalogException($"party.json: offlineRemoveAfterSeconds {data.OfflineRemoveAfterSeconds} darf nicht negativ sein.");

        var roles = new Dictionary<PartyRole, PartyRoleDefinition>();
        foreach (PartyRoleDto role in data.Roles)
        {
            if (!Enum.TryParse(role.Role, ignoreCase: false, out PartyRole parsed) || parsed == PartyRole.None
                || !Enum.IsDefined(typeof(PartyRole), parsed))
                throw new PartyCatalogException($"party.json: unbekannte Rolle '{role.Role}'.");
            if (roles.ContainsKey(parsed))
                throw new PartyCatalogException($"party.json: Rolle {parsed} ist doppelt definiert.");
            if (string.IsNullOrWhiteSpace(role.Name))
                throw new PartyCatalogException($"party.json: Rolle {parsed} braucht einen Namen.");
            if (role.AttackPercent < 0 || role.AttackPercent > 100 || role.DefensePercent < 0 || role.DefensePercent > 100)
                throw new PartyCatalogException($"party.json: Boni der Rolle {parsed} müssen zwischen 0 und 100 liegen.");
            roles[parsed] = new PartyRoleDefinition(parsed, role.Name, role.AttackPercent, role.DefensePercent);
        }

        return new PartyCatalog(data.MaxMembers, data.XpSharePercent, data.OfflineRemoveAfterSeconds * 1000L, roles);
    }
}

public sealed class PartyCatalogException : Exception
{
    public PartyCatalogException(string message) : base(message)
    {
    }
}

internal sealed class PartyCatalogDto
{
    public int MaxMembers { get; set; }
    public int XpSharePercent { get; set; }
    public int OfflineRemoveAfterSeconds { get; set; }
    public PartyRoleDto[] Roles { get; set; } = Array.Empty<PartyRoleDto>();
}

internal sealed class PartyRoleDto
{
    public string Role { get; set; } = "";
    public string Name { get; set; } = "";
    public int AttackPercent { get; set; }
    public int DefensePercent { get; set; }
}
