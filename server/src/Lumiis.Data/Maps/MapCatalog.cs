using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Lumiis.Data.Mobs;
using Lumiis.Data.Npcs;
using Lumiis.Shared;

namespace Lumiis.Data.Maps;

/// <summary>Lädt und validiert alle Karten aus data/maps/index.json plus je Kartenordner
/// (collision.bin, optional spawns.json/npcs.json/portals.json/map.json). Fail-fast beim Serverstart:
/// doppelte Ids, fehlende Kollision, blockierte Startposition, unbekanntes Portalziel, blockierte oder in
/// einem Portal liegende Ankunft brechen mit klarer Meldung ab (siehe docs/map-import-plan.md).</summary>
public sealed class MapCatalog
{
    private readonly Dictionary<string, MapDefinition> _byId;

    public IReadOnlyList<MapDefinition> Maps { get; }
    public string StartMapId { get; }
    public MapDefinition StartMap
    {
        get { return _byId[StartMapId]; }
    }

    /// <summary>Programmatischer Konstruktor (Tests) - validiert dieselben Regeln wie LoadFromDirectory.</summary>
    public MapCatalog(IReadOnlyList<MapDefinition> maps, string startMapId)
    {
        if (maps == null || maps.Count == 0)
            throw new MapCatalogException("MapCatalog: keine Karten vorhanden.");

        _byId = new Dictionary<string, MapDefinition>(StringComparer.Ordinal);
        foreach (MapDefinition map in maps)
        {
            if (!_byId.TryAdd(map.Id, map))
                throw new MapCatalogException($"MapCatalog: doppelte Karten-Id '{map.Id}'.");
        }

        if (string.IsNullOrWhiteSpace(startMapId) || !_byId.ContainsKey(startMapId))
            throw new MapCatalogException($"MapCatalog: Startkarte '{startMapId}' existiert nicht.");

        foreach (MapDefinition map in maps)
        {
            foreach (PortalDefinition portal in map.Portals)
            {
                if (!_byId.TryGetValue(portal.TargetMapId, out MapDefinition? target))
                    throw new MapCatalogException(
                        $"Karte '{map.Id}', Portal {portal.Id}: Zielkarte '{portal.TargetMapId}' existiert nicht.");

                if (target.Collision.IsBlockedAtWire(portal.TargetX, portal.TargetY))
                    throw new MapCatalogException(
                        $"Karte '{map.Id}', Portal {portal.Id}: Ankunft ({portal.TargetX},{portal.TargetY}) liegt auf einer blockierten Zelle der Zielkarte '{target.Id}'.");

                if (target.IsInsideAnyPortal(portal.TargetX, portal.TargetY))
                    throw new MapCatalogException(
                        $"Karte '{map.Id}', Portal {portal.Id}: Ankunft ({portal.TargetX},{portal.TargetY}) liegt selbst in einem Portal der Zielkarte '{target.Id}' (Endlosschleife).");
            }
        }

        Maps = maps;
        StartMapId = startMapId;
    }

    public bool TryGet(string mapId, out MapDefinition? map)
    {
        if (mapId == null)
        {
            map = null;
            return false;
        }
        return _byId.TryGetValue(mapId, out map);
    }

    public MapDefinition Get(string mapId)
    {
        if (!_byId.TryGetValue(mapId, out MapDefinition? map))
            throw new MapCatalogException($"Unbekannte Karten-Id '{mapId}'.");
        return map;
    }

