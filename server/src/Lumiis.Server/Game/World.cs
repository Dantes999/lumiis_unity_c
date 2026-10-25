using System;
using System.Collections.Generic;
using Lumiis.Data.Classes;
using Lumiis.Data.Items;
using Lumiis.Data.Maps;
using Lumiis.Data.Mobs;
using Lumiis.Data.Progression;
using Lumiis.Data.Skills;
using Lumiis.Data.Upgrade;
using Lumiis.Shared;
using Lumiis.Shared.Net;

namespace Lumiis.Server.Game;

/// <summary>Hält alle Entities einer Map und die reine Spiellogik (Join, Despawn, Bewegung). Kein Netzwerk,
/// kein EF Core-Bezug - bekommt nur einfache Werte übergeben (CharacterId, Name, Appearance-freie Position),
/// die Charakter-Zeile selbst lädt GameServer. CollisionMap (Lumiis.Data, aber ohne EF-Core-Kopplung) wird
/// ebenso injiziert statt selbst geladen - World bleibt frei von Dateizugriffen.</summary>
public sealed class World
{
    /// <summary>"Rezept" für die nächste Entity an einem SpawnPoint (M4b) - aus den Werten der zuletzt dort
    /// gestorbenen Monster-Entity eingefroren (siehe ScheduleMonsterRespawn), damit World.Tick bei Fälligkeit
    /// exakt denselben Mob-Typ mit denselben Werten über den normalen SpawnMonster-Weg neu erzeugen kann.
    /// Höchstens ein Eintrag pro SpawnPointId gleichzeitig (ein SpawnPoint hat immer entweder eine lebende
    /// Monster-Entity oder einen ausstehenden Respawn, nie beides).</summary>
    private readonly record struct PendingMonsterRespawn(int MobId, string Name, int PositionX, int PositionY, int SpeedWire,
        int WanderRadiusWire, int WanderIntervalMinMs, int WanderIntervalMaxMs,
        int MaxHp, int Attack, int Defense, int YangMin, int YangMax, int RespawnSeconds,
        int MeleeRangeWire, int AttackCooldownMs, bool Aggressive, int AggroRangeWire, int LeashRangeWire,
        IReadOnlyList<(int ItemId, int ChancePercent)> Drops, int Level, int BaseXp, long DueAtMs, bool FightsBack);

    private readonly Dictionary<int, Entity> _entities = new();
    private readonly Dictionary<int, int> _entityIdByCharacterId = new();
    private readonly Dictionary<int, PendingMonsterRespawn> _pendingMonsterRespawns = new();
    private readonly CollisionMap? _collisionMap;
    private readonly MapDefinition? _mapDefinition;
    private readonly ItemCatalog? _itemCatalog;
    private readonly ProgressionCatalog? _progressionCatalog;
    private readonly SkillCatalog? _skillCatalog;
    private readonly UpgradeCatalog? _upgradeCatalog;
    private readonly MobCatalog? _mobCatalog;
    private readonly MetinCatalog? _metinCatalog;
    private readonly bool _useGroundDrops;
    private readonly bool _requireBlacksmithForUpgrade;
    private readonly Random _random;
    private readonly Func<int> _allocateEntityId;
    private int _nextEntityId = 1;

    /// <summary>Multi-Map: Sperre gegen Mehrfachauslösung eines Portals (siehe CheckPortal) - 1s, fix.</summary>
    private const long PortalCooldownMs = 1000;

    /// <summary>Fix, keine Konfiguration (siehe docs/tasks/M4a-combat-core.md Abschnitt 4) - 3s nach dem Tod
    /// respawnt ein Spieler automatisch (siehe ApplyDamage/Tick).</summary>
    private const long PlayerRespawnDelayMs = 3000;

    /// <summary>collisionMap: null bedeutet "überall begehbar" (z. B. bestehende Tests ohne Kollisionsbezug) -
    /// echte Karten liefern immer eine Instanz mit, siehe GameServer/Program.cs. itemCatalog: null bedeutet
    /// "keine Items" (z. B. bestehende Tests ohne Item-Bezug) - genau wie CollisionMap wird er für laufende
    /// Abfragen gehalten (M5a: TryEquip/TryUnequip/TryUseConsumable müssen zur Laufzeit beliebige ItemIds aus
    /// dem Inventar auflösen können, nicht nur einmalig beim Spawn/Join wie MobCatalog/ClassCatalog - siehe
    /// docs/tasks/M5a-items-equipment.md Abschnitt 0). random: injizierbare Zufallsquelle für die Wander-KI
    /// (siehe RunWanderAi), Drops (siehe ApplyDamage/TryAttack) und seit M6a den Ausweichwurf (siehe
    /// TryAttack), Default eine echte, nicht-deterministische Instanz - Tests übergeben eine geseedete
    /// Instanz für deterministisches Verhalten. progressionCatalog: null bedeutet "kein XP-Gewinn" (z. B.
    /// bestehende Tests ohne Fortschrittsbezug - World.AwardXp wird dann zum No-op, siehe dort) - Program.cs
    /// lädt beim echten Serverstart immer eine Instanz aus data/progression.json (M6a). skillCatalog: null
    /// bedeutet "keine Skills nutzbar" (z. B. bestehende Tests ohne Skillbezug - World.TryUseSkill lehnt dann
    /// jeden Versuch mit NotLearned ab, siehe dort) - Program.cs lädt beim echten Serverstart immer eine
    /// Instanz aus data/skills.json (M6b). upgradeCatalog: null bedeutet "keine Aufwertung möglich" (z. B.
    /// bestehende Tests ohne Aufwertungsbezug - World.TryUpgrade lehnt dann mit NoMaterial ab) - Program.cs
    /// lädt beim echten Serverstart immer eine Instanz aus data/upgrade.json (M6c).</summary>
    public World(int maxPlayers, CollisionMap? collisionMap = null, Random? random = null, ItemCatalog? itemCatalog = null,
        ProgressionCatalog? progressionCatalog = null, SkillCatalog? skillCatalog = null,
        MapDefinition? mapDefinition = null, Func<int>? entityIdGenerator = null, UpgradeCatalog? upgradeCatalog = null,
        MobCatalog? mobCatalog = null, MetinCatalog? metinCatalog = null, bool useGroundDrops = false,
        bool requireBlacksmithForUpgrade = false)
    {
        MaxPlayers = maxPlayers;
        _mapDefinition = mapDefinition;
        _collisionMap = mapDefinition?.Collision ?? collisionMap;
        MapId = mapDefinition?.Id ?? "village";
        _itemCatalog = itemCatalog;
        _progressionCatalog = progressionCatalog;
        _skillCatalog = skillCatalog;
        _upgradeCatalog = upgradeCatalog;
        _mobCatalog = mobCatalog;
        _metinCatalog = metinCatalog;
        _useGroundDrops = useGroundDrops;
        _requireBlacksmithForUpgrade = requireBlacksmithForUpgrade;
        _random = random ?? new Random();
        _allocateEntityId = entityIdGenerator ?? (() => _nextEntityId++);
    }

    public int MaxPlayers { get; }

    /// <summary>Multi-Map: Id der Karte, die diese World hält (siehe MapDefinition).</summary>
    public string MapId { get; }

    /// <summary>Startposition der Karte (auch Respawn-Ziel für Spieler) - ohne MapDefinition die Kartenmitte.</summary>
    public int StartX
    {
        get { return _mapDefinition?.StartX ?? GameConstants.MapWidth / 2; }
    }

    public int StartY
    {
        get { return _mapDefinition?.StartY ?? GameConstants.MapHeight / 2; }
    }

    public IReadOnlyList<PortalDefinition> Portals
    {
        get { return _mapDefinition?.Portals ?? (IReadOnlyList<PortalDefinition>)Array.Empty<PortalDefinition>(); }
    }

    public IReadOnlyCollection<Entity> Entities => _entities.Values;

