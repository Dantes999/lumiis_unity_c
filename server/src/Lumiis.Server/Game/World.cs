using System;
using System.Collections.Generic;
using Lumiis.Data.Maps;
using Lumiis.Shared;
using Lumiis.Shared.Net;

namespace Lumiis.Server.Game;

/// <summary>Hält alle Entities einer Map und die reine Spiellogik (Join, Despawn, Bewegung). Kein Netzwerk,
/// kein EF Core-Bezug - bekommt nur einfache Werte übergeben (CharacterId, Name, Appearance-freie Position),
/// die Charakter-Zeile selbst lädt GameServer. CollisionMap (Lumiis.Data, aber ohne EF-Core-Kopplung) wird
/// ebenso injiziert statt selbst geladen - World bleibt frei von Dateizugriffen.</summary>
public sealed class World
{
    private readonly Dictionary<int, Entity> _entities = new();
    private readonly Dictionary<int, int> _entityIdByCharacterId = new();
    private readonly CollisionMap? _collisionMap;
    private int _nextEntityId = 1;

    /// <summary>collisionMap: null bedeutet "überall begehbar" (z. B. bestehende Tests ohne Kollisionsbezug) -
    /// echte Karten liefern immer eine Instanz mit, siehe GameServer/Program.cs.</summary>
    public World(int maxPlayers, CollisionMap? collisionMap = null)
    {
        MaxPlayers = maxPlayers;
        _collisionMap = collisionMap;
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

    /// <summary>Legt eine Monster-Entity an (Serverstart, siehe GameServer/Program.cs) - EntityId aus
    /// demselben Zähler wie Spieler (siehe World-Klassenkommentar/_nextEntityId), kein Verbindungsbezug, kein
    /// Spielerlimit-Check. x/y kommen bereits geprüft aus SpawnCatalog (begehbare Zelle), werden hier trotzdem
    /// defensiv auf die Kartengrenzen geklemmt, analog zu TryJoin.</summary>
    public Entity SpawnMonster(int mobId, string name, int x, int y)
    {
        int clampedX = Math.Clamp(x, 0, GameConstants.MapWidth);
        int clampedY = Math.Clamp(y, 0, GameConstants.MapHeight);

        int id = _nextEntityId++;
        Entity entity = Entity.CreateMonster(id, mobId, name, clampedX, clampedY);
        _entities[id] = entity;
        return entity;
    }

    public bool Remove(int entityId)
    {
        if (!_entities.TryGetValue(entityId, out Entity? entity)) return false;
        _entities.Remove(entityId);
        if (entity.CharacterId is int characterId) _entityIdByCharacterId.Remove(characterId); // Monster (CharacterId null) waren nie drin.
        return true;
    }

    public bool TryGet(int entityId, out Entity? entity)
    {
        return _entities.TryGetValue(entityId, out entity);
    }

    /// <summary>Setzt ein Bewegungsziel, zuerst auf die Kartengrenzen geklemmt, danach (falls eine
    /// CollisionMap gesetzt ist) gegen sie geprüft: die gerade Linie von der aktuellen Position zum
    /// geklemmten Ziel wird zellenweise abgeschritten (ClipToLastWalkableCell), die Bewegung stoppt an der
    /// letzten begehbaren Zelle vor einer blockierten.</summary>
    public bool SetDestination(int entityId, int targetX, int targetY)
    {
        if (!_entities.TryGetValue(entityId, out Entity? entity)) return false;

        int clampedX = Math.Clamp(targetX, 0, GameConstants.MapWidth);
        int clampedY = Math.Clamp(targetY, 0, GameConstants.MapHeight);

        if (_collisionMap == null)
        {
            entity.MoveTo(clampedX, clampedY, GameConstants.PlayerSpeed);
            return true;
        }

        int startCellX = entity.IntX / 100;
        int startCellY = entity.IntY / 100;

        // Sollte nicht vorkommen (Spawn-Positionen und vorherige Bewegungsziele landen nie auf einer
        // blockierten Zelle), aber abgesichert: von einer blockierten Startzelle aus keine Bewegung auslösen.
        if (_collisionMap.IsBlocked(startCellX, startCellY))
            return true;

        int targetCellX = clampedX / 100;
        int targetCellY = clampedY / 100;

        (int stopCellX, int stopCellY) = ClipToLastWalkableCell(_collisionMap, startCellX, startCellY, targetCellX, targetCellY);

        if (stopCellX == targetCellX && stopCellY == targetCellY)
        {
            // Ganzer Weg begehbar - Ziel exakt wie ohne Kollisionsprüfung, kein Zellen-Snapping.
            entity.MoveTo(clampedX, clampedY, GameConstants.PlayerSpeed);
            return true;
        }

        if (stopCellX == startCellX && stopCellY == startCellY)
            return true; // Schon die erste Zelle Richtung Ziel ist blockiert - keine Bewegung.

        entity.MoveTo(stopCellX * 100 + 50, stopCellY * 100 + 50, GameConstants.PlayerSpeed);
        return true;
    }

    /// <summary>Bresenham-Linienalgorithmus von der Start- zur Zielzelle: schreitet die Zellen der geraden
    /// Verbindungslinie ab und gibt die letzte NICHT blockierte Zelle zurück (das ist die Zielzelle selbst,
    /// wenn der ganze Weg frei ist, oder die Startzelle, wenn schon der erste Schritt blockiert ist).</summary>
    private static (int CellX, int CellY) ClipToLastWalkableCell(CollisionMap map, int startCellX, int startCellY, int targetCellX, int targetCellY)
    {
        int dx = Math.Abs(targetCellX - startCellX);
        int dy = -Math.Abs(targetCellY - startCellY);
        int sx = startCellX < targetCellX ? 1 : -1;
        int sy = startCellY < targetCellY ? 1 : -1;
        int err = dx + dy;

        int cellX = startCellX;
        int cellY = startCellY;
        int lastWalkableX = startCellX;
        int lastWalkableY = startCellY;

        while (cellX != targetCellX || cellY != targetCellY)
        {
            int e2 = 2 * err;
            if (e2 >= dy) { err += dy; cellX += sx; }
            if (e2 <= dx) { err += dx; cellY += sy; }

            if (map.IsBlocked(cellX, cellY)) break;

            lastWalkableX = cellX;
            lastWalkableY = cellY;
        }

        return (lastWalkableX, lastWalkableY);
    }

    /// <summary>Für jede Entity: welche anderen Entities liegen innerhalb von radiusWire (quadrierte Distanz,
    /// long-Arithmetik gegen Overflow bei bis zu GameConstants.MapWidth/Height pro Achse)? Reiner
    /// Distanzvergleich, keine Sichtlinien-/Hindernisprüfung (kein Ziel für M3b). Eine Entity sieht sich
    /// selbst nie. Der Radius ist inklusiv (Distanz == radiusWire gilt als sichtbar) - ein exaktes Treffen
    /// der Grenze ist bei quadrierter Ganzzahl-Distanz selten, inklusiv vermeidet zumindest in diesem
    /// seltenen Fall ein Flackern zwischen zwei Ticks mit identischer Distanz. Sichtbarkeit ist symmetrisch
    /// (A sieht B genau dann, wenn B sieht A) - jedes Paar wird deshalb nur einmal verglichen.</summary>
    public Dictionary<int, HashSet<int>> ComputeVisibility(int radiusWire)
    {
        var visible = new Dictionary<int, HashSet<int>>();
        var entities = new List<Entity>(_entities.Values);
        foreach (Entity entity in entities)
            visible[entity.Id] = new HashSet<int>();

        long radiusSq = (long)radiusWire * radiusWire;

        for (int i = 0; i < entities.Count; i++)
        {
            for (int j = i + 1; j < entities.Count; j++)
            {
                Entity a = entities[i];
                Entity b = entities[j];
                long dx = a.IntX - b.IntX;
                long dy = a.IntY - b.IntY;
                if (dx * dx + dy * dy <= radiusSq)
                {
                    visible[a.Id].Add(b.Id);
                    visible[b.Id].Add(a.Id);
                }
            }
        }

        return visible;
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
