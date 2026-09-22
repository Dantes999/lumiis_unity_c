using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Lumiis.Data.Mobs;

/// <summary>Ein Mob-Typ aus data/mobs.json (Platzhalterdaten, siehe CLAUDE.md Abschnitt "Monster").</summary>
public sealed class MobDefinition
{
    public int Id { get; }
    public string Name { get; }
    public int SpeedWire { get; }
    public int WanderRadiusWire { get; }
    public int WanderIntervalMinMs { get; }
    public int WanderIntervalMaxMs { get; }

    public MobDefinition(int id, string name, int speedWire, int wanderRadiusWire, int wanderIntervalMinMs, int wanderIntervalMaxMs)
    {
        Id = id;
        Name = name;
        SpeedWire = speedWire;
        WanderRadiusWire = wanderRadiusWire;
        WanderIntervalMinMs = wanderIntervalMinMs;
        WanderIntervalMaxMs = wanderIntervalMaxMs;
    }
}

/// <summary>Lädt und validiert data/mobs.json. Fehler beim Laden bedeuten Abbruch beim Serverstart (analog zu
/// LookCatalog).</summary>
public sealed class MobCatalog
{
    private readonly Dictionary<int, MobDefinition> _byId;

    private MobCatalog(Dictionary<int, MobDefinition> byId)
    {
        _byId = byId;
    }

    public static MobCatalog LoadFromFile(string path)
    {
        string json;
        try
        {
            json = File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new MobCatalogException($"mobs.json konnte nicht gelesen werden ({path}): {ex.Message}");
        }

        return LoadFromJson(json);
    }

    public static MobCatalog LoadFromJson(string json)
    {
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        MobCatalogDto? data;
        try
        {
            data = JsonSerializer.Deserialize<MobCatalogDto>(json, options);
        }
        catch (JsonException ex)
        {
            throw new MobCatalogException($"mobs.json ist kein gültiges JSON: {ex.Message}");
        }

        if (data == null)
            throw new MobCatalogException("mobs.json ist leer.");

        var byId = new Dictionary<int, MobDefinition>();
        foreach (MobEntryDto entry in data.Mobs)
        {
            if (byId.ContainsKey(entry.Id))
                throw new MobCatalogException($"mobs.json: doppelte Id {entry.Id}.");

            if (string.IsNullOrWhiteSpace(entry.Name))
                throw new MobCatalogException($"mobs.json: Mob {entry.Id} ohne Name.");

            if (entry.SpeedWire <= 0)
                throw new MobCatalogException($"mobs.json: Mob {entry.Id} ({entry.Name}) hat speedWire <= 0.");

            if (entry.WanderRadiusWire <= 0)
                throw new MobCatalogException($"mobs.json: Mob {entry.Id} ({entry.Name}) hat wanderRadiusWire <= 0.");

            if (entry.WanderIntervalMinMs <= 0 || entry.WanderIntervalMaxMs < entry.WanderIntervalMinMs)
            {
                throw new MobCatalogException(
                    $"mobs.json: Mob {entry.Id} ({entry.Name}) hat ein ungültiges Wander-Intervall (min {entry.WanderIntervalMinMs}, max {entry.WanderIntervalMaxMs}).");
            }

            byId[entry.Id] = new MobDefinition(entry.Id, entry.Name, entry.SpeedWire, entry.WanderRadiusWire, entry.WanderIntervalMinMs, entry.WanderIntervalMaxMs);
        }

        if (byId.Count == 0)
            throw new MobCatalogException("mobs.json: mindestens ein Mob-Typ wird benötigt.");

        return new MobCatalog(byId);
    }

    public bool TryGet(int mobId, out MobDefinition definition)
    {
        return _byId.TryGetValue(mobId, out definition!);
    }
}

public sealed class MobCatalogException : Exception
{
    public MobCatalogException(string message) : base(message)
    {
    }
}
