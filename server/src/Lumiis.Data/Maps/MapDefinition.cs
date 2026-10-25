using System;
using System.Collections.Generic;
using Lumiis.Data.Mobs;
using Lumiis.Data.Npcs;

namespace Lumiis.Data.Maps;

/// <summary>Rechteck-Portal in Wire-Koordinaten (siehe docs/map-import-plan.md). Ein Spieler, der sich
/// innerhalb des Rechtecks aufhält, wird auf die Zielkarte an die Ankunftsposition versetzt. Die
/// Ankunftsposition wird von MapCatalog validiert (begehbar UND nicht selbst in einem Portal der Zielkarte -
/// sonst würde der Wechsel sofort erneut auslösen).</summary>
public sealed class PortalDefinition
{
    public int Id { get; }
    public int X1 { get; }
    public int Y1 { get; }
    public int X2 { get; }
    public int Y2 { get; }
    public string TargetMapId { get; }
    public int TargetX { get; }
    public int TargetY { get; }

    public PortalDefinition(int id, int x1, int y1, int x2, int y2, string targetMapId, int targetX, int targetY)
    {
        Id = id;
        X1 = x1;
        Y1 = y1;
        X2 = x2;
        Y2 = y2;
        TargetMapId = targetMapId;
        TargetX = targetX;
        TargetY = targetY;
    }

    /// <summary>Inklusive Grenzen - die Rechteckkante zählt mit (dieselbe Konvention wie der AOI-Radius).</summary>
    public bool Contains(int wireX, int wireY)
    {
        return wireX >= X1 && wireX <= X2 && wireY >= Y1 && wireY <= Y2;
    }
}

/// <summary>Eine begehbare Karte mit Kollision, Startposition, Portalen sowie den bereits validierten
/// Monster-/NPC-Platzierungen. Reine Daten - World/GameServer laden selbst keine Dateien, MapCatalog
/// übernimmt das Laden/Validieren (analog zu CollisionMap/SpawnCatalog/NpcSpawnCatalog).</summary>
public sealed class MapDefinition
{
    public string Id { get; }
    public string Name { get; }
    public CollisionMap Collision { get; }
    public int StartX { get; }
    public int StartY { get; }
    public IReadOnlyList<PortalDefinition> Portals { get; }
    public IReadOnlyList<SpawnPoint> Spawns { get; }
    public IReadOnlyList<NpcPlacement> NpcPlacements { get; }

    /// <summary>Mini-Boss-Events (optional, events.json) - siehe MapEventDefinition.</summary>
    public IReadOnlyList<MapEventDefinition> Events { get; }

    public MapDefinition(string id, string name, CollisionMap collision, int startX, int startY,
        IReadOnlyList<PortalDefinition>? portals = null, IReadOnlyList<SpawnPoint>? spawns = null,
        IReadOnlyList<NpcPlacement>? npcPlacements = null, IReadOnlyList<MapEventDefinition>? events = null)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new MapCatalogException("Karte ohne Id.");
        if (string.IsNullOrWhiteSpace(name))
            throw new MapCatalogException($"Karte '{id}': Name fehlt.");
        if (collision == null)
            throw new MapCatalogException($"Karte '{id}': CollisionMap fehlt.");

        Id = id;
        Name = name;
        Collision = collision;
        StartX = startX;
        StartY = startY;
        Portals = portals ?? Array.Empty<PortalDefinition>();
        Spawns = spawns ?? Array.Empty<SpawnPoint>();
        NpcPlacements = npcPlacements ?? Array.Empty<NpcPlacement>();
        Events = events ?? Array.Empty<MapEventDefinition>();

        ValidateCoordinates(id, "Startposition", startX, startY, collision);
        if (collision.IsBlockedAtWire(startX, startY))
            throw new MapCatalogException(
                $"Karte '{id}': Startposition ({startX},{startY}) liegt auf einer blockierten Zelle.");

        var seenPortalIds = new HashSet<int>();
        foreach (PortalDefinition portal in Portals)
        {
            if (!seenPortalIds.Add(portal.Id))
                throw new MapCatalogException($"Karte '{id}': doppelte Portal-Id {portal.Id}.");

            if (portal.X1 > portal.X2 || portal.Y1 > portal.Y2)
                throw new MapCatalogException(
                    $"Karte '{id}', Portal {portal.Id}: Rechteck ist invertiert ({portal.X1},{portal.Y1})-({portal.X2},{portal.Y2}).");

            ValidateCoordinates(id, $"Portal {portal.Id} (Ecke 1)", portal.X1, portal.Y1, collision);
            ValidateCoordinates(id, $"Portal {portal.Id} (Ecke 2)", portal.X2, portal.Y2, collision);
            ValidateCoordinates(id, $"Portal {portal.Id} (Ziel)", portal.TargetX, portal.TargetY, collision, checkWalkable: false);

            if (string.IsNullOrWhiteSpace(portal.TargetMapId))
                throw new MapCatalogException($"Karte '{id}', Portal {portal.Id}: ZielMapId fehlt.");

            if (!ContainsWalkableCell(collision, portal))
                throw new MapCatalogException(
                    $"Karte '{id}', Portal {portal.Id}: Rechteck enthält keine begehbare Zelle.");
        }
    }

    public bool IsBlockedAtWire(int wireX, int wireY)
    {
        return Collision.IsBlockedAtWire(wireX, wireY);
    }

    /// <summary>True, wenn der Punkt in irgendeinem Portal DIESER Karte liegt - für die
    /// Ankunftsvalidierung (eine Ankunft direkt in einem Portal der Zielkarte würde sofort erneut
    /// auslösen und eine Schleife bilden).</summary>
    public bool IsInsideAnyPortal(int wireX, int wireY)
    {
        foreach (PortalDefinition portal in Portals)
        {
            if (portal.Contains(wireX, wireY)) return true;
        }
        return false;
    }

    private static void ValidateCoordinates(string mapId, string what, int wireX, int wireY, CollisionMap collision,
        bool checkWalkable = true)
    {
        int maxX = collision.Width * 100;
        int maxY = collision.Height * 100;
        if (wireX < 0 || wireY < 0 || wireX > maxX || wireY > maxY)
            throw new MapCatalogException(
                $"Karte '{mapId}': {what} ({wireX},{wireY}) liegt außerhalb der Karte (0..{maxX}, 0..{maxY}).");
        if (checkWalkable && collision.IsBlockedAtWire(wireX, wireY))
            throw new MapCatalogException(
                $"Karte '{mapId}': {what} ({wireX},{wireY}) liegt auf einer blockierten Zelle.");
    }

    private static bool ContainsWalkableCell(CollisionMap collision, PortalDefinition portal)
    {
        int fromCellX = Math.Max(0, portal.X1 / 100);
        int fromCellY = Math.Max(0, portal.Y1 / 100);
        int toCellX = Math.Min(collision.Width - 1, portal.X2 / 100);
        int toCellY = Math.Min(collision.Height - 1, portal.Y2 / 100);
        for (int cellY = fromCellY; cellY <= toCellY; cellY++)
        {
            for (int cellX = fromCellX; cellX <= toCellX; cellX++)
            {
                if (!collision.IsBlocked(cellX, cellY)) return true;
            }
        }
        return false;
    }
}
