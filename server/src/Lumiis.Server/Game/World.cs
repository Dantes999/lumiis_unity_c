using System;
using System.Collections.Generic;
using Lumiis.Data.Items;
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
    /// <summary>"Rezept" für die nächste Entity an einem SpawnPoint (M4b) - aus den Werten der zuletzt dort
    /// gestorbenen Monster-Entity eingefroren (siehe ScheduleMonsterRespawn), damit World.Tick bei Fälligkeit
    /// exakt denselben Mob-Typ mit denselben Werten über den normalen SpawnMonster-Weg neu erzeugen kann.
    /// Höchstens ein Eintrag pro SpawnPointId gleichzeitig (ein SpawnPoint hat immer entweder eine lebende
    /// Monster-Entity oder einen ausstehenden Respawn, nie beides).</summary>
    private readonly record struct PendingMonsterRespawn(int MobId, string Name, int PositionX, int PositionY, int SpeedWire,
        int WanderRadiusWire, int WanderIntervalMinMs, int WanderIntervalMaxMs,
        int MaxHp, int Attack, int Defense, int YangMin, int YangMax, int RespawnSeconds,
        int MeleeRangeWire, int AttackCooldownMs, bool Aggressive, int AggroRangeWire, int LeashRangeWire,
        IReadOnlyList<(int ItemId, int ChancePercent)> Drops, long DueAtMs);

    private readonly Dictionary<int, Entity> _entities = new();
    private readonly Dictionary<int, int> _entityIdByCharacterId = new();
    private readonly Dictionary<int, PendingMonsterRespawn> _pendingMonsterRespawns = new();
    private readonly CollisionMap? _collisionMap;
    private readonly ItemCatalog? _itemCatalog;
    private readonly Random _random;
    private int _nextEntityId = 1;

    /// <summary>Fix, keine Konfiguration (siehe docs/tasks/M4a-combat-core.md Abschnitt 4) - 3s nach dem Tod
    /// respawnt ein Spieler automatisch (siehe ApplyDamage/Tick).</summary>
    private const long PlayerRespawnDelayMs = 3000;

    /// <summary>collisionMap: null bedeutet "überall begehbar" (z. B. bestehende Tests ohne Kollisionsbezug) -
    /// echte Karten liefern immer eine Instanz mit, siehe GameServer/Program.cs. itemCatalog: null bedeutet
    /// "keine Items" (z. B. bestehende Tests ohne Item-Bezug) - genau wie CollisionMap wird er für laufende
    /// Abfragen gehalten (M5a: TryEquip/TryUnequip/TryUseConsumable müssen zur Laufzeit beliebige ItemIds aus
    /// dem Inventar auflösen können, nicht nur einmalig beim Spawn/Join wie MobCatalog/ClassCatalog - siehe
    /// docs/tasks/M5a-items-equipment.md Abschnitt 0). random: injizierbare Zufallsquelle für die Wander-KI
    /// (siehe RunWanderAi) und Drops (siehe ApplyDamage/TryAttack), Default eine echte, nicht-deterministische
    /// Instanz - Tests übergeben eine geseedete Instanz für deterministisches Verhalten.</summary>
    public World(int maxPlayers, CollisionMap? collisionMap = null, Random? random = null, ItemCatalog? itemCatalog = null)
    {
        MaxPlayers = maxPlayers;
        _collisionMap = collisionMap;
        _itemCatalog = itemCatalog;
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
    /// GameServer-Konstruktor übergibt hier immer die echte SpawnPoint.Id. respawnSeconds: nur bei gesetzter
    /// spawnPointId aussagekräftig (siehe Entity.RespawnSeconds, M4b). meleeRangeWire/attackCooldownMs/
    /// aggressive/aggroRangeWire/leashRangeWire: Kampf-/Aggro-Werte aus der MobCatalog (M4c). drops: mögliche
    /// Drops beim Tod (M5a, siehe Entity.Drops) - Default leer.</summary>
    public Entity SpawnMonster(int mobId, string name, int x, int y, int speedWire,
        int wanderRadiusWire, int wanderIntervalMinMs, int wanderIntervalMaxMs, long nowMs,
        int maxHp = Entity.DefaultMaxHp, int attack = Entity.DefaultAttack, int defense = Entity.DefaultDefense,
        int yangMin = 0, int yangMax = 0, int? spawnPointId = null, int respawnSeconds = 0,
        int meleeRangeWire = Entity.DefaultMeleeRangeWire, int attackCooldownMs = Entity.DefaultAttackCooldownMs,
        bool aggressive = false, int aggroRangeWire = 0, int leashRangeWire = 0,
        IReadOnlyList<(int ItemId, int ChancePercent)>? drops = null)
    {
        int clampedX = Math.Clamp(x, 0, GameConstants.MapWidth);
        int clampedY = Math.Clamp(y, 0, GameConstants.MapHeight);

        int id = _nextEntityId++;
        Entity entity = Entity.CreateMonster(id, mobId, spawnPointId, name, clampedX, clampedY, speedWire,
            wanderRadiusWire, wanderIntervalMinMs, wanderIntervalMaxMs, maxHp, attack, defense, yangMin, yangMax, respawnSeconds,
            meleeRangeWire, attackCooldownMs, aggressive, aggroRangeWire, leashRangeWire, drops);
        entity.NextWanderAtMs = nowMs + _random.Next(wanderIntervalMinMs, wanderIntervalMaxMs + 1);
        _entities[id] = entity;
        return entity;
    }

    /// <summary>Legt eine statische NPC-Entity an (M5b, Serverstart, siehe GameServer-Konstruktor) - EntityId
    /// aus demselben Zähler wie Spieler/Monster, kein Verbindungsbezug, kein Spielerlimit-Check. x/y kommen
    /// bereits geprüft aus NpcSpawnCatalog (begehbare Zelle), werden hier trotzdem defensiv auf die
    /// Kartengrenzen geklemmt, analog zu SpawnMonster/TryJoin. appearance kommt direkt aus der NpcCatalog
    /// (anders als bei Monstern: NPCs haben eine echte, sichtbare Ausrüstung, siehe Entity.CreateNpc-
    /// Kommentar).</summary>
    public Entity SpawnNpc(int npcId, string name, int x, int y, Appearance appearance)
    {
        int clampedX = Math.Clamp(x, 0, GameConstants.MapWidth);
        int clampedY = Math.Clamp(y, 0, GameConstants.MapHeight);

        int id = _nextEntityId++;
        Entity entity = Entity.CreateNpc(id, npcId, name, clampedX, clampedY);
        entity.Appearance = appearance;
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

    /// <summary>Serverautoritative Angriffsprüfung + -ausführung (M4a, seit M4c auch für Monster als
    /// Angreifer verallgemeinert - siehe docs/tasks/M4c-monster-aggro.md Abschnitt 0a) - kein Netzwerkbezug,
    /// siehe GameServer.HandleAttackRequest (Spieler-initiiert) bzw. die Monster-KI in Tick
    /// (Monster-initiiert) für die jeweilige Netzwerk-/Aufrufer-Seite. Prüft in dieser Reihenfolge (siehe
    /// docs/tasks/M4a-combat-core.md Abschnitt 4): Ziel existiert, Ziel lebt, Angreifer und Ziel sind
    /// unterschiedlichen Kinds (blockt Spieler-vs-Spieler UND Monster-vs-Monster - Letzteres löst mein
    /// eigener KI-Code nie aus, aber konzeptionell konsistent; ersetzt den ursprünglichen "Ziel ist kein
    /// Spieler"-Check aus M4a, der implizit "Angreifer ist immer ein Spieler" annahm), Cooldown des
    /// Angreifers, Reichweite (quadrierte Distanz wie ComputeVisibility, gegen MeleeRangeWire des Angreifers -
    /// bei Spielern aus ClassCatalog, bei Monstern seit M4c aus MobCatalog statt hartkodiert 0).
    /// attackerEntityId muss bereits als existierende Entity geprüft sein (bei Spieler-initiierten Angriffen
    /// die per Verbindung gehaltene eigene Entity, siehe GameServer.HandleAttackRequest) - der defensive
    /// TargetNotFound-Fallback hier sollte nie greifen.</summary>
    public AttackResult TryAttack(int attackerEntityId, int targetEntityId, long nowMs)
    {
        if (!_entities.TryGetValue(attackerEntityId, out Entity? attacker))
            return AttackResult.Fail(AttackFailedReason.TargetNotFound);

        if (!_entities.TryGetValue(targetEntityId, out Entity? target))
            return AttackResult.Fail(AttackFailedReason.TargetNotFound);

        if (target.IsDead)
            return AttackResult.Fail(AttackFailedReason.TargetAlreadyDead);

        if (attacker.Kind == target.Kind)
            return AttackResult.Fail(AttackFailedReason.TargetIsPlayer);

        if (nowMs < attacker.AttackReadyAtMs)
            return AttackResult.Fail(AttackFailedReason.OnCooldown);

        long dx = attacker.IntX - target.IntX;
        long dy = attacker.IntY - target.IntY;
        long rangeSq = (long)attacker.MeleeRangeWire * attacker.MeleeRangeWire;
        if (dx * dx + dy * dy > rangeSq)
            return AttackResult.Fail(AttackFailedReason.OutOfRange);

        attacker.AttackReadyAtMs = nowMs + attacker.AttackCooldownMs;

        // Reaktive Zielerfassung (M4c): ein getroffenes Monster ohne eigenes Ziel merkt sich den Angreifer -
        // gilt für aggressive UND passive Mobs gleichermaßen (ein Wildhund, der von hinten angegriffen wird,
        // dreht sich natürlich auch um). target.Kind == Monster schließt die umgekehrte Richtung (Spieler
        // wird von einem Monster getroffen) hier automatisch aus, kein Sonderfall nötig.
        if (target.Kind == EntityKind.Monster && target.TargetEntityId == null)
            target.TargetEntityId = attackerEntityId;

        int targetMaxHp = target.MaxHp;
        int damage = Math.Max(1, attacker.Attack - target.Defense);
        IReadOnlyList<(int ItemId, int ChancePercent)> targetDrops = target.Drops;
        (int targetHpAfter, bool died, long yangAwarded) = ApplyDamage(target, damage, nowMs);

        if (yangAwarded > 0)
            attacker.Yang += yangAwarded;

        // M5a: Drops nach Yang, nur bei totem Monster (target ist nach ApplyDamage evtl. schon aus World
        // entfernt - deshalb targetDrops vorher gesichert statt target.Drops erneut zu lesen). Jeder Eintrag
        // unabhängig gewürfelt (_random.Next(100) in [0,99]: chancePercent=0 kann nie treffen, 100 immer).
        List<int>? droppedItemIds = null;
        bool itemLostToFullInventory = false;
        if (died && target.Kind == EntityKind.Monster && targetDrops.Count > 0)
        {
            foreach ((int itemId, int chancePercent) in targetDrops)
            {
                if (_random.Next(100) >= chancePercent)
                    continue;

                if (TryAddItemToInventory(attackerEntityId, itemId))
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

        return AttackResult.Hit(damage, targetHpAfter, targetMaxHp, died, target.Kind, yangAwarded, droppedItemIds, itemLostToFullInventory);
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
            nowMs + diedMonster.RespawnSeconds * 1000L);
    }

    // ---- M5a: Items, Inventar, Ausrüstung (kein Netzwerkbezug, siehe GameServer für die Paket-Seite) ----

    /// <summary>Legt das Item aus inventorySlotIndex an: prüft Slot belegt und dass der ItemType zu einem
    /// Ausrüstungs-Slot passt, tauscht dann Inventarplatz und Ausrüstungs-Slot (bisheriges Ausrüstungsitem
    /// landet zurück im selben Inventarplatz, 0 falls vorher nichts ausgerüstet war) und berechnet
    /// Attack/Defense neu.</summary>
    public (bool Success, InventoryActionFailedReason Reason) TryEquip(int entityId, int inventorySlotIndex)
    {
        if (!_entities.TryGetValue(entityId, out Entity? entity)) return (false, InventoryActionFailedReason.InvalidSlot);
        if (inventorySlotIndex < 0 || inventorySlotIndex >= entity.Inventory.Length) return (false, InventoryActionFailedReason.InvalidSlot);

        int itemId = entity.Inventory[inventorySlotIndex];
        if (itemId == 0) return (false, InventoryActionFailedReason.EmptySlot);

        if (_itemCatalog == null || !_itemCatalog.TryGet(itemId, out ItemDefinition item))
            return (false, InventoryActionFailedReason.InvalidSlot); // sollte nicht vorkommen - Items im Inventar stammen immer aus dem Katalog.

        int previousEquippedItemId;
        switch (item.ItemType)
        {
            case ItemType.Armor: previousEquippedItemId = entity.EquippedArmorItemId; entity.EquippedArmorItemId = itemId; break;
            case ItemType.Helmet: previousEquippedItemId = entity.EquippedHelmetItemId; entity.EquippedHelmetItemId = itemId; break;
            case ItemType.Weapon: previousEquippedItemId = entity.EquippedWeaponItemId; entity.EquippedWeaponItemId = itemId; break;
            case ItemType.Shield: previousEquippedItemId = entity.EquippedShieldItemId; entity.EquippedShieldItemId = itemId; break;
            default: return (false, InventoryActionFailedReason.WrongItemType); // Consumable kann nicht angelegt werden.
        }

        entity.Inventory[inventorySlotIndex] = previousEquippedItemId;
        RecalculateCombatStats(entity);
        return (true, default);
    }

    /// <summary>Legt das Item aus equipmentSlot ab: braucht einen freien Inventarplatz (erste freie Stelle),
    /// sonst InventoryFull. Berechnet Attack/Defense neu.</summary>
    public (bool Success, InventoryActionFailedReason Reason) TryUnequip(int entityId, EquipmentSlot equipmentSlot)
    {
        if (!_entities.TryGetValue(entityId, out Entity? entity)) return (false, InventoryActionFailedReason.EmptySlot);

        int currentItemId = equipmentSlot switch
        {
            EquipmentSlot.Armor => entity.EquippedArmorItemId,
            EquipmentSlot.Helmet => entity.EquippedHelmetItemId,
            EquipmentSlot.Weapon => entity.EquippedWeaponItemId,
            EquipmentSlot.Shield => entity.EquippedShieldItemId,
            _ => 0
        };
        if (currentItemId == 0) return (false, InventoryActionFailedReason.EmptySlot);

        int freeSlot = FindFirstFreeInventorySlot(entity);
        if (freeSlot < 0) return (false, InventoryActionFailedReason.InventoryFull);

        switch (equipmentSlot)
        {
            case EquipmentSlot.Armor: entity.EquippedArmorItemId = 0; break;
            case EquipmentSlot.Helmet: entity.EquippedHelmetItemId = 0; break;
            case EquipmentSlot.Weapon: entity.EquippedWeaponItemId = 0; break;
            case EquipmentSlot.Shield: entity.EquippedShieldItemId = 0; break;
        }

        entity.Inventory[freeSlot] = currentItemId;
        RecalculateCombatStats(entity);
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
        return (true, default);
    }

    /// <summary>Legt itemId auf die erste freie Inventarstelle. Gibt false zurück, wenn kein Platz frei ist
    /// (Inventar voll) - der Aufrufer entscheidet, wie er das meldet (siehe GameServer.HandleXxx bzw.
    /// TryAttack für Monster-Drops).</summary>
    public bool TryAddItemToInventory(int entityId, int itemId)
    {
        if (!_entities.TryGetValue(entityId, out Entity? entity)) return false;

        int freeSlot = FindFirstFreeInventorySlot(entity);
        if (freeSlot < 0) return false;

        entity.Inventory[freeSlot] = itemId;
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

    /// <summary>Setzt Attack/Defense auf BaseAttack/BaseDefense plus die Summe der Boni aller vier
    /// Ausrüstungs-Slots - nach jedem Equip/Unequip aufgerufen (M5a). Öffentlich, damit GameServer sie auch
    /// direkt nach dem Join aufrufen kann, sobald die aus der DB geladene Ausrüstung auf die Entity
    /// übertragen wurde (siehe GameServer.CompleteJoin).</summary>
    public void RecalculateCombatStats(int entityId)
    {
        if (_entities.TryGetValue(entityId, out Entity? entity))
            RecalculateCombatStats(entity);
    }

    private void RecalculateCombatStats(Entity entity)
    {
        int attackBonus = 0;
        int defenseBonus = 0;

        if (_itemCatalog != null)
        {
            int[] equippedItemIds = { entity.EquippedArmorItemId, entity.EquippedHelmetItemId, entity.EquippedWeaponItemId, entity.EquippedShieldItemId };
            foreach (int equippedItemId in equippedItemIds)
            {
                if (equippedItemId == 0) continue;
                if (_itemCatalog.TryGet(equippedItemId, out ItemDefinition item))
                {
                    attackBonus += item.AttackBonus;
                    defenseBonus += item.DefenseBonus;
                }
            }
        }

        entity.Attack = entity.BaseAttack + attackBonus;
        entity.Defense = entity.BaseDefense + defenseBonus;
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
    /// GameServer nur zum Loggen) und MonsterAttacks (M4c: erfolgreiche Monster-initiierte Angriffe gegen
    /// Spieler - AttackerEntityId/TargetEntityId/Result, damit GameServer dieselben Pakete verschicken kann
    /// wie bei einem spielerinitiierten Angriff, siehe GameServer.BroadcastAttackResult).</summary>
    public (List<Entity> Arrived, List<Entity> StartedWandering, List<Entity> Respawned, List<Entity> RespawnedMonsters,
        List<(int AttackerEntityId, int TargetEntityId, AttackResult Result)> MonsterAttacks) Tick(double deltaSeconds, long nowMs)
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

            entity.Teleport(GameConstants.MapWidth / 2, GameConstants.MapHeight / 2);
            entity.CurrentHp = entity.MaxHp;
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
                    drops: pending.Drops);
                respawnedMonsters.Add(monster);
            }
        }

        return (arrived, startedWandering, respawned, respawnedMonsters, monsterAttacks);
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
