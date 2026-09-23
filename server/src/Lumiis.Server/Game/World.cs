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
    private readonly Random _random;
    private int _nextEntityId = 1;

    /// <summary>Fix, keine Konfiguration (siehe docs/tasks/M4a-combat-core.md Abschnitt 4) - 3s nach dem Tod
    /// respawnt ein Spieler automatisch (siehe ApplyDamage/Tick).</summary>
    private const long PlayerRespawnDelayMs = 3000;

    /// <summary>collisionMap: null bedeutet "überall begehbar" (z. B. bestehende Tests ohne Kollisionsbezug) -
    /// echte Karten liefern immer eine Instanz mit, siehe GameServer/Program.cs. random: injizierbare
    /// Zufallsquelle für die Wander-KI (siehe RunWanderAi), Default eine echte, nicht-deterministische
    /// Instanz - Tests übergeben eine geseedete Instanz für deterministisches Verhalten.</summary>
    public World(int maxPlayers, CollisionMap? collisionMap = null, Random? random = null)
    {
        MaxPlayers = maxPlayers;
        _collisionMap = collisionMap;
        _random = random ?? new Random();
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
    /// <summary>maxHp/attack/defense/meleeRangeWire/attackCooldownMs: Kampfwerte aus der ClassCatalog (M4a,
    /// siehe GameServer.CompleteJoin) - Defaults nur für bestehende Tests ohne Kampfbezug, siehe
    /// Entity.DefaultMaxHp-Kommentar.</summary>
    public bool TryJoin(int characterId, string name, int? x, int? y, out Entity? entity, out JoinRejectReason reason,
        int maxHp = Entity.DefaultMaxHp, int attack = Entity.DefaultAttack, int defense = Entity.DefaultDefense,
        int meleeRangeWire = Entity.DefaultMeleeRangeWire, int attackCooldownMs = Entity.DefaultAttackCooldownMs)
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
        entity = new Entity(id, characterId, name, spawnX, spawnY, maxHp, attack, defense, meleeRangeWire, attackCooldownMs);
        _entities[id] = entity;
        _entityIdByCharacterId[characterId] = id;
        return true;
    }

    /// <summary>Legt eine Monster-Entity an (Serverstart, siehe GameServer/Program.cs) - EntityId aus
    /// demselben Zähler wie Spieler (siehe World-Klassenkommentar/_nextEntityId), kein Verbindungsbezug, kein
    /// Spielerlimit-Check. x/y kommen bereits geprüft aus SpawnCatalog (begehbare Zelle), werden hier trotzdem
    /// defensiv auf die Kartengrenzen geklemmt, analog zu TryJoin. nowMs: Wandzeit für die erste
    /// Wander-Fälligkeit (zufällig zwischen wanderIntervalMinMs/MaxMs ab jetzt, siehe RunWanderAi).</summary>
    /// <summary>maxHp/attack/defense/yangMin/yangMax: Kampfwerte aus der MobCatalog (M4a, siehe
    /// GameServer-Konstruktor) - Defaults nur für bestehende Tests ohne Kampfbezug, siehe
    /// Entity.DefaultMaxHp-Kommentar. spawnPointId: null (Default) bedeutet "nicht an einen SpawnPoint
    /// gebunden" (Dev-Tools-Debug-Spawn, siehe Entity.SpawnPointId) - der reguläre Katalog-Spawn im
    /// GameServer-Konstruktor übergibt hier immer die echte SpawnPoint.Id.</summary>
    public Entity SpawnMonster(int mobId, string name, int x, int y, int speedWire,
        int wanderRadiusWire, int wanderIntervalMinMs, int wanderIntervalMaxMs, long nowMs,
        int maxHp = Entity.DefaultMaxHp, int attack = Entity.DefaultAttack, int defense = Entity.DefaultDefense,
        int yangMin = 0, int yangMax = 0, int? spawnPointId = null)
    {
        int clampedX = Math.Clamp(x, 0, GameConstants.MapWidth);
        int clampedY = Math.Clamp(y, 0, GameConstants.MapHeight);

        int id = _nextEntityId++;
        Entity entity = Entity.CreateMonster(id, mobId, spawnPointId, name, clampedX, clampedY, speedWire,
            wanderRadiusWire, wanderIntervalMinMs, wanderIntervalMaxMs, maxHp, attack, defense, yangMin, yangMax);
        entity.NextWanderAtMs = nowMs + _random.Next(wanderIntervalMinMs, wanderIntervalMaxMs + 1);
        _entities[id] = entity;
        return entity;
    }

    /// <summary>Dev-Tools-Debug-Spawn (siehe GameServer.HandleDebugSpawnMonster): ein Punkt in zufälliger
    /// Richtung, offsetMin..offsetMax Wire-Einheiten von anchor entfernt, auf Kartengrenzen geklemmt. Ist die
    /// Zielzelle blockiert, wird (wie bei RunWanderAi) kein Retry versucht - stattdessen anchor selbst
    /// zurückgegeben (die Absenderposition ist per Definition begehbar, sonst könnte der Spieler dort nicht
    /// stehen). Reine Debug-Bequemlichkeit, keine Präzisionsanforderung.</summary>
    public (int X, int Y) FindNearbyWalkablePoint(int anchorX, int anchorY, int offsetMin, int offsetMax)
    {
        double angle = _random.NextDouble() * Math.PI * 2;
        int distance = _random.Next(offsetMin, offsetMax + 1);
        int targetX = Math.Clamp(anchorX + (int)Math.Round(Math.Cos(angle) * distance), 0, GameConstants.MapWidth);
        int targetY = Math.Clamp(anchorY + (int)Math.Round(Math.Sin(angle) * distance), 0, GameConstants.MapHeight);

        if (_collisionMap != null && _collisionMap.IsBlocked(targetX / 100, targetY / 100))
            return (anchorX, anchorY);

        return (targetX, targetY);
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
            entity.MoveTo(clampedX, clampedY, entity.MovementSpeed);
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
            entity.MoveTo(clampedX, clampedY, entity.MovementSpeed);
            return true;
        }

        if (stopCellX == startCellX && stopCellY == startCellY)
            return true; // Schon die erste Zelle Richtung Ziel ist blockiert - keine Bewegung.

        entity.MoveTo(stopCellX * 100 + 50, stopCellY * 100 + 50, entity.MovementSpeed);
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

    /// <summary>Serverautoritative Angriffsprüfung + -ausführung (M4a) - kein Netzwerkbezug, siehe
    /// GameServer.HandleAttackRequest für die Netzwerk-Seite. Prüft in dieser Reihenfolge (siehe
    /// docs/tasks/M4a-combat-core.md Abschnitt 4): Ziel existiert, Ziel lebt, Ziel ist kein Spieler (PvP in
    /// M4a abgelehnt), Cooldown des Angreifers, Reichweite (quadrierte Distanz wie ComputeVisibility, gegen
    /// MeleeRangeWire des Angreifers). attackerEntityId muss bereits als existierende, per Verbindung
    /// gehaltene Entity geprüft sein (siehe GameServer.HandleAttackRequest) - der defensive TargetNotFound-
    /// Fallback hier sollte nie greifen (es gäbe dafür auch keine Verbindung, an die AttackFailed ginge).</summary>
    public AttackResult TryAttack(int attackerEntityId, int targetEntityId, long nowMs)
    {
        if (!_entities.TryGetValue(attackerEntityId, out Entity? attacker))
            return AttackResult.Fail(AttackFailedReason.TargetNotFound);

        if (!_entities.TryGetValue(targetEntityId, out Entity? target))
            return AttackResult.Fail(AttackFailedReason.TargetNotFound);

        if (target.IsDead)
            return AttackResult.Fail(AttackFailedReason.TargetAlreadyDead);

        if (target.Kind == EntityKind.Player)
            return AttackResult.Fail(AttackFailedReason.TargetIsPlayer);

        if (nowMs < attacker.AttackReadyAtMs)
            return AttackResult.Fail(AttackFailedReason.OnCooldown);

        long dx = attacker.IntX - target.IntX;
        long dy = attacker.IntY - target.IntY;
        long rangeSq = (long)attacker.MeleeRangeWire * attacker.MeleeRangeWire;
        if (dx * dx + dy * dy > rangeSq)
            return AttackResult.Fail(AttackFailedReason.OutOfRange);

        attacker.AttackReadyAtMs = nowMs + attacker.AttackCooldownMs;

        int targetMaxHp = target.MaxHp;
        int damage = Math.Max(1, attacker.Attack - target.Defense);
        (int targetHpAfter, bool died, long yangAwarded) = ApplyDamage(target, damage, nowMs);

        if (yangAwarded > 0)
            attacker.Yang += yangAwarded;

        return AttackResult.Hit(damage, targetHpAfter, targetMaxHp, died, target.Kind, yangAwarded);
    }

    /// <summary>Wendet Schaden auf eine beliebige Entity an und löst bei 0 Leben die passende Tod-Behandlung
    /// aus: Monster werden sofort aus World entfernt (wie ein normaler Despawn, TODO(M4b): stattdessen nach
    /// SpawnPoint.RespawnSeconds respawnen statt endgültig zu verschwinden) und zahlen eine zufällige
    /// Yang-Spanne aus; Spieler werden NICHT entfernt (kein Despawn), sondern bekommen RespawnAtMs gesetzt
    /// (siehe Tick) - CurrentHp bleibt bis dahin 0. Von TryAttack nach allen Prüfungen aufgerufen, zusätzlich
    /// internal sichtbar: TryAttack lehnt Spieler-Ziele selbst ab (kein PvP in M4a), das Sterbe-/Respawn-
    /// Verhalten für Spieler ist trotzdem generisch implementiert und muss direkt testbar sein (siehe
    /// WorldTests) - für eine spätere Erweiterung (z. B. Monster, die Spieler angreifen) bereits vorbereitet.</summary>
    internal (int HpAfter, bool Died, long YangAwarded) ApplyDamage(Entity target, int damage, long nowMs)
    {
        int hpAfter = Math.Max(0, target.CurrentHp - damage);
        target.CurrentHp = hpAfter;

        bool died = hpAfter <= 0;
        long yangAwarded = 0;

        if (died)
        {
            if (target.Kind == EntityKind.Monster)
            {
                yangAwarded = _random.Next(target.YangMin, target.YangMax + 1);
                Remove(target.Id);
            }
            else
            {
                target.StopMoving();
                target.RespawnAtMs = nowMs + PlayerRespawnDelayMs;
            }
        }

        return (hpAfter, died, yangAwarded);
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

    /// <summary>Schreibt alle Bewegungen um deltaSeconds fort (Spieler-MoveTo UND laufende Monster-Wander-
    /// Bewegung, derselbe Mechanismus). Läuft danach die Wander-KI für fällige Monster (Reihenfolge
    /// willkürlich, aber konsistent - siehe docs/tasks/M3c-monsters.md), zuletzt fällige Spieler-Respawns
    /// (M4a, siehe ApplyDamage): Position auf Kartenmitte, CurrentHp auf MaxHp, EntityKind/Appearance
    /// unverändert. Gibt drei Listen zurück: Arrived (Entities, die in diesem Tick exakt am Ziel angekommen
    /// sind - für EntityStop), StartedWandering (Monster, die in diesem Tick per RunWanderAi ein neues
    /// Bewegungsziel bekommen haben - für EntityMove, analog zu HandleMoveTo bei Spielern, die aber über ein
    /// echtes MoveToPacket ausgelöst werden statt über den Tick) und Respawned (Spieler, die in diesem Tick
    /// respawnt sind - ebenfalls für EntityStop an der neuen Position, siehe GameServer).</summary>
    public (List<Entity> Arrived, List<Entity> StartedWandering, List<Entity> Respawned) Tick(double deltaSeconds, long nowMs)
    {
        List<Entity> arrived = new();
        foreach (Entity entity in _entities.Values)
        {
            if (entity.Tick(deltaSeconds))
                arrived.Add(entity);
        }

        List<Entity> startedWandering = new();
        foreach (Entity entity in _entities.Values)
        {
            if (entity.Kind != EntityKind.Monster) continue;
            if (nowMs < entity.NextWanderAtMs) continue;

            if (RunWanderAi(entity, nowMs))
                startedWandering.Add(entity);
        }

        List<Entity> respawned = new();
        foreach (Entity entity in _entities.Values)
        {
            if (entity.Kind != EntityKind.Player) continue;
            if (entity.RespawnAtMs == 0 || nowMs < entity.RespawnAtMs) continue;

            entity.Teleport(GameConstants.MapWidth / 2, GameConstants.MapHeight / 2);
            entity.CurrentHp = entity.MaxHp;
            entity.RespawnAtMs = 0;
            respawned.Add(entity);
        }

        return (arrived, startedWandering, respawned);
    }

    /// <summary>Eine fällige Wander-Entscheidung für ein Monster: zufälliger Punkt innerhalb WanderRadiusWire
    /// um den Anker (Quadrat, nicht Kreis - gleiche einfache Herangehensweise wie BotTargetPicker für
    /// Spieler-Bots), auf Kartengrenzen geklemmt. Ist die Zielzelle blockiert, wird dieser Zyklus
    /// übersprungen (kein Retry-Loop) - die Bewegung selbst nutzt danach denselben SetDestination-Mechanismus
    /// wie Spieler-MoveTo (inklusive Kollisions-Klemmung entlang der Linie aus M3a), keine neue
    /// Bewegungslogik. Die nächste Fälligkeit wird immer neu bestimmt, unabhängig davon, ob diese Runde
    /// übersprungen wurde. Gibt zurück, ob tatsächlich eine neue Bewegung gestartet wurde.</summary>
    private bool RunWanderAi(Entity entity, long nowMs)
    {
        entity.NextWanderAtMs = nowMs + _random.Next(entity.WanderIntervalMinMs, entity.WanderIntervalMaxMs + 1);

        int offsetX = _random.Next(-entity.WanderRadiusWire, entity.WanderRadiusWire + 1);
        int offsetY = _random.Next(-entity.WanderRadiusWire, entity.WanderRadiusWire + 1);
        int targetX = Math.Clamp(entity.WanderAnchorX + offsetX, 0, GameConstants.MapWidth);
        int targetY = Math.Clamp(entity.WanderAnchorY + offsetY, 0, GameConstants.MapHeight);

        if (_collisionMap != null && _collisionMap.IsBlocked(targetX / 100, targetY / 100))
            return false;

        SetDestination(entity.Id, targetX, targetY);
        return true;
    }
}