    /// <summary>Lädt data/maps/index.json und alle dort gelisteten Karten. Mob-/NpcCatalog werden nur zur
    /// Validierung der Platzierungen gebraucht (jede MobId/NpcId muss existieren) - dieselbe Prüfung wie
    /// SpawnCatalog/NpcSpawnCatalog sie beim Einzelladen machen.</summary>
    public static MapCatalog LoadFromDirectory(string mapsDirectory, MobCatalog mobCatalog, NpcCatalog npcCatalog)
    {
        string indexPath = Path.Combine(mapsDirectory, "index.json");
        string json;
        try
        {
            json = File.ReadAllText(indexPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new MapCatalogException($"index.json konnte nicht gelesen werden ({indexPath}): {ex.Message}");
        }

        MapIndexDto? index;
        try
        {
            index = JsonSerializer.Deserialize<MapIndexDto>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new MapCatalogException($"index.json ist kein gültiges JSON: {ex.Message}");
        }

        if (index == null || index.Maps == null || index.Maps.Count == 0)
            throw new MapCatalogException("index.json enthält keine Karten.");
        if (string.IsNullOrWhiteSpace(index.StartMapId))
            throw new MapCatalogException("index.json: startMapId fehlt.");

        var maps = new List<MapDefinition>();
        foreach (MapIndexEntryDto entry in index.Maps)
        {
            if (entry == null || string.IsNullOrWhiteSpace(entry.Id))
                throw new MapCatalogException("index.json: Karten-Eintrag ohne id.");
            maps.Add(LoadMap(mapsDirectory, entry, mobCatalog, npcCatalog));
        }

        return new MapCatalog(maps, index.StartMapId);
    }

    private static MapDefinition LoadMap(string mapsDirectory, MapIndexEntryDto entry, MobCatalog mobCatalog,
        NpcCatalog npcCatalog)
    {
        string mapDirectory = Path.Combine(mapsDirectory, entry.Id);
        string collisionPath = Path.Combine(mapDirectory, "collision.bin");

        CollisionMap collision;
        try
        {
            collision = CollisionMap.LoadFromFile(collisionPath,
                GameConstants.MapWidth / 100, GameConstants.MapHeight / 100);
        }
        catch (Exception ex) when (ex is CollisionMapException or IOException or UnauthorizedAccessException)
        {
            throw new MapCatalogException($"Karte '{entry.Id}': {ex.Message}");
        }

        MapFileDto? mapFile = TryReadOptionalJson<MapFileDto>(Path.Combine(mapDirectory, "map.json"));
        string name = mapFile?.Name ?? entry.Name ?? entry.Id;
        int startX = mapFile?.StartX ?? entry.StartX ?? GameConstants.MapWidth / 2;
        int startY = mapFile?.StartY ?? entry.StartY ?? GameConstants.MapHeight / 2;

        IReadOnlyList<SpawnPoint> spawns = Array.Empty<SpawnPoint>();
        string spawnsPath = Path.Combine(mapDirectory, "spawns.json");
        if (File.Exists(spawnsPath))
            spawns = SpawnCatalog.LoadFromFile(spawnsPath, mobCatalog, collision).Spawns;

        IReadOnlyList<NpcPlacement> npcs = Array.Empty<NpcPlacement>();
        string npcsPath = Path.Combine(mapDirectory, "npcs.json");
        if (File.Exists(npcsPath))
            npcs = NpcSpawnCatalog.LoadFromFile(npcsPath, npcCatalog, collision).Placements;

        IReadOnlyList<PortalDefinition> portals = Array.Empty<PortalDefinition>();
        string portalsPath = Path.Combine(mapDirectory, "portals.json");
        if (File.Exists(portalsPath))
            portals = LoadPortals(portalsPath);

        IReadOnlyList<MapEventDefinition> events = Array.Empty<MapEventDefinition>();
        string eventsPath = Path.Combine(mapDirectory, "events.json");
        if (File.Exists(eventsPath))
            events = LoadEvents(entry.Id, eventsPath, mobCatalog, collision);

        return new MapDefinition(entry.Id, name, collision, startX, startY, portals, spawns, npcs, events);
    }

    /// <summary>Lädt events.json fail-fast: Boss/Eskorte müssen in der MobCatalog existieren, jeder Punkt begehbar,
    /// Intervall &gt; 0, mindestens ein Punkt, Ids eindeutig.</summary>
    public static IReadOnlyList<MapEventDefinition> LoadEvents(string mapId, string path, MobCatalog mobCatalog,
        CollisionMap collision)
    {
        EventsFileDto data = TryReadOptionalJson<EventsFileDto>(path)
            ?? throw new MapCatalogException($"events.json ist leer ({path}).");
        var events = new List<MapEventDefinition>();
        var ids = new HashSet<int>();
        foreach (EventEntryDto e in data.Events ?? new List<EventEntryDto>())
        {
            string where = $"Karte '{mapId}', Event {e.Id}";
            if (!ids.Add(e.Id))
                throw new MapCatalogException($"{where}: doppelte Event-Id.");
            if (!mobCatalog.TryGet(e.BossMobId, out _))
                throw new MapCatalogException($"{where}: unbekannte Boss-MobId {e.BossMobId}.");
            if (e.IntervalSeconds <= 0)
                throw new MapCatalogException($"{where}: intervalSeconds muss > 0 sein.");
            var escort = new List<(int, int)>();
            foreach (EscortDto esc in e.Escort ?? new List<EscortDto>())
            {
                if (!mobCatalog.TryGet(esc.MobId, out _))
                    throw new MapCatalogException($"{where}: unbekannte Eskorte-MobId {esc.MobId}.");
                if (esc.Count < 0 || esc.Count > 20)
                    throw new MapCatalogException($"{where}: Eskorte-Anzahl {esc.Count} außerhalb 0..20.");
                escort.Add((esc.MobId, esc.Count));
            }
            var points = new List<(int, int)>();
            foreach (PointDto pt in e.Points ?? new List<PointDto>())
            {
                if (pt.X < 0 || pt.Y < 0 || pt.X >= collision.Width * 100 || pt.Y >= collision.Height * 100
                    || collision.IsBlockedAtWire(pt.X, pt.Y))
                    throw new MapCatalogException($"{where}: Punkt ({pt.X},{pt.Y}) ist nicht begehbar.");
                points.Add((pt.X, pt.Y));
            }
            if (points.Count == 0)
                throw new MapCatalogException($"{where}: keine Erscheinungsorte.");
            events.Add(new MapEventDefinition(e.Id, e.BossMobId, escort, e.IntervalSeconds, e.AreaName ?? "", points));
        }
        return events;
    }

    private sealed class EventsFileDto
    {
        public List<EventEntryDto>? Events { get; set; }
    }

    private sealed class EventEntryDto
    {
        public int Id { get; set; }
        public int BossMobId { get; set; }
        public List<EscortDto>? Escort { get; set; }
        public int IntervalSeconds { get; set; }
        public string? AreaName { get; set; }
        public List<PointDto>? Points { get; set; }
    }

    private sealed class EscortDto
    {
        public int MobId { get; set; }
        public int Count { get; set; }
    }

    private sealed class PointDto
    {
        public int X { get; set; }
        public int Y { get; set; }
    }

    private static IReadOnlyList<PortalDefinition> LoadPortals(string path)
    {
        string json;
        try
        {
            json = File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new MapCatalogException($"portals.json konnte nicht gelesen werden ({path}): {ex.Message}");
        }

        PortalsFileDto? data;
        try
        {
            data = JsonSerializer.Deserialize<PortalsFileDto>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new MapCatalogException($"portals.json ist kein gültiges JSON ({path}): {ex.Message}");
        }

        if (data == null || data.Portals == null)
            throw new MapCatalogException($"portals.json ist leer ({path}).");

        var portals = new List<PortalDefinition>();
        foreach (PortalEntryDto entry in data.Portals)
        {
            portals.Add(new PortalDefinition(entry.Id, entry.X1, entry.Y1, entry.X2, entry.Y2,
                entry.TargetMapId, entry.TargetX, entry.TargetY));
        }
        return portals;
    }

    private static T? TryReadOptionalJson<T>(string path) where T : class
    {
        if (!File.Exists(path)) return null;
        try
        {
            return JsonSerializer.Deserialize<T>(File.ReadAllText(path), JsonOptions);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            throw new MapCatalogException($"{Path.GetFileName(path)} konnte nicht gelesen werden ({path}): {ex.Message}");
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private sealed class MapIndexDto
    {
        public string? StartMapId { get; set; }
        public List<MapIndexEntryDto>? Maps { get; set; }
    }

    private sealed class MapIndexEntryDto
    {
        public string Id { get; set; } = "";
        public string? Name { get; set; }
        public int? StartX { get; set; }
        public int? StartY { get; set; }
    }

    private sealed class MapFileDto
    {
        public string? Name { get; set; }
        public int? StartX { get; set; }
        public int? StartY { get; set; }
    }

    private sealed class PortalsFileDto
    {
        public List<PortalEntryDto>? Portals { get; set; }
    }

    private sealed class PortalEntryDto
    {
        public int Id { get; set; }
        public int X1 { get; set; }
        public int Y1 { get; set; }
        public int X2 { get; set; }
        public int Y2 { get; set; }
        public string TargetMapId { get; set; } = "";
        public int TargetX { get; set; }
        public int TargetY { get; set; }
    }
}

public sealed class MapCatalogException : Exception
{
    public MapCatalogException(string message) : base(message)
    {
    }
}
