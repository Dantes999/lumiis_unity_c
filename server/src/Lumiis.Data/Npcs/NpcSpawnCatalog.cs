using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Lumiis.Data.Maps;

namespace Lumiis.Data.Npcs;

/// <summary>Eine NPC-Platzierung aus data/maps/&lt;map&gt;/npcs.json (M5b) - anders als SpawnPoint (Monster)
/// kein RespawnSeconds: NPCs sind statisch, kein Tod, kein Respawn-Scheduling (siehe CLAUDE.md "Shop und
/// NPCs").</summary>
public sealed class NpcPlacement
{
    public int Id { get; }
    public int NpcId { get; }
    public int PositionX { get; }
    public int PositionY { get; }

    public NpcPlacement(int id, int npcId, int positionX, int positionY)
    {
        Id = id;
        NpcId = npcId;
        PositionX = positionX;
        PositionY = positionY;
    }
}

/// <summary>Lädt und validiert npcs.json: jede NpcId muss in der übergebenen NpcCatalog existieren, jede
/// Position muss laut der übergebenen CollisionMap begehbar sein - beides beim Start geprüft, Startabbruch mit
/// Angabe der betroffenen Platzierung bei Verstoß (analog zu SpawnCatalog für Monster).</summary>
public sealed class NpcSpawnCatalog
{
    public IReadOnlyList<NpcPlacement> Placements { get; }

    private NpcSpawnCatalog(List<NpcPlacement> placements)
    {
        Placements = placements;
    }

    public static NpcSpawnCatalog LoadFromFile(string path, NpcCatalog npcCatalog, CollisionMap collisionMap)
    {
        string json;
        try
        {
            json = File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new NpcSpawnCatalogException($"npcs.json (Platzierungen) konnte nicht gelesen werden ({path}): {ex.Message}");
        }

        return LoadFromJson(json, npcCatalog, collisionMap);
    }

    public static NpcSpawnCatalog LoadFromJson(string json, NpcCatalog npcCatalog, CollisionMap collisionMap)
    {
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        NpcSpawnCatalogDto? data;
        try
        {
            data = JsonSerializer.Deserialize<NpcSpawnCatalogDto>(json, options);
        }
        catch (JsonException ex)
        {
            throw new NpcSpawnCatalogException($"npcs.json (Platzierungen) ist kein gültiges JSON: {ex.Message}");
        }

        if (data == null)
            throw new NpcSpawnCatalogException("npcs.json (Platzierungen) ist leer.");

        var placements = new List<NpcPlacement>();
        var seenIds = new HashSet<int>();

        foreach (NpcPlacementEntryDto entry in data.Placements)
        {
            if (!seenIds.Add(entry.Id))
                throw new NpcSpawnCatalogException($"npcs.json (Platzierungen): doppelte Id {entry.Id}.");

            if (!npcCatalog.TryGet(entry.NpcId, out _))
                throw new NpcSpawnCatalogException($"npcs.json (Platzierungen): Platzierung {entry.Id} verweist auf unbekannte NpcId {entry.NpcId}.");

            int cellX = entry.PositionX / 100;
            int cellY = entry.PositionY / 100;
            if (collisionMap.IsBlocked(cellX, cellY))
            {
                throw new NpcSpawnCatalogException(
                    $"npcs.json (Platzierungen): Platzierung {entry.Id} (NpcId {entry.NpcId}) liegt auf einer blockierten Zelle ({cellX},{cellY}, Position {entry.PositionX},{entry.PositionY}).");
            }

            placements.Add(new NpcPlacement(entry.Id, entry.NpcId, entry.PositionX, entry.PositionY));
        }

        return new NpcSpawnCatalog(placements);
    }
}

public sealed class NpcSpawnCatalogException : Exception
{
    public NpcSpawnCatalogException(string message) : base(message)
    {
    }
}