    /// <summary>Prüft, ob dieser Charakter schon online ist, und das Spielerlimit; spawnt bei Erfolg an der
    /// übergebenen Position (null = Kartenmitte). Name und Existenz des Charakters sind zu diesem Zeitpunkt
    /// bereits geprüft (GameServer lädt die Character-Zeile per CharacterId+AccountId aus der DB, bevor
    /// TryJoin aufgerufen wird). Keine eigene Namens-Eindeutigkeitsprüfung mehr hier: Character.Name ist
    /// seit M2b0 über einen case-insensitiven Unique-Index in der DB global eindeutig (siehe
    /// LumiisDbContext) - eine zweite, rein speicherresidente Prüfung hier wäre redundant und könnte bei
    /// direkten DB-Änderungen sogar von der tatsächlichen Quelle der Wahrheit abweichen.</summary>
    /// <summary>maxHp/attack/defense/meleeRangeWire/attackCooldownMs: Kampfwerte-Fallback für bestehende
    /// Tests ohne Kampf-/Fortschrittsbezug (kein classDef, siehe Entity.DefaultMaxHp-Kommentar). classDef:
    /// Klassen-Formelkonstanten (M6a, siehe GameServer.CompleteJoin) - null bedeutet "keine Formel-Werte",
    /// dann gelten maxHp/attack/defense direkt wie vor M6a. level/xp/spent*/unspent*: aus der geladenen
    /// Character-Zeile (M6a) - Defaults (Level 1, alles andere 0) entsprechen einem frischen Charakter.
    /// currentHp/currentSp: aus der Character-Zeile, Login-Regeln siehe unten - Defaults null (voll).</summary>
    public bool TryJoin(int characterId, string name, int? x, int? y, out Entity? entity, out JoinRejectReason reason,
        int maxHp = Entity.DefaultMaxHp, int attack = Entity.DefaultAttack, int defense = Entity.DefaultDefense,
        int meleeRangeWire = Entity.DefaultMeleeRangeWire, int attackCooldownMs = Entity.DefaultAttackCooldownMs,
        ClassDefinition? classDef = null, int level = 1, long xp = 0,
        int spentSt = 0, int spentHt = 0, int spentDx = 0, int spentIq = 0,
        int unspentStatPoints = 0, int unspentSkillPoints = 0,
        int? currentHp = null, int? currentSp = null)
    {
        entity = null;
        reason = default;

        if (_entityIdByCharacterId.ContainsKey(characterId))
        {
            reason = JoinRejectReason.CharacterAlreadyOnline;
            return false;
        }

        if (_entityIdByCharacterId.Count >= MaxPlayers)
        {
            reason = JoinRejectReason.ServerFull;
            return false;
        }

        int spawnX = Math.Clamp(x ?? StartX, 0, GameConstants.MapWidth);
        int spawnY = Math.Clamp(y ?? StartY, 0, GameConstants.MapHeight);

        int id = _allocateEntityId();
        Entity created = new(id, characterId, name, spawnX, spawnY, maxHp, attack, defense, meleeRangeWire, attackCooldownMs, classDef);
        created.MapId = MapId;
        created.Level = level;
        created.Xp = xp;
        created.SpentSt = spentSt;
        created.SpentHt = spentHt;
        created.SpentDx = spentDx;
        created.SpentIq = spentIq;
        created.UnspentStatPoints = unspentStatPoints;
        created.UnspentSkillPoints = unspentSkillPoints;

        if (classDef != null)
            RecomputeDerivedStats(created);

        // M6a Login-Regeln (siehe docs/tasks/M6a-progression.md Abschnitt 0d/4): null -> voll, <= 0 ->
        // wiederbelebt (HP UND SP voll, Position Kartenmitte - "Ausloggen im Tod"), sonst auf das
        // berechnete Maximum begrenzt. "Wiederbelebt" wird ausschließlich an CurrentHp festgemacht (dieselbe
        // HP<=0-Definition wie Entity.IsDead) - CurrentSp hat in M6a keinen eigenen "Tod"-Zustand (nichts
        // senkt SP unter 0), wird im Wiederbelebungsfall aber trotzdem mit auf voll gesetzt.
        bool revived = currentHp.HasValue && currentHp.Value <= 0;
        if (revived)
        {
            created.CurrentHp = created.MaxHp;
            created.CurrentSp = created.MaxSp;
            created.Teleport(StartX, StartY);
        }
        else
        {
            created.CurrentHp = currentHp.HasValue ? Math.Min(currentHp.Value, created.MaxHp) : created.MaxHp;
            created.CurrentSp = currentSp.HasValue ? Math.Min(currentSp.Value, created.MaxSp) : created.MaxSp;
        }

        entity = created;
        _entities[id] = created;
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
    /// GameServer-Konstruktor übergibt hier immer die echte SpawnPoint.Id. respawnSeconds: nur bei gesetzter
    /// spawnPointId aussagekräftig (siehe Entity.RespawnSeconds, M4b). meleeRangeWire/attackCooldownMs/
    /// aggressive/aggroRangeWire/leashRangeWire: Kampf-/Aggro-Werte aus der MobCatalog (M4c). drops: mögliche
    /// Drops beim Tod (M5a, siehe Entity.Drops) - Default leer. level/baseXp: M6a, siehe MobDefinition.Level/
    /// BaseXp - Defaults nur für bestehende Tests ohne XP-Bezug.</summary>
    public Entity SpawnMonster(int mobId, string name, int x, int y, int speedWire,
        int wanderRadiusWire, int wanderIntervalMinMs, int wanderIntervalMaxMs, long nowMs,
        int maxHp = Entity.DefaultMaxHp, int attack = Entity.DefaultAttack, int defense = Entity.DefaultDefense,
        int yangMin = 0, int yangMax = 0, int? spawnPointId = null, int respawnSeconds = 0,
        int meleeRangeWire = Entity.DefaultMeleeRangeWire, int attackCooldownMs = Entity.DefaultAttackCooldownMs,
        bool aggressive = false, int aggroRangeWire = 0, int leashRangeWire = 0,
        IReadOnlyList<(int ItemId, int ChancePercent)>? drops = null, int level = 1, int baseXp = 0)
    {
        int clampedX = Math.Clamp(x, 0, GameConstants.MapWidth);
        int clampedY = Math.Clamp(y, 0, GameConstants.MapHeight);

        int id = _allocateEntityId();
        Entity entity = Entity.CreateMonster(id, mobId, spawnPointId, name, clampedX, clampedY, speedWire,
            wanderRadiusWire, wanderIntervalMinMs, wanderIntervalMaxMs, maxHp, attack, defense, yangMin, yangMax, respawnSeconds,
            meleeRangeWire, attackCooldownMs, aggressive, aggroRangeWire, leashRangeWire, drops, level, baseXp);
        entity.MapId = MapId;
        entity.NextWanderAtMs = nowMs + _random.Next(wanderIntervalMinMs, wanderIntervalMaxMs + 1);
        _entities[id] = entity;
        return entity;
    }

    /// <summary>Legt eine statische NPC-Entity an (M5b, Serverstart, siehe GameServer-Konstruktor) - EntityId
    /// aus demselben Zähler wie Spieler/Monster, kein Verbindungsbezug, kein Spielerlimit-Check. x/y kommen
    /// bereits geprüft aus NpcSpawnCatalog (begehbare Zelle), werden hier trotzdem defensiv auf die
    /// Kartengrenzen geklemmt, analog zu SpawnMonster/TryJoin. appearance kommt direkt aus der NpcCatalog
    /// (anders als bei Monstern: NPCs haben eine echte, sichtbare Ausrüstung, siehe Entity.CreateNpc-
    /// Kommentar). shopItemIds: Sortiment aus der NpcCatalog (siehe Entity.ShopItemIds) - leer = kein Shop
    /// (aktuell kein Anwendungsfall, alle NpcCatalog-Einträge haben ein validiertes, nicht-leeres Sortiment).</summary>
    public Entity SpawnNpc(int npcId, string name, int x, int y, Appearance appearance, IReadOnlyList<int>? shopItemIds = null,
        NpcService service = NpcService.None)
    {
        int clampedX = Math.Clamp(x, 0, GameConstants.MapWidth);
        int clampedY = Math.Clamp(y, 0, GameConstants.MapHeight);

        int id = _allocateEntityId();
        Entity entity = Entity.CreateNpc(id, npcId, name, clampedX, clampedY);
        entity.Appearance = appearance;
        entity.ShopItemIds = shopItemIds ?? Array.Empty<int>();
        entity.Service = service;
        entity.MapId = MapId;
        _entities[id] = entity;
        return entity;
    }

    /// <summary>Legt einen Boden-Drop an. Nur bei aktiviertem Ground-Drop-Modus (Produktion); Tests ohne
    /// diesen Modus behalten das bisherige Auto-Loot-Verhalten. ownerEntityId: Killer, dem der Drop für
    /// GameConstants.GroundItemOwnershipMs gehört (null = sofort frei). nowMs: Basis für Besitz- und
    /// Lebensdauer (GameConstants.GroundItemLifetimeMs); null = kein Besitz, kein automatisches Verschwinden.</summary>
    public Entity SpawnGroundItem(int itemId, int x, int y, int upgradeLevel = 0, int? ownerEntityId = null, long? nowMs = null)
    {
        string name = _itemCatalog != null && _itemCatalog.TryGet(itemId, out ItemDefinition item)
            ? item.Name
            : "Gegenstand";

        int id = _allocateEntityId();
        Entity entity = Entity.CreateGroundItem(id, itemId, upgradeLevel, name,
            Math.Clamp(x, 0, GameConstants.MapWidth), Math.Clamp(y, 0, GameConstants.MapHeight));
        entity.MapId = MapId;
        if (nowMs is long now)
        {
            if (ownerEntityId is int owner)
            {
                entity.LootOwnerEntityId = owner;
                entity.LootOwnershipEndsAtMs = now + GameConstants.GroundItemOwnershipMs;
            }
            entity.DespawnAtMs = now + GameConstants.GroundItemLifetimeMs;
        }
        _entities[id] = entity;
        return entity;
    }

    /// <summary>Hebt einen Boden-Drop auf. Prüft Existenz, Besitz (Killer bzw. dessen Gruppe, solange
    /// LootOwnershipEndsAtMs nicht erreicht ist), Distanz und Inventarplatz; bei Erfolg wird die Ground-
    /// Entity entfernt. sharesLootWith(ownerEntityId): true, wenn der Aufhebende Beute dieses Besitzers
    /// nehmen darf (GameServer: gleiche Gruppe) - null = nur der Besitzer selbst. Der Aufrufer (GameServer)
    /// verschickt InventorySync/EntityDespawn.</summary>
    public (bool Success, InventoryActionFailedReason Reason) TryPickupItem(int playerEntityId, int groundItemEntityId,
        long nowMs = 0, Func<int, bool>? sharesLootWith = null)
    {
        if (!_entities.TryGetValue(playerEntityId, out Entity? player) || player.Kind != EntityKind.Player)
            return (false, InventoryActionFailedReason.InvalidSlot);

        if (!_entities.TryGetValue(groundItemEntityId, out Entity? ground) || ground.Kind != EntityKind.GroundItem)
            return (false, InventoryActionFailedReason.GroundItemNotFound);

        if (ground.LootOwnerEntityId is int owner && owner != player.Id && nowMs < ground.LootOwnershipEndsAtMs
            && !(sharesLootWith?.Invoke(owner) ?? false))
            return (false, InventoryActionFailedReason.PickupNotOwner);

        long dx = player.IntX - ground.IntX;
        long dy = player.IntY - ground.IntY;
        long rangeSq = (long)GameConstants.PickupRangeWire * GameConstants.PickupRangeWire;
        if (dx * dx + dy * dy > rangeSq)
            return (false, InventoryActionFailedReason.PickupOutOfRange);

        if (!TryAddItemToInventory(playerEntityId, ground.TypeId, ground.ItemUpgradeLevel))
            return (false, InventoryActionFailedReason.InventoryFull);

        Remove(groundItemEntityId);
        return (true, default);
    }

    /// <summary>Entfernt abgelaufene Boden-Drops (DespawnAtMs erreicht) und liefert ihre EntityIds, damit
    /// GameServer EntityDespawn an die Beobachter schicken kann. Einmal pro Tick aufgerufen.</summary>
    public List<int> RemoveExpiredGroundItems(long nowMs)
    {
        List<int> expired = new();
        foreach (Entity entity in _entities.Values)
        {
            if (entity.Kind == EntityKind.GroundItem && entity.DespawnAtMs > 0 && nowMs >= entity.DespawnAtMs)
                expired.Add(entity.Id);
        }
        foreach (int id in expired) Remove(id);
        return expired;
    }

    /// <summary>Verteilt mehrere Drops eines Kills leicht um den Todesort, damit sie nicht exakt übereinander
    /// liegen (deterministisch nach Index, ohne Zufall). Blockierte Versatzpunkte fallen auf den Todesort zurück.</summary>
    private (int X, int Y) GroundDropPosition(int centerX, int centerY, int index)
    {
        if (index == 0) return (centerX, centerY);
        const int step = 40;
        int ring = (index - 1) / 8 + 1;
        (int ox, int oy)[] directions = { (1, 0), (-1, 0), (0, 1), (0, -1), (1, 1), (-1, 1), (1, -1), (-1, -1) };
        (int ox, int oy) = directions[(index - 1) % 8];
        int x = Math.Clamp(centerX + ox * step * ring, 0, GameConstants.MapWidth);
        int y = Math.Clamp(centerY + oy * step * ring, 0, GameConstants.MapHeight);
        return IsBlockedAtWire(x, y) ? (centerX, centerY) : (x, y);
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

    /// <summary>Multi-Map: setzt eine bereits existierende Spieler-Entity auf diese Karte (Portal-/Dev-Tool-
    /// Wechsel). Die EntityId bleibt erhalten (der Client behält seine lokale Figur), MapId wird
    /// aktualisiert. Kein Spielerlimit-/Doppel-Online-Check - der Aufrufer (GameServer) hat die Entity gerade
    /// aus einer anderen World entfernt.</summary>
    public void AddExistingEntity(Entity entity)
    {
        entity.MapId = MapId;
        entity.PortalReadyAtMs = 0;
        _entities[entity.Id] = entity;
        if (entity.CharacterId is int characterId) _entityIdByCharacterId[characterId] = entity.Id;
    }

    /// <summary>Multi-Map: prüft, ob ein Spieler gerade in einem Portal dieser Karte steht und der
    /// Portal-Cooldown abgelaufen ist. Reine Entscheidung ohne Seiteneffekt außer dem Cooldown-Stempel -
    /// der eigentliche Kartenwechsel (Entity aus dieser World entfernen, in die Ziel-World setzen, Pakete)
    /// bleibt Sache von GameServer. Tote Spieler lösen keine Portale aus.</summary>
    public PortalDefinition? CheckPortal(Entity entity, long nowMs)
    {
        if (entity.Kind != EntityKind.Player || entity.IsDead) return null;
        if (nowMs < entity.PortalReadyAtMs) return null;

        foreach (PortalDefinition portal in Portals)
        {
            if (!portal.Contains(entity.IntX, entity.IntY)) continue;
            entity.PortalReadyAtMs = nowMs + PortalCooldownMs;
            return portal;
        }
        return null;
    }

    public bool TryGet(int entityId, out Entity? entity)
    {
        return _entities.TryGetValue(entityId, out entity);
    }

    public bool IsBlockedAtWire(int wireX, int wireY)
    {
        return _collisionMap != null && _collisionMap.IsBlockedAtWire(wireX, wireY);
    }

    /// <summary>Setzt ein Bewegungsziel, zuerst auf die Kartengrenzen geklemmt, danach (falls eine
    /// CollisionMap gesetzt ist) gegen sie geprüft: die gerade Linie von der aktuellen Position zum
    /// geklemmten Ziel wird zellenweise abgeschritten (ClipToLastWalkableCell), die Bewegung stoppt an der
    /// letzten begehbaren Zelle vor einer blockierten.</summary>
    public bool SetDestination(int entityId, int targetX, int targetY)
    {
        if (!_entities.TryGetValue(entityId, out Entity? entity)) return false;
        if (entity.IsDead) return false; // Tote laufen nicht bis zum Respawn.

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

    /// <summary>Serverautoritative Angriffsprüfung + -ausführung (M4a, seit M4c auch für Monster als
    /// Angreifer verallgemeinert - siehe docs/tasks/M4c-monster-aggro.md Abschnitt 0a) - kein Netzwerkbezug,
    /// siehe GameServer.HandleAttackRequest (Spieler-initiiert) bzw. die Monster-KI in Tick
    /// (Monster-initiiert) für die jeweilige Netzwerk-/Aufrufer-Seite. Prüft in dieser Reihenfolge (siehe
    /// docs/tasks/M4a-combat-core.md Abschnitt 4): Ziel existiert, Ziel lebt, Angreifer und Ziel sind
    /// unterschiedlichen Kinds (blockt Spieler-vs-Spieler UND Monster-vs-Monster - Letzteres löst mein
    /// eigener KI-Code nie aus, aber konzeptionell konsistent; ersetzt den ursprünglichen "Ziel ist kein
    /// Spieler"-Check aus M4a, der implizit "Angreifer ist immer ein Spieler" annahm), Cooldown des
    /// Angreifers, Reichweite (quadrierte Distanz wie ComputeVisibility, gegen MeleeRangeWire des Angreifers -
    /// bei Spielern aus ClassCatalog, bei Monstern seit M4c aus MobCatalog statt hartkodiert 0). Setzt danach
    /// den (ggf. per Hast verkürzten, M6b siehe EffectiveAttackCooldownMs) Cooldown und delegiert die
    /// eigentliche Treffer-Auflösung an ResolveHit (M6b-Extraktion, siehe docs/tasks/M6b-skills.md
    /// Abschnitt 0a - Multiplikator 100 = normaler Angriff, unverändertes Verhalten).
    /// attackerEntityId muss bereits als existierende Entity geprüft sein (bei Spieler-initiierten Angriffen
    /// die per Verbindung gehaltene eigene Entity, siehe GameServer.HandleAttackRequest) - der defensive
    /// TargetNotFound-Fallback hier sollte nie greifen.</summary>
    public AttackResult TryAttack(int attackerEntityId, int targetEntityId, long nowMs)
    {
        if (!_entities.TryGetValue(attackerEntityId, out Entity? attacker))
            return AttackResult.Fail(AttackFailedReason.TargetNotFound);

        if (attacker.IsDead)
            return AttackResult.Fail(AttackFailedReason.AttackerDead);

        if (!_entities.TryGetValue(targetEntityId, out Entity? target))
            return AttackResult.Fail(AttackFailedReason.TargetNotFound);

        if (target.IsDead)
            return AttackResult.Fail(AttackFailedReason.TargetAlreadyDead);

        // M5b: NPCs sind unangreifbar/statisch (siehe docs/tasks/M5b-shop.md Abschnitt 0a) - vor dem
        // Gleiche-Kinds-Check, weil der ("attacker.Kind == target.Kind") einen Player-vs-Npc-Angriff sonst
        // NICHT blocken würde (unterschiedliche Kinds).
        if (!IsCombatKind(target.Kind))
            return AttackResult.Fail(AttackFailedReason.TargetNotAttackable);

        if (attacker.Kind == target.Kind)
            return AttackResult.Fail(AttackFailedReason.TargetIsPlayer);

        if (nowMs < attacker.AttackReadyAtMs)
            return AttackResult.Fail(AttackFailedReason.OnCooldown);

        long dx = attacker.IntX - target.IntX;
        long dy = attacker.IntY - target.IntY;
        long rangeSq = (long)attacker.MeleeRangeWire * attacker.MeleeRangeWire;
        if (dx * dx + dy * dy > rangeSq)
            return AttackResult.Fail(AttackFailedReason.OutOfRange);

        attacker.AttackReadyAtMs = nowMs + EffectiveAttackCooldownMs(attacker);

        return ResolveHit(attacker, target, damageMultiplierPercent: 100, nowMs);
    }

    /// <summary>Nur Spieler und Monster nehmen am Kampf teil - NPCs und Boden-Drops (GroundItem) sind nie
    /// angreifbar, weder per normalem Angriff noch per Wuchtschlag/Kreisschlag.</summary>
    private static bool IsCombatKind(EntityKind kind) => kind == EntityKind.Player || kind == EntityKind.Monster;

    /// <summary>M6b: normaler Angriffs-Cooldown, ggf. durch einen aktiven Hast-Buff verkürzt (siehe
    /// docs/tasks/M6b-skills.md Abschnitt 0d) - effektiver Cooldown = Basis * (100 - CooldownReductionPercent)
    /// / 100, sonst unverändert. Betrifft NUR den normalen Angriffs-Cooldown (AttackReadyAtMs), nicht die
    /// skill-eigenen Cooldowns (siehe TryUseSkill - die haben keinen Hast-Bezug).</summary>
    private static int EffectiveAttackCooldownMs(Entity attacker)
    {
        if (attacker.ActiveBuffs.TryGetValue(BuffType.AttackSpeedBuff, out (int Strength, long ExpiresAtMs) hast))
            return attacker.AttackCooldownMs * (100 - hast.Strength) / 100;
        return attacker.AttackCooldownMs;
    }

    /// <summary>M6b-Extraktion aus TryAttack (siehe docs/tasks/M6b-skills.md Abschnitt 0a) - reine
    /// Treffer-Auflösung OHNE die Existenz-/Kind-/Cooldown-/Reichweiten-Vorprüfungen und OHNE
    /// Cooldown-Verbrauch (beides bleibt Sache des jeweiligen Aufrufers: TryAttack für normale Angriffe,
    /// TryUseSkill für Wuchtschlag/Kreisschlag, siehe dort). Reaktive Zielerfassung (M4c), Ausweichwurf (M6a),
    /// Schaden (skaliert mit damageMultiplierPercent - bei 100 mathematisch identisch zum bisherigen
    /// Math.Max(1, attacker.Attack - target.Defense), damit bleiben alle bestehenden TryAttack-Tests
    /// unverändert gültig), Tod-Behandlung inkl. XP/Yang/Drops (M6a/M5a) - alles unverändert wie vorher in
    /// TryAttack, nur ausgelagert.</summary>
    private AttackResult ResolveHit(Entity attacker, Entity target, int damageMultiplierPercent, long nowMs)
    {
        // Reaktive Zielerfassung (M4c): ein getroffenes Monster ohne eigenes Ziel merkt sich den Angreifer -
        // gilt für aggressive UND passive Mobs gleichermaßen (ein Wildhund, der von hinten angegriffen wird,
        // dreht sich natürlich auch um). target.Kind == Monster schließt die umgekehrte Richtung (Spieler
        // wird von einem Monster getroffen) hier automatisch aus, kein Sonderfall nötig. Läuft bewusst VOR
        // dem Ausweichwurf (M6a) - ein ausgewichener Treffer ist immer noch ein Angriff, der den Verteidiger
        // aufmerksam macht (siehe docs/tasks/M6a-progression.md Abschnitt 0e).
        if (target.Kind == EntityKind.Monster && target.FightsBack && target.TargetEntityId == null)
            target.TargetEntityId = attacker.Id;

        // M6a: Ausweichwurf NACH reaktiver Zielerfassung (bleibt unverändert, siehe Kommentar oben) - Monster
        // haben DodgeBp 0 (nie neu berechnet, siehe RecomputeDerivedStats), weichen also nie aus. Bei
        // Ausweichen: kein Schaden, kein Yang/XP/Drop, nur das Ziel merkt sich das Kampfereignis (der
        // Angreifer selbst zählt das nicht als "ausgeteilt" - siehe LastCombatAtMs-Kommentar auf Entity).
        if (target.DodgeBp > 0 && _random.Next(10000) < target.DodgeBp)
        {
            target.LastCombatAtMs = nowMs;
            return AttackResult.Dodge(target.Kind);
        }

        int targetMaxHp = target.MaxHp;
        int targetLevel = target.Level;
        int targetBaseXp = target.BaseXp;
        // M6b: Multiplikator wirkt auf die rohe Differenz VOR dem Mindestschaden-Floor (nicht danach) - bei
        // 100 % ist Math.Round(rawDamage * 100 / 100.0) == rawDamage, das Ergebnis bleibt exakt
        // Math.Max(1, rawDamage) wie vor der Extraktion.
        int rawDamage = attacker.Attack - target.Defense;
        int scaledDamage = (int)Math.Round(rawDamage * damageMultiplierPercent / 100.0);
        int damage = Math.Max(1, scaledDamage);
        IReadOnlyList<(int ItemId, int ChancePercent)> targetDrops = target.Drops;
        (int targetHpAfter, bool died, long yangAwarded) = ApplyDamage(target, damage, nowMs);

        // M6d: Lumiis-Wellen. Die Schwellenpruefung passiert NACH dem Schaden, aber VOR der Rueckgabe an
        // GameServer; die neuen Monster landen direkt in dieser World und werden im naechsten
        // UpdateVisibility-Tick als EntitySpawn verteilt.
        if (!died)
            TryTriggerMetinWaves(target, targetMaxHp, targetHpAfter, nowMs);

        attacker.LastCombatAtMs = nowMs;
        // target wurde bei einem tödlichen Treffer an ein Monster ggf. schon aus World entfernt (siehe
        // ApplyDamage) - LastCombatAtMs auf der entfernten Instanz zu setzen ist harmlos (kein Verweis mehr
        // von irgendwo), aber sinnlos bei einem toten Spieler-Ziel ebenfalls unschädlich (Tote regenerieren
        // ohnehin nicht, siehe RunRegeneration).
        target.LastCombatAtMs = nowMs;

        // M5b: gemeinsame Yang-Methode mit MaxYang-Deckel (0c) - Rückgabewert bewusst ignoriert: schlägt die
        // Gutschrift wegen des Deckels fehl, bleibt der Kill trotzdem gültig (gleiche "Haupt-Aktion bleibt
        // gültig, Nebeneffekt geht ggf. verloren"-Linie wie bei vollem Inventar bei Drops, siehe unten).
        if (yangAwarded > 0)
            CreditYang(attacker, yangAwarded);

        // M6a: XP am selben Punkt wie Yang (0c) - nur wenn ein SPIELER ein MONSTER getötet hat (Monster/NPCs
        // sammeln in M6a keine XP). AwardXp ist bei fehlendem ProgressionCatalog/ClassDef (bestehende Tests
        // ohne Fortschrittsbezug) ein No-op und gibt (0, false) zurück.
        long xpAwarded = 0;
        bool leveledUp = false;
        if (died && target.Kind == EntityKind.Monster && attacker.Kind == EntityKind.Player)
            (xpAwarded, leveledUp) = AwardXp(attacker, targetLevel, targetBaseXp);

        // M5a: Drops nach Yang, nur bei totem Monster (target ist nach ApplyDamage evtl. schon aus World
        // entfernt - deshalb targetDrops vorher gesichert statt target.Drops erneut zu lesen). Jeder Eintrag
        // unabhängig gewürfelt (_random.Next(100) in [0,99]: chancePercent=0 kann nie treffen, 100 immer).
        List<int>? droppedItemIds = null;
        bool itemLostToFullInventory = false;
        if (died && target.Kind == EntityKind.Monster && targetDrops.Count > 0)
        {
            int groundDropIndex = 0;
            foreach ((int itemId, int chancePercent) in targetDrops)
            {
                if (_random.Next(100) >= chancePercent)
                    continue;

                if (_useGroundDrops)
                {
                    (int dropX, int dropY) = GroundDropPosition(target.IntX, target.IntY, groundDropIndex++);
                    int? owner = attacker.Kind == EntityKind.Player ? attacker.Id : null;
                    SpawnGroundItem(itemId, dropX, dropY, 0, owner, nowMs);
                    continue;
                }

                if (TryAddItemToInventory(attacker.Id, itemId))
                {
                    droppedItemIds ??= new List<int>();
                    droppedItemIds.Add(itemId);
                }
                else
                {
                    itemLostToFullInventory = true;
                }
            }
        }

        return AttackResult.Hit(damage, targetHpAfter, targetMaxHp, died, target.Kind, yangAwarded,
            xpAwarded, leveledUp, droppedItemIds, itemLostToFullInventory);
    }

    /// <summary>Wendet Schaden auf eine beliebige Entity an und löst bei 0 Leben die passende Tod-Behandlung
    /// aus: Monster werden sofort aus World entfernt (wie ein normaler Despawn) und zahlen eine zufällige
    /// Yang-Spanne aus; hatte das Monster eine SpawnPointId (regulärer Katalog-Spawn, kein Dev-Tools-Debug-
    /// Spawn), wird zusätzlich ein Respawn an genau diesem SpawnPoint terminiert (M4b, siehe
    /// ScheduleMonsterRespawn/Tick). Spieler werden NICHT entfernt (kein Despawn), sondern bekommen
    /// RespawnAtMs gesetzt (siehe Tick) - CurrentHp bleibt bis dahin 0. Von TryAttack nach allen Prüfungen
    /// aufgerufen, zusätzlich internal sichtbar: TryAttack lehnt Spieler-Ziele selbst ab (kein PvP in M4a),
    /// das Sterbe-/Respawn-Verhalten für Spieler ist trotzdem generisch implementiert und muss direkt testbar
    /// sein (siehe WorldTests) - für eine spätere Erweiterung (z. B. Monster, die Spieler angreifen) bereits
    /// vorbereitet.</summary>
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
                if (target.SpawnPointId is int spawnPointId)
                    ScheduleMonsterRespawn(spawnPointId, target, nowMs);
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

    /// <summary>Friert die Werte der gerade gestorbenen Monster-Entity als "Rezept" für ihren Nachfolger ein
    /// (M4b) - Position ist der SpawnPoint selbst (WanderAnchorX/Y, nicht die evtl. verwanderte aktuelle
    /// Position), fällig bei nowMs + RespawnSeconds*1000. Überschreibt einen evtl. vorhandenen Eintrag für
    /// denselben SpawnPoint nicht extra ab - kann laut Klassenkommentar zu PendingMonsterRespawn ohnehin nie
    /// vorkommen (ein SpawnPoint hat nie gleichzeitig eine lebende Entity UND einen ausstehenden Respawn).</summary>
    private void ScheduleMonsterRespawn(int spawnPointId, Entity diedMonster, long nowMs)
    {
        _pendingMonsterRespawns[spawnPointId] = new PendingMonsterRespawn(
            diedMonster.TypeId, diedMonster.Name, diedMonster.WanderAnchorX, diedMonster.WanderAnchorY, diedMonster.MovementSpeed,
            diedMonster.WanderRadiusWire, diedMonster.WanderIntervalMinMs, diedMonster.WanderIntervalMaxMs,
            diedMonster.MaxHp, diedMonster.Attack, diedMonster.Defense, diedMonster.YangMin, diedMonster.YangMax,
            diedMonster.RespawnSeconds, diedMonster.MeleeRangeWire, diedMonster.AttackCooldownMs,
            diedMonster.Aggressive, diedMonster.AggroRangeWire, diedMonster.LeashRangeWire, diedMonster.Drops,
            diedMonster.Level, diedMonster.BaseXp, nowMs + diedMonster.RespawnSeconds * 1000L, diedMonster.FightsBack);
    }

    // ---- M6a: Fortschritt (Level/XP/Werte/SP/Regeneration, kein Netzwerkbezug, siehe GameServer für die
    // Paket-Seite und CLAUDE.md Abschnitt "Fortschritt: Level, XP, Werte") ----

    /// <summary>Berechnet den XP-Gewinn nach einem Monster-Kill (Levelunterschied Spielerlevel minus
    /// Monsterlevel, Prozent-Faktor aus ProgressionCatalog, mindestens 1 XP solange der Faktor &gt; 0),
    /// wendet ihn auf player.Xp an und durchläuft danach so viele Levelaufstiege wie die XP hergeben
    /// (Schleife statt Rekursion, stoppt an MaxLevel - überschüssige Xp verfallen dort, siehe
    /// docs/tasks/M6a-progression.md Abschnitt 1: "Ab MaxLevel gibt es keine XP mehr"). Pro Levelaufstieg:
    /// +StatPointsPerLevel/+SkillPointsPerLevel (aus ClassDef), volle Heilung (HP UND SP),
    /// RecomputeDerivedStats. No-op (gibt (0, false) zurück) ohne ProgressionCatalog/ClassDef (bestehende
    /// Tests ohne Fortschrittsbezug) oder wenn player bereits auf MaxLevel steht.</summary>
    /// <summary>M6d: prueft die HP-Schwellen eines Lumiis-Steins und spawnt fuer jede neu unterschrittene
    /// Welle die konfigurierten Verstaerkungen. Der Maskenwert auf der Entity verhindert Mehrfachausloesung;
    /// die Monster sind normale Mobs ohne SpawnPoint (kein Respawn) und erscheinen ueber den normalen
    /// AOI-Diff. Nicht konfigurierte Steine oder fehlende Kataloge sind ein No-op.</summary>
    private void TryTriggerMetinWaves(Entity target, int targetMaxHp, int targetHpAfter, long nowMs)
    {
        if (_metinCatalog == null || _mobCatalog == null) return;
        if (targetMaxHp <= 0 || targetHpAfter <= 0) return;
        if (!_metinCatalog.TryGet(target.TypeId, out MetinDefinition metin)) return;

        for (int waveIndex = 0; waveIndex < metin.Waves.Count; waveIndex++)
        {
            int bit = 1 << waveIndex;
            if ((target.MetinWavesTriggeredMask & bit) != 0) continue;

            MetinWave wave = metin.Waves[waveIndex];
            if (targetHpAfter * 100L > targetMaxHp * (long)wave.HpPercent) continue;

            target.MetinWavesTriggeredMask |= bit;
            if (!_mobCatalog.TryGet(wave.MobId, out MobDefinition mobDef)) continue;

            for (int i = 0; i < wave.Count; i++)
            {
                (int x, int y) = FindNearbyWalkablePoint(target.IntX, target.IntY, 150, 350);
                Entity waveMob = SpawnMonster(wave.MobId, mobDef.Name, x, y, mobDef.SpeedWire,
                    mobDef.WanderRadiusWire, mobDef.WanderIntervalMinMs, mobDef.WanderIntervalMaxMs, nowMs,
                    mobDef.MaxHp, mobDef.Attack, mobDef.Defense, mobDef.YangMin, mobDef.YangMax,
                    meleeRangeWire: mobDef.MeleeRangeWire, attackCooldownMs: mobDef.AttackCooldownMs,
                    aggressive: mobDef.Aggressive, aggroRangeWire: mobDef.AggroRangeWire, leashRangeWire: mobDef.LeashRangeWire,
                    drops: mobDef.Drops.Select(d => (d.ItemId, d.ChancePercent)).ToList(),
                    level: mobDef.Level, baseXp: mobDef.BaseXp);
                waveMob.FightsBack = mobDef.FightsBack;
            }
        }
    }

    internal (long XpGained, bool LeveledUp) AwardXp(Entity player, int monsterLevel, int monsterBaseXp)
    {
        if (_progressionCatalog == null || player.ClassDef == null) return (0, false);
        if (player.Level >= _progressionCatalog.MaxLevel) return (0, false);

        int percent = _progressionCatalog.PercentForLevelDiff(player.Level - monsterLevel);
        if (percent <= 0) return (0, false);

        long xpGained = Math.Max(1, monsterBaseXp * (long)percent / 100);
        return ApplyXp(player, xpGained);
    }

    /// <summary>M7-Party: verteilt eine bereits berechnete XP-Menge (z. B. die Hälfte des Kill-XP an
    /// Gruppenmitglieder). Dieselbe Levelaufstiegs-/Heil-Logik wie AwardXp, nur ohne Monsterformel.</summary>
    internal (long XpGained, bool LeveledUp) GrantXp(Entity player, long amount)
    {
        if (amount <= 0 || _progressionCatalog == null || player.ClassDef == null) return (0, false);
        if (player.Level >= _progressionCatalog.MaxLevel) return (0, false);
        return ApplyXp(player, amount);
    }

    private (long XpGained, bool LeveledUp) ApplyXp(Entity player, long xpGained)
    {
        player.Xp += xpGained;

        bool leveledUp = false;
        while (player.Level < _progressionCatalog!.MaxLevel && player.Xp >= _progressionCatalog.XpToNextLevel(player.Level))
        {
            player.Xp -= _progressionCatalog.XpToNextLevel(player.Level);
            player.Level++;
            player.UnspentStatPoints += player.ClassDef!.StatPointsPerLevel;
            player.UnspentSkillPoints += player.ClassDef.SkillPointsPerLevel;
            RecomputeDerivedStats(player);
            player.CurrentHp = player.MaxHp;
            player.CurrentSp = player.MaxSp;
            leveledUp = true;
        }

        if (player.Level >= _progressionCatalog.MaxLevel)
            player.Xp = 0;

        return (xpGained, leveledUp);
    }

    /// <summary>Verteilt einen Statuspunkt (M6a, siehe AllocateStatPointRequestPacket/GameServer.
    /// HandleAllocateStatPointRequest) - braucht UnspentStatPoints &gt; 0, sonst false (keine Wirkung,
    /// GameServer antwortet trotzdem mit dem aktuellen, unveränderten CharacterStatsPacket, kein eigenes
    /// Fehlerpaket). Ein HT-Punkt erhöht CurrentHp um dieselbe Differenz wie MaxHp, ein IQ-Punkt CurrentSp
    /// analog - verteilte Punkte heilen also nie über den bisherigen "fehlenden" Betrag hinaus, sie schieben
    /// nur die Obergrenze mit hoch (bei St/Dx ändert sich MaxHp/MaxSp nicht, die Differenz ist dann 0).</summary>
    public bool TryAllocateStatPoint(int entityId, StatKind stat)
    {
        if (!_entities.TryGetValue(entityId, out Entity? entity)) return false;
        if (entity.UnspentStatPoints <= 0) return false;

        switch (stat)
        {
            case StatKind.St: entity.SpentSt++; break;
            case StatKind.Ht: entity.SpentHt++; break;
            case StatKind.Dx: entity.SpentDx++; break;
            case StatKind.Iq: entity.SpentIq++; break;
            default: return false;
        }

        int previousMaxHp = entity.MaxHp;
        int previousMaxSp = entity.MaxSp;
        entity.UnspentStatPoints--;
        RecomputeDerivedStats(entity);
        entity.CurrentHp += entity.MaxHp - previousMaxHp;
        entity.CurrentSp += entity.MaxSp - previousMaxSp;

        return true;
    }

    /// <summary>Dev-Tool (siehe DebugSetLevelRequestPacket/CLAUDE.md "Dev-Tools") - setzt Level = level,
    /// Xp/Spent* zurückgesetzt, Unspent* entsprechend nachgezählt (dient auch als Stat-Reset zum Testen), HP
    /// und SP voll. level muss zwischen 1 und ProgressionCatalog.MaxLevel liegen, sonst false (keine
    /// Wirkung).</summary>
    public bool TryDebugSetLevel(int entityId, int level)
    {
        if (!_entities.TryGetValue(entityId, out Entity? entity) || entity.ClassDef == null) return false;
        if (_progressionCatalog != null && (level < 1 || level > _progressionCatalog.MaxLevel)) return false;
        if (_progressionCatalog == null && level < 1) return false;

        entity.Level = level;
        entity.Xp = 0;
        entity.SpentSt = 0;
        entity.SpentHt = 0;
        entity.SpentDx = 0;
        entity.SpentIq = 0;
        entity.UnspentStatPoints = (level - 1) * entity.ClassDef.StatPointsPerLevel;
        entity.UnspentSkillPoints = (level - 1) * entity.ClassDef.SkillPointsPerLevel;
        RecomputeDerivedStats(entity);
        entity.CurrentHp = entity.MaxHp;
        entity.CurrentSp = entity.MaxSp;

        return true;
    }

    /// <summary>Natürliche Regeneration (M6a): läuft für jeden lebenden Spieler höchstens einmal pro
    /// RegenIntervalMs (dasselbe Fälligkeits-Muster wie NextWanderAtMs bei Monstern), und nur, wenn seit
    /// LastCombatAtMs mindestens RegenDelayMs vergangen sind (Schaden genommen/ausgeteilt/ausgewichen -
    /// siehe TryAttack). Hebt HP/SP um HpRegenPercent/SpRegenPercent Prozent des jeweiligen Maximums an
    /// (mindestens 1 Punkt, gedeckelt am Maximum). Tote regenerieren nicht (werden gar nicht erst geprüft -
    /// ihr CurrentHp bleibt bis zum Respawn unverändert, siehe Tick). Gibt zurück, ob sich CurrentHp/CurrentSp
    /// tatsächlich geändert haben (für GameServer: nur dann lohnt ein neues CharacterStatsPacket).</summary>
    private bool RunRegeneration(Entity entity, long nowMs)
    {
        if (entity.Kind != EntityKind.Player || entity.ClassDef == null) return false;
        if (entity.IsDead) return false;
        if (nowMs < entity.NextRegenAtMs) return false;

        entity.NextRegenAtMs = nowMs + entity.ClassDef.RegenIntervalMs;

        if (nowMs - entity.LastCombatAtMs < entity.ClassDef.RegenDelayMs) return false;

        int hpGain = entity.MaxHp > 0 ? Math.Max(1, entity.MaxHp * entity.ClassDef.HpRegenPercent / 100) : 0;
        int spGain = entity.MaxSp > 0 ? Math.Max(1, entity.MaxSp * entity.ClassDef.SpRegenPercent / 100) : 0;

        int newHp = Math.Min(entity.MaxHp, entity.CurrentHp + hpGain);
        int newSp = Math.Min(entity.MaxSp, entity.CurrentSp + spGain);

        if (newHp == entity.CurrentHp && newSp == entity.CurrentSp) return false;

        entity.CurrentHp = newHp;
        entity.CurrentSp = newSp;
        return true;
    }

    // ---- M6b: Skills (kein Netzwerkbezug, siehe GameServer für die Paket-Seite und CLAUDE.md Abschnitt
    // "Skills") ----

    /// <summary>Löst einen Skill-Einsatz auf (M6b, siehe UseSkillRequestPacket/docs/tasks/M6b-skills.md
    /// Abschnitt 4). Gemeinsame Vorprüfungen für alle Skill-Typen: Skill existiert (SkillCatalog), Charakter
    /// hat ihn gelernt (Level &gt; 0), genug SP, Skill-eigener Cooldown bereit (vier UNABHÄNGIGE Cooldowns
    /// pro Charakter, siehe Entity.SkillCooldownReadyAtMs - getrennt vom normalen Angriffs-Cooldown). Danach
    /// je Typ: SingleStrike braucht ein gültiges Ziel in MeleeRangeWire (wie ein normaler Angriff) und ruft
    /// ResolveHit einmal auf; AreaAttack braucht kein Ziel, findet alle gültigen Ziele (anderes Kind als der
    /// Nutzer, kein NPC, lebendig) innerhalb RadiusWire und ruft ResolveHit für jedes einzeln auf (jedes Ziel
    /// weicht unabhängig aus); AttackBuff/AttackSpeedBuff brauchen kein Ziel und setzen nur den passenden
    /// Buff-Zustand (kein Stapeln - ein erneuter Cast ersetzt den bisherigen Eintrag). Bei jedem Erfolg: SP
    /// abziehen, Skill-Cooldown setzen (siehe ConsumeSkillUse) - auch bei einem ausgewichenen Wuchtschlag,
    /// analog zum normalen Angriffs-Cooldown, der ebenfalls unabhängig vom Ausweichen verbraucht wird.</summary>
    public SkillUseResult TryUseSkill(int userEntityId, int skillId, int targetEntityId, long nowMs)
    {
        if (!_entities.TryGetValue(userEntityId, out Entity? user))
            return SkillUseResult.Fail(UseSkillFailedReason.NotLearned); // sollte nicht vorkommen, siehe GameServer.HandleUseSkillRequest.

        if (user.IsDead)
            return SkillUseResult.Fail(UseSkillFailedReason.UserDead);

        if (_skillCatalog == null || !_skillCatalog.TryGet(skillId, out SkillDefinition skillDef))
            return SkillUseResult.Fail(UseSkillFailedReason.NotLearned);

        if (!user.SkillLevels.TryGetValue(skillId, out int level) || level <= 0)
            return SkillUseResult.Fail(UseSkillFailedReason.NotLearned);

        SkillLevelDefinition levelData = skillDef.LevelData(level);

        if (user.CurrentSp < levelData.SpCost)
            return SkillUseResult.Fail(UseSkillFailedReason.NotEnoughSp);

        if (user.SkillCooldownReadyAtMs.TryGetValue(skillId, out long readyAtMs) && nowMs < readyAtMs)
            return SkillUseResult.Fail(UseSkillFailedReason.OnCooldown);

        return skillDef.Type switch
        {
            SkillType.SingleStrike => TryUseSingleStrike(user, skillDef, levelData, targetEntityId, nowMs),
            SkillType.AreaAttack => TryUseAreaAttack(user, skillDef, levelData, nowMs),
            _ => TryUseBuffSkill(user, skillDef, levelData, nowMs)
        };
    }

    private SkillUseResult TryUseSingleStrike(Entity user, SkillDefinition skillDef, SkillLevelDefinition levelData, int targetEntityId, long nowMs)
    {
        if (targetEntityId == 0)
            return SkillUseResult.Fail(UseSkillFailedReason.TargetRequired);

        if (!_entities.TryGetValue(targetEntityId, out Entity? target))
            return SkillUseResult.Fail(UseSkillFailedReason.TargetNotFound);

        if (target.IsDead)
            return SkillUseResult.Fail(UseSkillFailedReason.TargetAlreadyDead);

        if (!IsCombatKind(target.Kind))
            return SkillUseResult.Fail(UseSkillFailedReason.TargetNotAttackable);

        if (user.Kind == target.Kind)
            return SkillUseResult.Fail(UseSkillFailedReason.TargetIsPlayer);

        long dx = user.IntX - target.IntX;
        long dy = user.IntY - target.IntY;
        long rangeSq = (long)user.MeleeRangeWire * user.MeleeRangeWire;
        if (dx * dx + dy * dy > rangeSq)
            return SkillUseResult.Fail(UseSkillFailedReason.OutOfRange);

        ConsumeSkillUse(user, skillDef.Id, levelData, nowMs);

        AttackResult result = ResolveHit(user, target, levelData.EffectValue, nowMs);
        return SkillUseResult.HitSuccess(skillDef.Type, new List<(int TargetEntityId, AttackResult Result)> { (targetEntityId, result) });
    }

    private SkillUseResult TryUseAreaAttack(Entity user, SkillDefinition skillDef, SkillLevelDefinition levelData, long nowMs)
    {
        ConsumeSkillUse(user, skillDef.Id, levelData, nowMs);

        var hits = new List<(int TargetEntityId, AttackResult Result)>();
        long radiusSq = (long)skillDef.RadiusWire * skillDef.RadiusWire;
        // Kopie, da ResolveHit ein sterbendes Monster aus _entities entfernt (siehe ApplyDamage) - direktes
        // Iterieren über _entities.Values würde dabei die Sammlung während der Iteration verändern.
        foreach (Entity candidate in new List<Entity>(_entities.Values))
        {
            if (candidate.Id == user.Id || candidate.Kind == user.Kind || !IsCombatKind(candidate.Kind) || candidate.IsDead)
                continue;

            long dx = user.IntX - candidate.IntX;
            long dy = user.IntY - candidate.IntY;
            if (dx * dx + dy * dy > radiusSq) continue;

            AttackResult result = ResolveHit(user, candidate, levelData.EffectValue, nowMs);
            hits.Add((candidate.Id, result));
        }

        return SkillUseResult.HitSuccess(skillDef.Type, hits);
    }

    private SkillUseResult TryUseBuffSkill(Entity user, SkillDefinition skillDef, SkillLevelDefinition levelData, long nowMs)
    {
        ConsumeSkillUse(user, skillDef.Id, levelData, nowMs);

        BuffType buffType = skillDef.Type == SkillType.AttackBuff ? BuffType.AttackBuff : BuffType.AttackSpeedBuff;
        // Kein Stapeln - ein erneuter Cast ersetzt den bisherigen Eintrag desselben Typs komplett (neue
        // Wirkungsstärke, neue volle Dauer).
        user.ActiveBuffs[buffType] = (levelData.EffectValue, nowMs + skillDef.DurationMs);
        RecomputeDerivedStats(user); // siehe docs/tasks/M6b-skills.md Abschnitt 4: bei Buff-Start UND -Ende.

        return SkillUseResult.BuffSuccess(skillDef.Type, buffType);
    }

    private static void ConsumeSkillUse(Entity user, int skillId, SkillLevelDefinition levelData, long nowMs)
    {
        user.CurrentSp -= levelData.SpCost;
        user.SkillCooldownReadyAtMs[skillId] = nowMs + levelData.CooldownMs;
    }

    /// <summary>Verteilt einen Fertigkeitspunkt (M6b, siehe AllocateSkillPointRequestPacket/GameServer.
    /// HandleAllocateSkillPointRequest) - braucht UnspentSkillPoints &gt; 0 UND aktuelle Stufe &lt; 17, sonst
    /// false (keine Wirkung, GameServer antwortet trotzdem mit dem aktuellen, unveränderten
    /// CharacterStatsPacket, kein eigenes Fehlerpaket - analog zu TryAllocateStatPoint aus M6a).</summary>
    public bool TryAllocateSkillPoint(int entityId, int skillId)
    {
        if (!_entities.TryGetValue(entityId, out Entity? entity)) return false;
        if (entity.UnspentSkillPoints <= 0) return false;
        if (_skillCatalog == null || !_skillCatalog.TryGet(skillId, out _)) return false;

        int currentLevel = entity.SkillLevels.TryGetValue(skillId, out int level) ? level : 0;
        if (currentLevel >= SkillCatalog.MaxSkillLevel) return false;

        entity.SkillLevels[skillId] = currentLevel + 1;
        entity.UnspentSkillPoints--;
        return true;
    }

    // ---- M5a: Items, Inventar, Ausrüstung (kein Netzwerkbezug, siehe GameServer für die Paket-Seite) ----

    /// <summary>Legt das Item aus inventorySlotIndex an: prüft Slot belegt und dass der ItemType zu einem
    /// Ausrüstungs-Slot passt, tauscht dann Inventarplatz und Ausrüstungs-Slot (bisheriges Ausrüstungsitem
    /// landet zurück im selben Inventarplatz, 0 falls vorher nichts ausgerüstet war) und berechnet
    /// Attack/Defense neu. Seit M6c wandert die Aufwertungsstufe mit dem Item (Inventar &lt;-&gt; Ausrüstung),
    /// damit eine aufgewertete Instanz beim An-/Ablegen ihre Stufe behält.</summary>
    public (bool Success, InventoryActionFailedReason Reason) TryEquip(int entityId, int inventorySlotIndex)
    {
        if (!_entities.TryGetValue(entityId, out Entity? entity)) return (false, InventoryActionFailedReason.InvalidSlot);
        if (inventorySlotIndex < 0 || inventorySlotIndex >= entity.Inventory.Length) return (false, InventoryActionFailedReason.InvalidSlot);

        int itemId = entity.Inventory[inventorySlotIndex];
        if (itemId == 0) return (false, InventoryActionFailedReason.EmptySlot);

        if (_itemCatalog == null || !_itemCatalog.TryGet(itemId, out ItemDefinition item))
            return (false, InventoryActionFailedReason.InvalidSlot); // sollte nicht vorkommen - Items im Inventar stammen immer aus dem Katalog.

        if (!item.IsEquipment)
            return (false, InventoryActionFailedReason.WrongItemType); // Consumable/Aufwertungsmaterial kann nicht angelegt werden.

        EquipmentSlot slot = Entity.SlotForItemType(item.ItemType)!.Value;
        (int previousEquippedItemId, int previousEquippedUpgradeLevel) = entity.GetEquipped(slot);
        int newUpgradeLevel = entity.InventoryUpgradeLevels[inventorySlotIndex];

        entity.SetEquipped(slot, itemId, newUpgradeLevel);
        entity.Inventory[inventorySlotIndex] = previousEquippedItemId;
        entity.InventoryUpgradeLevels[inventorySlotIndex] = previousEquippedItemId == 0 ? 0 : previousEquippedUpgradeLevel;
        RecomputeDerivedStats(entity);
        return (true, default);
    }

    /// <summary>Legt das Item aus equipmentSlot ab: braucht einen freien Inventarplatz (erste freie Stelle),
    /// sonst InventoryFull. Berechnet Attack/Defense neu. Seit M6c wandert die Aufwertungsstufe mit.</summary>
    public (bool Success, InventoryActionFailedReason Reason) TryUnequip(int entityId, EquipmentSlot equipmentSlot)
    {
        if (!_entities.TryGetValue(entityId, out Entity? entity)) return (false, InventoryActionFailedReason.EmptySlot);

        (int currentItemId, int currentUpgradeLevel) = entity.GetEquipped(equipmentSlot);
        if (currentItemId == 0) return (false, InventoryActionFailedReason.EmptySlot);

        int freeSlot = FindFirstFreeInventorySlot(entity);
        if (freeSlot < 0) return (false, InventoryActionFailedReason.InventoryFull);

        entity.SetEquipped(equipmentSlot, 0, 0);
        entity.Inventory[freeSlot] = currentItemId;
        entity.InventoryUpgradeLevels[freeSlot] = currentUpgradeLevel;
        RecomputeDerivedStats(entity);
        return (true, default);
    }

    /// <summary>Benutzt das Item aus inventorySlotIndex: nur wenn ItemType=Consumable, heilt CurrentHp um
    /// HealAmount (gedeckelt bei MaxHp) und entfernt das Item aus dem Slot. TargetAtFullHp, falls CurrentHp
    /// bereits MaxHp ist - nowMs aktuell ungenutzt (keine Cooldown-/Zeitlogik für Consumables in M5a),
    /// trotzdem übergeben für Konsistenz mit TryAttack und für eine spätere Erweiterung (z. B. Cooldown pro
    /// Trank-Typ).</summary>
    public (bool Success, InventoryActionFailedReason Reason) TryUseConsumable(int entityId, int inventorySlotIndex, long nowMs)
    {
        if (!_entities.TryGetValue(entityId, out Entity? entity)) return (false, InventoryActionFailedReason.InvalidSlot);
        if (inventorySlotIndex < 0 || inventorySlotIndex >= entity.Inventory.Length) return (false, InventoryActionFailedReason.InvalidSlot);

        int itemId = entity.Inventory[inventorySlotIndex];
        if (itemId == 0) return (false, InventoryActionFailedReason.EmptySlot);

        if (_itemCatalog == null || !_itemCatalog.TryGet(itemId, out ItemDefinition item))
            return (false, InventoryActionFailedReason.InvalidSlot);

        if (item.ItemType != ItemType.Consumable) return (false, InventoryActionFailedReason.NotConsumable);
        if (entity.CurrentHp >= entity.MaxHp) return (false, InventoryActionFailedReason.TargetAtFullHp);

        entity.CurrentHp = Math.Min(entity.MaxHp, entity.CurrentHp + item.HealAmount);
        entity.Inventory[inventorySlotIndex] = 0;
        entity.InventoryUpgradeLevels[inventorySlotIndex] = 0;
        return (true, default);
    }

    /// <summary>Legt itemId auf die erste freie Inventarstelle. Gibt false zurück, wenn kein Platz frei ist
    /// (Inventar voll) - der Aufrufer entscheidet, wie er das meldet (siehe GameServer.HandleXxx bzw.
    /// TryAttack für Monster-Drops). upgradeLevel (M6c, Default 0) ist die Stufe der neuen Item-Instanz.</summary>
    public bool TryAddItemToInventory(int entityId, int itemId, int upgradeLevel = 0)
    {
        if (!_entities.TryGetValue(entityId, out Entity? entity)) return false;

        int freeSlot = FindFirstFreeInventorySlot(entity);
        if (freeSlot < 0) return false;

        entity.Inventory[freeSlot] = itemId;
        entity.InventoryUpgradeLevels[freeSlot] = upgradeLevel;
        return true;
    }

    private static int FindFirstFreeInventorySlot(Entity entity)
    {
        for (int i = 0; i < entity.Inventory.Length; i++)
        {
            if (entity.Inventory[i] == 0) return i;
        }
        return -1;
    }

    /// <summary>M6c: Wertet die angelegte Ausrüstung im genannten Slot auf. Reihenfolge ist bewusst streng
    /// (siehe docs/tasks/M6c-upgrade.md Abschnitt 4): ALLE Vorprüfungen (Slot belegt, nicht Maximalstufe,
    /// Schleifstein vorhanden, ggf. Bewahrungsrolle vorhanden) laufen VOR jedem Verbrauch. Erst danach wird
    /// gewürfelt (_random.Next(100), injizierbar für Tests) und der Schleifstein (immer) sowie die
    /// Bewahrungsrolle (falls angefordert, immer) entfernt. Erfolg: Stufe +1 und RecomputeDerivedStats.
    /// Fehlschlag MIT Rolle: Item/Stufe unverändert. Fehlschlag OHNE Rolle: Ausrüstungs-Slot wird komplett
    /// geleert (Item zerstört). nowMs ist aktuell ungenutzt (keine Cooldown-/Zeitlogik), aber aus
    /// Konsistenz mit den anderen World-Aktionen im Signaturvertrag.</summary>
    public UpgradeAttemptResult TryUpgrade(int entityId, EquipmentSlot equipmentSlot, bool useProtectionScroll, long nowMs)
    {
        if (_upgradeCatalog == null || _itemCatalog == null)
            return UpgradeAttemptResult.Reject(UpgradeActionFailedReason.NoMaterial);

        if (!_entities.TryGetValue(entityId, out Entity? entity) || entity.Kind != EntityKind.Player)
            return UpgradeAttemptResult.Reject(UpgradeActionFailedReason.SlotEmpty);

        if (_requireBlacksmithForUpgrade)
        {
            bool nearBlacksmith = false;
            foreach (Entity npc in _entities.Values)
            {
                if (npc.Kind != EntityKind.Npc || npc.Service != NpcService.Blacksmith) continue;
                long dx = entity.IntX - npc.IntX;
                long dy = entity.IntY - npc.IntY;
                if (dx * dx + dy * dy <= (long)GameConstants.NpcInteractRangeWire * GameConstants.NpcInteractRangeWire)
                {
                    nearBlacksmith = true;
                    break;
                }
            }
            if (!nearBlacksmith) return UpgradeAttemptResult.Reject(UpgradeActionFailedReason.NoBlacksmith);
        }

        (int equippedItemId, int currentUpgradeLevel) = entity.GetEquipped(equipmentSlot);
        if (equippedItemId == 0)
            return UpgradeAttemptResult.Reject(UpgradeActionFailedReason.SlotEmpty);
        if (currentUpgradeLevel >= _upgradeCatalog.MaxUpgradeLevel)
            return UpgradeAttemptResult.Reject(UpgradeActionFailedReason.AlreadyMaxLevel);

        int whetstoneSlot = FindInventorySlotWithItem(entity, _upgradeCatalog.WhetstoneItemId);
        if (whetstoneSlot < 0)
            return UpgradeAttemptResult.Reject(UpgradeActionFailedReason.NoMaterial);

        int protectionScrollSlot = -1;
        if (useProtectionScroll)
        {
            protectionScrollSlot = FindInventorySlotWithItem(entity, _upgradeCatalog.ProtectionScrollItemId);
            if (protectionScrollSlot < 0)
                return UpgradeAttemptResult.Reject(UpgradeActionFailedReason.NoProtectionScroll);
        }

        // Ab hier ist der Versuch unwiderruflich: Material wird IMMER verbraucht, unabhängig vom Ausgang.
        int successChancePercent = _upgradeCatalog.GetSuccessChancePercent(currentUpgradeLevel);
        bool success = _random.Next(100) < successChancePercent;

        entity.Inventory[whetstoneSlot] = 0;
        entity.InventoryUpgradeLevels[whetstoneSlot] = 0;
        if (protectionScrollSlot >= 0)
        {
            entity.Inventory[protectionScrollSlot] = 0;
            entity.InventoryUpgradeLevels[protectionScrollSlot] = 0;
        }

        if (success)
        {
            int newLevel = currentUpgradeLevel + 1;
            entity.SetEquipped(equipmentSlot, equippedItemId, newLevel);
            RecomputeDerivedStats(entity);
            return UpgradeAttemptResult.Succeeded(newLevel);
        }

        if (useProtectionScroll)
            return UpgradeAttemptResult.FailedWithProtection(currentUpgradeLevel);

        entity.SetEquipped(equipmentSlot, 0, 0);
        RecomputeDerivedStats(entity);
        return UpgradeAttemptResult.DestroyedItem();
    }

    private static int FindInventorySlotWithItem(Entity entity, int itemId)
    {
        for (int i = 0; i < entity.Inventory.Length; i++)
        {
            if (entity.Inventory[i] == itemId) return i;
        }
        return -1;
    }

    /// <summary>M6a: einzige Stelle, die MaxHp/MaxSp/Attack/Defense/DodgeBp berechnet (ersetzt das bisherige
    /// M5a-RecalculateCombatStats, das nur Attack/Defense aus Ausrüstungs-Boni kannte) - siehe
    /// docs/tasks/M6a-progression.md Abschnitt 4. Aufruf bei Join (siehe TryJoin), Levelaufstieg
    /// (AwardXp), Punkteverteilung (TryAllocateStatPoint) und Equip/Unequip (ApplyInventoryAndAppearanceChange
    /// über GameServer) - bei reinen Ausrüstungsänderungen ändern sich MaxHp/MaxSp/DodgeBp dabei nicht
    /// (Level/Werte bleiben gleich), nur Attack/Defense (Formel-Basiswert ist ohnehin gleich, nur der
    /// Ausrüstungs-Bonus variiert) - "Equip/Unequip verändern nur Attack/Defense" ergibt sich so von selbst,
    /// ohne Sonderfall. Bei Kind != Player ein No-op. Ohne ClassDef (siehe Entity.ClassDef-Kommentar, z. B.
    /// bestehende Tests ohne Fortschrittsbezug) bleiben MaxHp/MaxSp/DodgeBp/BaseAttack/BaseDefense bei ihrem
    /// bisherigen Stand (Konstruktor-Default) - NUR der Ausrüstungs-Bonus auf Attack/Defense wird trotzdem
    /// angewendet, damit Equip/Unequip für diese Tests unverändert wie vor M6a funktioniert (genau das
    /// bisherige RecalculateCombatStats-Verhalten). Öffentlich, damit GameServer sie auch direkt nach dem
    /// Join aufrufen kann (siehe CompleteJoin).</summary>
    public void RecomputeDerivedStats(int entityId)
    {
        if (_entities.TryGetValue(entityId, out Entity? entity))
            RecomputeDerivedStats(entity);
    }

    internal void RecomputeDerivedStats(Entity entity)
    {
        if (entity.Kind != EntityKind.Player) return;

        if (entity.ClassDef != null)
        {
            ClassDefinition classDef = entity.ClassDef;
            entity.MaxHp = classDef.BaseHp + entity.Ht * classDef.HpPerHt + (entity.Level - 1) * classDef.HpPerLevel;
            entity.MaxSp = classDef.BaseSp + entity.Iq * classDef.SpPerIq + (entity.Level - 1) * classDef.SpPerLevel;
            entity.BaseAttack = classDef.BaseAttack + entity.St * classDef.AttackPerSt;
            entity.BaseDefense = classDef.BaseDefense + entity.Ht / classDef.HtPerDefense;
            entity.DodgeBp = Math.Min(classDef.DodgeCapBp, entity.Dx * classDef.DodgeBpPerDx);
        }

        int attackBonus = 0;
        int defenseBonus = 0;
        if (_itemCatalog != null)
        {
            (int ItemId, int UpgradeLevel)[] equipped =
            {
                (entity.EquippedArmorItemId, entity.EquippedArmorUpgradeLevel),
                (entity.EquippedHelmetItemId, entity.EquippedHelmetUpgradeLevel),
                (entity.EquippedWeaponItemId, entity.EquippedWeaponUpgradeLevel),
                (entity.EquippedShieldItemId, entity.EquippedShieldUpgradeLevel)
            };
            foreach ((int equippedItemId, int upgradeLevel) in equipped)
            {
                if (equippedItemId == 0) continue;
                if (_itemCatalog.TryGet(equippedItemId, out ItemDefinition item))
                {
                    int upgradeBonus = item.UpgradeBonusPerLevel * Math.Clamp(upgradeLevel, 0, UpgradeCatalog.SupportedMaxUpgradeLevel);
                    if (item.ItemType == ItemType.Weapon)
                        attackBonus += item.AttackBonus + upgradeBonus;
                    else
                        defenseBonus += item.DefenseBonus + upgradeBonus;
                }
            }
        }

        int attackAfterEquipment = entity.BaseAttack + attackBonus;

        // M6b: Kampfrausch NACH der bisherigen Attack-Berechnung (auf den bereits ausrüstungsverstärkten
        // Wert, siehe docs/tasks/M6b-skills.md Abschnitt 0c) - ändert nichts an den Formelzeilen davor, die
        // Level-1-Krieger-Regressionstests (ohne aktiven Buff) bleiben unberührt. Gerundet (nicht
        // abgeschnitten), siehe Abschnitt 4.
        if (entity.ActiveBuffs.TryGetValue(BuffType.AttackBuff, out (int Strength, long ExpiresAtMs) rage))
            attackAfterEquipment += (int)Math.Round(attackAfterEquipment * rage.Strength / 100.0);

        int defenseAfterEquipment = entity.BaseDefense + defenseBonus;
        // Gruppenrolle (data/party.json): zuletzt, auf den bereits verstärkten Wert, gerundet wie Kampfrausch.
        if (entity.PartyAttackBonusPercent > 0)
            attackAfterEquipment += (int)Math.Round(attackAfterEquipment * entity.PartyAttackBonusPercent / 100.0);
        if (entity.PartyDefenseBonusPercent > 0)
            defenseAfterEquipment += (int)Math.Round(defenseAfterEquipment * entity.PartyDefenseBonusPercent / 100.0);

        entity.Attack = attackAfterEquipment;
        entity.Defense = defenseAfterEquipment;
    }

    // ---- M5b: Shop/Yang (kein Netzwerkbezug, siehe GameServer für die Paket-Seite) ----

    /// <summary>Einzige Stelle im Projekt, die Entity.Yang erhöht (Monster-Drops via TryAttack, Verkauf via
    /// TrySell, Debug-Tools via HandleDebugGiveYangRequest, siehe docs/tasks/M5b-shop.md Abschnitt 0c) -
    /// deckelt bei GameConstants.MaxYang. amount &lt;= 0 ist ein No-op (gilt als Erfolg, nichts zu tun). Gibt
    /// false zurück, wenn amount &gt; 0 den Deckel überschreiten würde - dann bleibt Yang UNVERÄNDERT (kein
    /// Teilbetrag), der Aufrufer entscheidet, wie er das behandelt (TrySell bricht komplett ab, TryAttack
    /// lässt den Kill trotzdem gültig).</summary>
    public bool CreditYang(int entityId, long amount)
    {
        return _entities.TryGetValue(entityId, out Entity? entity) && CreditYang(entity, amount);
    }

    private static bool CreditYang(Entity entity, long amount)
    {
        if (amount <= 0) return true;
        if (entity.Yang + amount > GameConstants.MaxYang) return false;

        entity.Yang += amount;
        return true;
    }

    /// <summary>Prüft, ob playerEntityId gerade mit npcEntityId Handel treiben darf: NPC existiert und ist
    /// Kind=Npc, hat ein Sortiment (ShopItemIds nicht leer), Distanz &lt;= NpcInteractRangeWire (quadrierte
    /// Distanz wie TryAttack/AOI). Gemeinsame Vorprüfung für TryOpenShop/TryBuy/TrySell - JEDE Shop-Aktion
    /// prüft das erneut (der Spieler könnte sich seit dem letzten Request wegbewegt haben, siehe
    /// docs/tasks/M5b-shop.md Abschnitt 4).</summary>
    private (bool Success, ShopActionFailedReason Reason) CheckShopAccess(int playerEntityId, int npcEntityId)
    {
        if (!_entities.TryGetValue(playerEntityId, out Entity? player))
            return (false, ShopActionFailedReason.NpcNotFound); // sollte nicht vorkommen, analog zu TryAttack.

        if (!_entities.TryGetValue(npcEntityId, out Entity? npc) || npc.Kind != EntityKind.Npc)
            return (false, ShopActionFailedReason.NpcNotFound);

        if (npc.ShopItemIds.Count == 0)
            return (false, ShopActionFailedReason.NotAShop);

        long dx = player.IntX - npc.IntX;
        long dy = player.IntY - npc.IntY;
        long rangeSq = (long)GameConstants.NpcInteractRangeWire * GameConstants.NpcInteractRangeWire;
        if (dx * dx + dy * dy > rangeSq)
            return (false, ShopActionFailedReason.OutOfRange);

        return (true, default);
    }

    public (bool Success, ShopActionFailedReason Reason) TryOpenShop(int playerEntityId, int npcEntityId)
    {
        return CheckShopAccess(playerEntityId, npcEntityId);
    }

    /// <summary>Kauft itemId beim NPC: NPC/Reichweite (erneut, siehe CheckShopAccess), Item im Sortiment,
    /// genug Yang, freier Inventarplatz. Erfolg: Yang abziehen, Item über TryAddItemToInventory einfügen
    /// (Platz gerade erst geprüft, kann dort nicht mehr fehlschlagen).</summary>
    public (bool Success, ShopActionFailedReason Reason) TryBuy(int playerEntityId, int npcEntityId, int itemId)
    {
        (bool accessOk, ShopActionFailedReason accessReason) = CheckShopAccess(playerEntityId, npcEntityId);
        if (!accessOk) return (false, accessReason);

        Entity player = _entities[playerEntityId];
        Entity npc = _entities[npcEntityId];

        bool inStock = false;
        foreach (int shopItemId in npc.ShopItemIds)
        {
            if (shopItemId == itemId) { inStock = true; break; }
        }
        if (!inStock)
            return (false, ShopActionFailedReason.ItemNotInStock);

        if (_itemCatalog == null || !_itemCatalog.TryGet(itemId, out ItemDefinition item))
            return (false, ShopActionFailedReason.ItemNotInStock); // sollte nicht vorkommen - NpcCatalog validiert das beim Laden.

        if (player.Yang < item.BuyPrice)
            return (false, ShopActionFailedReason.NotEnoughYang);

        if (FindFirstFreeInventorySlot(player) < 0)
            return (false, ShopActionFailedReason.InventoryFull);

        player.Yang -= item.BuyPrice;
        TryAddItemToInventory(playerEntityId, itemId);
        return (true, default);
    }

    /// <summary>Verkauft das Item aus inventorySlotIndex an den NPC: NPC/Reichweite, Slot gültig und belegt,
    /// SellPrice &gt; 0. Erfolg: Slot leeren, Yang gutschreiben über CreditYang (MaxYang-Deckel) - bei
    /// Überschreitung Abbruch mit YangCapReached, NICHTS wird verändert (Slot bleibt belegt, kein
    /// Teilverkauf).</summary>
    public (bool Success, ShopActionFailedReason Reason) TrySell(int playerEntityId, int npcEntityId, int inventorySlotIndex)
    {
        (bool accessOk, ShopActionFailedReason accessReason) = CheckShopAccess(playerEntityId, npcEntityId);
        if (!accessOk) return (false, accessReason);

        Entity player = _entities[playerEntityId];

        if (inventorySlotIndex < 0 || inventorySlotIndex >= player.Inventory.Length)
            return (false, ShopActionFailedReason.InvalidSlot);

        int itemId = player.Inventory[inventorySlotIndex];
        if (itemId == 0)
            return (false, ShopActionFailedReason.EmptySlot);

        if (_itemCatalog == null || !_itemCatalog.TryGet(itemId, out ItemDefinition item))
            return (false, ShopActionFailedReason.ItemNotSellable); // sollte nicht vorkommen - Items im Inventar stammen immer aus dem Katalog.

        if (item.SellPrice <= 0)
            return (false, ShopActionFailedReason.ItemNotSellable);

        if (!CreditYang(playerEntityId, item.SellPrice))
            return (false, ShopActionFailedReason.YangCapReached);

        player.Inventory[inventorySlotIndex] = 0;
        player.InventoryUpgradeLevels[inventorySlotIndex] = 0;
        return (true, default);
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

    /// <summary>Schreibt alle Bewegungen um deltaSeconds fort (Spieler-MoveTo UND laufende Monster-Bewegung,
    /// derselbe Mechanismus). Läuft danach die Monster-KI für jedes Monster (M3c Wander plus M4c
    /// Zielerfassung/Verfolgung/Leine/Angriff, siehe RunMonsterAi - ein Monster ist entweder am Wandern ODER
    /// verfolgt/bekämpft ein Ziel, nie beides gleichzeitig), dann fällige Spieler-Respawns (M4a, siehe
    /// ApplyDamage): Position auf Kartenmitte, CurrentHp auf MaxHp, EntityKind/Appearance unverändert. Zuletzt
    /// fällige Monster-Respawns (M4b, siehe ScheduleMonsterRespawn): erzeugt über den normalen SpawnMonster-
    /// Weg eine NEUE Entity mit neuer EntityId (anders als beim Spieler - die alte wurde beim Tod schon
    /// entfernt, der Client hat längst EntityDespawn bekommen), räumt den SpawnPoint danach wieder frei. Gibt
    /// fünf Listen zurück: Arrived (Entities, die in diesem Tick exakt am Ziel angekommen sind - für
    /// EntityStop), StartedWandering (trotz des aus M3c stammenden Namens seit M4c für JEDE neue/fortgesetzte
    /// KI-gesteuerte Bewegung eines Monsters genutzt - Wander-Start, Leine-Rückkehr-Start UND laufende
    /// Zielverfolgung, siehe RunMonsterAi - für EntityMove, analog zu HandleMoveTo bei Spielern, die aber über
    /// ein echtes MoveToPacket ausgelöst werden statt über den Tick), Respawned (Spieler, die in diesem Tick
    /// respawnt sind - ebenfalls für EntityStop an der neuen Position, siehe GameServer), RespawnedMonsters
    /// (neu erschienene Monster - kein Sonderfall nötig, der normale AOI-Diff-Mechanismus erkennt sie als "neu
    /// sichtbar" und verschickt EntitySpawn von selbst, siehe GameServer.UpdateVisibility; die Liste dient
    /// GameServer nur zum Loggen), MonsterAttacks (M4c: erfolgreiche Monster-initiierte Angriffe gegen
    /// Spieler - AttackerEntityId/TargetEntityId/Result, damit GameServer dieselben Pakete verschicken kann
    /// wie bei einem spielerinitiierten Angriff, siehe GameServer.BroadcastAttackResult) und RegeneratedPlayers
    /// (M6a: Spieler, deren HP/SP sich in diesem Tick durch natürliche Regeneration tatsächlich geändert
    /// haben, siehe RunRegeneration - GameServer verschickt dafür ein neues CharacterStatsPacket).</summary>
    public (List<Entity> Arrived, List<Entity> StartedWandering, List<Entity> Respawned, List<Entity> RespawnedMonsters,
        List<(int AttackerEntityId, int TargetEntityId, AttackResult Result)> MonsterAttacks, List<Entity> RegeneratedPlayers,
        List<Entity> BuffsExpired) Tick(double deltaSeconds, long nowMs)
    {
        List<Entity> arrived = new();
        foreach (Entity entity in _entities.Values)
        {
            if (entity.Tick(deltaSeconds))
                arrived.Add(entity);
        }

        List<Entity> startedWandering = new();
        List<(int AttackerEntityId, int TargetEntityId, AttackResult Result)> monsterAttacks = new();
        foreach (Entity entity in _entities.Values)
        {
            if (entity.Kind != EntityKind.Monster) continue;
            RunMonsterAi(entity, nowMs, startedWandering, monsterAttacks);
        }

        List<Entity> respawned = new();
        foreach (Entity entity in _entities.Values)
        {
            if (entity.Kind != EntityKind.Player) continue;
            if (entity.RespawnAtMs == 0 || nowMs < entity.RespawnAtMs) continue;

            entity.Teleport(StartX, StartY);
            entity.CurrentHp = entity.MaxHp;
            entity.CurrentSp = entity.MaxSp; // M6a: Respawn heilt HP UND SP voll.
            entity.RespawnAtMs = 0;
            respawned.Add(entity);
        }

        List<Entity> respawnedMonsters = new();
        if (_pendingMonsterRespawns.Count > 0)
        {
            List<int> dueSpawnPointIds = new();
            foreach (KeyValuePair<int, PendingMonsterRespawn> kvp in _pendingMonsterRespawns)
            {
                if (nowMs >= kvp.Value.DueAtMs) dueSpawnPointIds.Add(kvp.Key);
            }

            foreach (int spawnPointId in dueSpawnPointIds)
            {
                PendingMonsterRespawn pending = _pendingMonsterRespawns[spawnPointId];
                _pendingMonsterRespawns.Remove(spawnPointId);

                Entity monster = SpawnMonster(pending.MobId, pending.Name, pending.PositionX, pending.PositionY, pending.SpeedWire,
                    pending.WanderRadiusWire, pending.WanderIntervalMinMs, pending.WanderIntervalMaxMs, nowMs,
                    pending.MaxHp, pending.Attack, pending.Defense, pending.YangMin, pending.YangMax,
                    spawnPointId: spawnPointId, respawnSeconds: pending.RespawnSeconds,
                    meleeRangeWire: pending.MeleeRangeWire, attackCooldownMs: pending.AttackCooldownMs,
                    aggressive: pending.Aggressive, aggroRangeWire: pending.AggroRangeWire, leashRangeWire: pending.LeashRangeWire,
                    drops: pending.Drops, level: pending.Level, baseXp: pending.BaseXp);
                monster.FightsBack = pending.FightsBack;
                respawnedMonsters.Add(monster);
            }
        }

        List<Entity> regeneratedPlayers = new();
        foreach (Entity entity in _entities.Values)
        {
            if (RunRegeneration(entity, nowMs))
                regeneratedPlayers.Add(entity);
        }

        // M6b: abgelaufene Buffs entfernen (Skill-Start meldet sich selbst über den TryUseSkill-Rückgabewert,
        // Ablauf braucht dafür diese Tick-getriebene Liste, analog zu RegeneratedPlayers). Buffs überleben
        // keinen Disconnect (siehe Entity.ActiveBuffs-Kommentar) - hier geht es nur um natürliches Ablaufen
        // bei weiterhin verbundenen Spielern.
        List<Entity> buffsExpired = new();
        foreach (Entity entity in _entities.Values)
        {
            if (entity.ActiveBuffs.Count == 0) continue;

            List<BuffType>? expired = null;
            foreach (KeyValuePair<BuffType, (int Strength, long ExpiresAtMs)> kvp in entity.ActiveBuffs)
            {
                if (nowMs >= kvp.Value.ExpiresAtMs)
                {
                    expired ??= new List<BuffType>();
                    expired.Add(kvp.Key);
                }
            }
            if (expired == null) continue;

            foreach (BuffType buffType in expired)
                entity.ActiveBuffs.Remove(buffType);

            RecomputeDerivedStats(entity);
            buffsExpired.Add(entity);
        }

        return (arrived, startedWandering, respawned, respawnedMonsters, monsterAttacks, regeneratedPlayers, buffsExpired);
    }

    /// <summary>Monster-KI für ein einzelnes Monster (M3c Wander, M4c Zielerfassung/Verfolgung/Leine/Angriff).
    /// Ohne aktuelles Ziel: aggressive Mobs suchen den nächstgelegenen lebenden Spieler innerhalb
    /// AggroRangeWire (quadrierte Distanz wie AOI/TryAttack) und setzen ihn bei Fund als Ziel; passive Mobs
    /// erfassen ihr Ziel ausschließlich reaktiv nach einem Treffer (siehe TryAttack) - hier passiert für sie
    /// nichts. Ohne Ziel (weiterhin) läuft die normale Wander-Fälligkeitsprüfung wie bisher. MIT Ziel: zuerst
    /// prüfen, ob der Zielspieler noch existiert/lebt (sonst Ziel fallen lassen, zurück zum Wander-Zustand -
    /// kein SetDestination hier, die nächste reguläre Wander-Fälligkeit übernimmt), dann die Leine (Distanz
    /// vom Anker zum AKTUELLEN Standort DES MONSTERS, nicht zum Spieler) - jenseits davon Ziel fallen lassen
    /// und einmalig Richtung Anker lossetzen. Sonst: innerhalb MeleeRangeWire -> angreifen falls Cooldown
    /// bereit (über TryAttack, derselbe Mechanismus wie beim Spieler-Angriff - nur bei Erfolg in
    /// monsterAttacks aufgenommen, damit GameServer die Pakete verschickt); außerhalb -> Bewegungsziel JEDEN
    /// Tick neu auf die aktuelle Position des Zielspielers setzen (bewusst - das Ziel bewegt sich live weiter,
    /// siehe docs/tasks/M4c-monster-aggro.md Abschnitt 3), dafür in jedem Chase-Tick in startedWandering
    /// aufgenommen (siehe Tick-Kommentar zur erweiterten Bedeutung dieser Liste).</summary>
    private void RunMonsterAi(Entity entity, long nowMs,
        List<Entity> startedWandering, List<(int AttackerEntityId, int TargetEntityId, AttackResult Result)> monsterAttacks)
    {
        if (entity.TargetEntityId is int currentTargetId)
        {
            if (!_entities.TryGetValue(currentTargetId, out Entity? targetPlayer) || targetPlayer.IsDead)
            {
                entity.TargetEntityId = null;
                return;
            }

            long dxAnchor = entity.IntX - entity.WanderAnchorX;
            long dyAnchor = entity.IntY - entity.WanderAnchorY;
            long leashSq = (long)entity.LeashRangeWire * entity.LeashRangeWire;
            if (dxAnchor * dxAnchor + dyAnchor * dyAnchor > leashSq)
            {
                entity.TargetEntityId = null;
                SetDestination(entity.Id, entity.WanderAnchorX, entity.WanderAnchorY);
                startedWandering.Add(entity);
                return;
            }

            long dxTarget = entity.IntX - targetPlayer.IntX;
            long dyTarget = entity.IntY - targetPlayer.IntY;
            long meleeSq = (long)entity.MeleeRangeWire * entity.MeleeRangeWire;
            if (dxTarget * dxTarget + dyTarget * dyTarget <= meleeSq)
            {
                if (nowMs >= entity.AttackReadyAtMs)
                {
                    AttackResult result = TryAttack(entity.Id, targetPlayer.Id, nowMs);
                    if (result.Success)
                        monsterAttacks.Add((entity.Id, targetPlayer.Id, result));
                }
                return;
            }

            SetDestination(entity.Id, targetPlayer.IntX, targetPlayer.IntY);
            startedWandering.Add(entity);
            return;
        }

        if (entity.Aggressive)
        {
            Entity? nearestPlayer = FindNearestLivingPlayerWithinRange(entity, entity.AggroRangeWire);
            if (nearestPlayer != null)
            {
                entity.TargetEntityId = nearestPlayer.Id;
                return; // Verfolgung beginnt im naechsten Tick (40ms Verzoegerung, nicht wahrnehmbar)
            }
        }

        if (nowMs < entity.NextWanderAtMs) return;
        if (RunWanderAi(entity, nowMs))
            startedWandering.Add(entity);
    }

    /// <summary>Nächstgelegener lebender Spieler innerhalb rangeWire um monster (quadrierte Distanz, wie
    /// ComputeVisibility/TryAttack) - null, wenn keiner in Reichweite ist. O(n) über alle Entities, analog zur
    /// bewussten O(n²)-Entscheidung bei ComputeVisibility (siehe dort) - bei unserer Spielergrößenordnung
    /// vernachlässigbar, keine räumliche Indexstruktur nötig.</summary>
    private Entity? FindNearestLivingPlayerWithinRange(Entity monster, int rangeWire)
    {
        long rangeSq = (long)rangeWire * rangeWire;
        Entity? nearest = null;
        long nearestDistSq = long.MaxValue;

        foreach (Entity candidate in _entities.Values)
        {
            if (candidate.Kind != EntityKind.Player || candidate.IsDead) continue;

            long dx = monster.IntX - candidate.IntX;
            long dy = monster.IntY - candidate.IntY;
            long distSq = dx * dx + dy * dy;
            if (distSq > rangeSq || distSq >= nearestDistSq) continue;

            nearestDistSq = distSq;
            nearest = candidate;
        }

        return nearest;
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
