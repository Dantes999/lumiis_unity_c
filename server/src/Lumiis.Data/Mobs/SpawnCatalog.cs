using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Lumiis.Data.Maps;

namespace Lumiis.Data.Mobs;

/// <summary>Ein Spawn-Punkt aus data/maps/&lt;map&gt;/spawns.json. RespawnSeconds ist im Schema vorbereitet,
/// wird in M3c bewusst nicht ausgewertet (siehe docs/tasks/M3c-monsters.md).</summary>
public sealed class SpawnPoint
{
    public int Id { get; }
    public int MobId { get; }
    public int PositionX { get; }
    public int PositionY { get; }
    public int RespawnSeconds { get; }

    /// <summary>Monster-Gruppe (0 = keine), siehe SpawnPointEntry.GroupId.</summary>
    public int GroupId { get; }

    public SpawnPoint(int id, int mobId, int positionX, int positionY, int respawnSeconds, int groupId = 0)
    {
        Id = id;
        MobId = mobId;
        PositionX = positionX;
        PositionY = positionY;
        RespawnSeconds = respawnSeconds;
        GroupId = groupId;
    }
}

/// <summary>Lädt und validiert spawns.json: jede MobId muss in der übergebenen MobCatalog existieren, jede
/// Position muss laut der übergebenen CollisionMap begehbar sein - beides beim Start geprüft, Startabbruch mit
/// Angabe des betroffenen Spawn-Punkts bei Verstoß (analog zu MobCatalog/CollisionMap selbst).</summary>
public sealed class SpawnCatalog
{
    public IReadOnlyList<SpawnPoint> Spawns { get; }

    private SpawnCatalog(List<SpawnPoint> spawns)
    {
        Spawns = spawns;
    }

    public static SpawnCatalog LoadFromFile(string path, MobCatalog mobCatalog, CollisionMap collisionMap)
    {
        string json;
        try
        {
            json = File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new SpawnCatalogException($"spawns.json konnte nicht gelesen werden ({path}): {ex.Message}");
        }

        return LoadFromJson(json, mobCatalog, collisionMap);
    }

    public static SpawnCatalog LoadFromJson(string json, MobCatalog mobCatalog, CollisionMap collisionMap)
    {
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        SpawnCatalogDto? data;
        try
        {
            data = JsonSerializer.Deserialize<SpawnCatalogDto>(json, options);
        }
        catch (JsonException ex)
        {
            throw new SpawnCatalogException($"spawns.json ist kein gültiges JSON: {ex.Message}");
        }

        if (data == null)
            throw new SpawnCatalogException("spawns.json ist leer.");

        var spawns = new List<SpawnPoint>();
        var seenIds = new HashSet<int>();

        foreach (SpawnEntryDto entry in data.Spawns)
        {
            if (!seenIds.Add(entry.Id))
                throw new SpawnCatalogException($"spawns.json: doppelte Id {entry.Id}.");

            if (!mobCatalog.TryGet(entry.MobId, out _))
                throw new SpawnCatalogException($"spawns.json: Spawn-Punkt {entry.Id} verweist auf unbekannte MobId {entry.MobId}.");

            int cellX = entry.PositionX / 100;
            int cellY = entry.PositionY / 100;
            if (collisionMap.IsBlocked(cellX, cellY))
            {
                throw new SpawnCatalogException(
                    $"spawns.json: Spawn-Punkt {entry.Id} (MobId {entry.MobId}) liegt auf einer blockierten Zelle ({cellX},{cellY}, Position {entry.PositionX},{entry.PositionY}).");
            }

            spawns.Add(new SpawnPoint(entry.Id, entry.MobId, entry.PositionX, entry.PositionY, entry.RespawnSeconds, Math.Max(0, entry.GroupId)));
        }

        return new SpawnCatalog(spawns);
    }
}

public sealed class SpawnCatalogException : Exception
{
    public SpawnCatalogException(string message) : base(message)
    {
    }
}
