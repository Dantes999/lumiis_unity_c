using System;
using System.Collections.Generic;
using Lumiis.Shared;
using Lumiis.Shared.Net;

namespace Lumiis.Server.Game;

/// <summary>Hält alle Entities einer Map und die reine Spiellogik (Join, Despawn, Bewegung). Kein Netzwerk.</summary>
public sealed class World
{
    private readonly Dictionary<int, Entity> _entities = new();
    private readonly Dictionary<string, int> _entityIdByNameLower = new();
    private int _nextEntityId = 1;

    public World(int maxPlayers)
    {
        MaxPlayers = maxPlayers;
    }

    public int MaxPlayers { get; }

    public IReadOnlyCollection<Entity> Entities => _entities.Values;

    /// <summary>Prüft Name und Spielerlimit und spawnt bei Erfolg am Kartenzentrum.</summary>
    public bool TryJoin(string name, out Entity? entity, out JoinRejectReason reason)
    {
        entity = null;
        reason = default;

        if (!NameRules.IsValid(name))
        {
            reason = JoinRejectReason.InvalidName;
            return false;
        }

        string key = name.ToLowerInvariant();
        if (_entityIdByNameLower.ContainsKey(key))
        {
            reason = JoinRejectReason.NameTaken;
            return false;
        }

        if (_entities.Count >= MaxPlayers)
        {
            reason = JoinRejectReason.ServerFull;
            return false;
        }

        int id = _nextEntityId++;
        entity = new Entity(id, name, GameConstants.MapWidth / 2, GameConstants.MapHeight / 2);
        _entities[id] = entity;
        _entityIdByNameLower[key] = id;
        return true;
    }

    public bool Remove(int entityId)
    {
        if (!_entities.TryGetValue(entityId, out Entity? entity)) return false;
        _entities.Remove(entityId);
        _entityIdByNameLower.Remove(entity.Name.ToLowerInvariant());
        return true;
    }

    public bool TryGet(int entityId, out Entity? entity)
    {
        return _entities.TryGetValue(entityId, out entity);
    }

    /// <summary>Setzt ein Bewegungsziel, geklemmt auf die Kartengrenzen. TODO(M3): Sichtbarkeit/AOI - aktuell sehen alle Entities alle anderen.</summary>
    public bool SetDestination(int entityId, int targetX, int targetY)
    {
        if (!_entities.TryGetValue(entityId, out Entity? entity)) return false;

        int clampedX = Math.Clamp(targetX, 0, GameConstants.MapWidth);
        int clampedY = Math.Clamp(targetY, 0, GameConstants.MapHeight);
        entity.MoveTo(clampedX, clampedY, GameConstants.PlayerSpeed);
        return true;
    }

    /// <summary>Schreibt alle Bewegungen um deltaSeconds fort. Gibt die Entities zurück, die in diesem Tick exakt am Ziel angekommen sind.</summary>
    public List<Entity> Tick(double deltaSeconds)
    {
        List<Entity> arrived = new();
        foreach (Entity entity in _entities.Values)
        {
            if (entity.Tick(deltaSeconds))
                arrived.Add(entity);
        }
        return arrived;
    }
}
