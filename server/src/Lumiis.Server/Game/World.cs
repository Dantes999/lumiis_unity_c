using System;
using System.Collections.Generic;
using Lumiis.Shared;
using Lumiis.Shared.Net;

namespace Lumiis.Server.Game;

/// <summary>Hält alle Entities einer Map und die reine Spiellogik (Join, Despawn, Bewegung). Kein Netzwerk,
/// kein EF Core/Lumiis.Data-Bezug - bekommt nur einfache Werte übergeben (CharacterId, Name, Appearance-
/// freie Position), die Charakter-Zeile selbst lädt GameServer.</summary>
public sealed class World
{
    private readonly Dictionary<int, Entity> _entities = new();
    private readonly Dictionary<int, int> _entityIdByCharacterId = new();
    private int _nextEntityId = 1;

    public World(int maxPlayers)
    {
        MaxPlayers = maxPlayers;
    }

    public int MaxPlayers { get; }

    public IReadOnlyCollection<Entity> Entities => _entities.Values;

    /// <summary>Prüft, ob dieser Charakter schon online ist, und das Spielerlimit; spawnt bei Erfolg an der
    /// übergebenen Position (null = Kartenmitte). Name und Existenz des Charakters sind zu diesem Zeitpunkt
    /// bereits geprüft (GameServer lädt die Character-Zeile per CharacterId+AccountId aus der DB, bevor
    /// TryJoin aufgerufen wird). Keine eigene Namens-Eindeutigkeitsprüfung mehr hier: Character.Name ist
    /// seit M2b0 über einen case-insensitiven Unique-Index in der DB global eindeutig (siehe
    /// LumiisDbContext) - eine zweite, rein speicherresidente Prüfung hier wäre redundant und könnte bei
    /// direkten DB-Änderungen sogar von der tatsächlichen Quelle der Wahrheit abweichen.</summary>
    public bool TryJoin(int characterId, string name, int? x, int? y, out Entity? entity, out JoinRejectReason reason)
    {
        entity = null;
        reason = default;

        if (_entityIdByCharacterId.ContainsKey(characterId))
        {
            reason = JoinRejectReason.CharacterAlreadyOnline;
            return false;
        }

        if (_entities.Count >= MaxPlayers)
        {
            reason = JoinRejectReason.ServerFull;
            return false;
        }

        int spawnX = Math.Clamp(x ?? GameConstants.MapWidth / 2, 0, GameConstants.MapWidth);
        int spawnY = Math.Clamp(y ?? GameConstants.MapHeight / 2, 0, GameConstants.MapHeight);

        int id = _nextEntityId++;
        entity = new Entity(id, characterId, name, spawnX, spawnY);
        _entities[id] = entity;
        _entityIdByCharacterId[characterId] = id;
        return true;
    }

    public bool Remove(int entityId)
    {
        if (!_entities.TryGetValue(entityId, out Entity? entity)) return false;
        _entities.Remove(entityId);
        _entityIdByCharacterId.Remove(entity.CharacterId);
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
