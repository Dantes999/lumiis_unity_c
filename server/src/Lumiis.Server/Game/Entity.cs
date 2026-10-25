using System.Collections.Generic;
using Lumiis.Data.Classes;
using Lumiis.Data.Items;
using Lumiis.Shared;
using Lumiis.Shared.Net;

namespace Lumiis.Server.Game;

/// <summary>Eine bewegliche Entity in der Welt - seit M3c Spieler ODER Monster (Kind), reine Logik, kein
/// Netzwerk.</summary>
public sealed class Entity
{
    /// <summary>Default-Kampfwerte für Call-Sites ohne Kampfbezug (Bewegung/AOI/Positions-Speicherung-Tests,
    /// die new Entity(...)/World.TryJoin/World.SpawnMonster ohne explizite Werte aufrufen) - reale Spieler
    /// bekommen ihre Werte immer aus ClassDef (siehe GameServer.CompleteJoin/World.RecomputeDerivedStats),
    /// reale Monster aus der MobCatalog (siehe World.SpawnMonster-Aufruf im GameServer-Konstruktor).</summary>
    internal const int DefaultMaxHp = 100;
    internal const int DefaultAttack = 10;
    internal const int DefaultDefense = 0;
    internal const int DefaultMeleeRangeWire = 150;
    internal const int DefaultAttackCooldownMs = 1000;

    public int Id { get; }

    /// <summary>Die Datenbank-Id des Charakters (Lumiis.Data.Character) - World nutzt sie, um pro
    /// CharacterId nur eine gleichzeitige Verbindung zuzulassen (siehe World._entityIdByCharacterId). Nur bei
    /// Kind=Player gesetzt - Monster haben keine Verbindung/keinen Account (siehe CreateMonster).</summary>
    public int? CharacterId { get; }

    public string Name { get; }

    public EntityKind Kind { get; }

    /// <summary>Id der Karte, auf der diese Entity aktuell steht (Multi-Map, Protocol.Version 19). Wird von
    /// World beim Anlegen/Transfer gesetzt und von GameServer für Persistenz (Position/CharacterSave) genutzt -
    /// Mobs/NPCs bleiben zeitlebens auf ihrer Karte, Spieler wechseln per Portal/Dev-Tool.</summary>
    public string MapId { get; set; } = "village";

    /// <summary>Wandzeit, ab wann dieses Portal-Ereignis erneut auslösen darf (Multi-Map: Sperre gegen
    /// Mehrfachauslösung, siehe World.CheckPortal) - 0 = sofort. Nur bei Kind=Player relevant.</summary>
    public long PortalReadyAtMs { get; set; }

    /// <summary>M6d: Bitmaske der bereits ausgeloesten Lumiis-Wellen (Bit i = Welle i aus data/metins.json).
    /// Nur bei Kind=Monster mit einem Stein-Eintrag relevant; ein Respawn erzeugt eine neue Entity und damit
    /// wieder 0.</summary>
    public int MetinWavesTriggeredMask { get; set; }

    /// <summary>Bei Kind=Monster ein Index in data/mobs.json, bei Kind=Npc (M5b) ein Index in data/npcs.json,
    /// bei Kind=Player ungenutzt/0 - bis M5a "MobId" genannt, seit M5b umbenannt (reiner Rename, siehe
    /// docs/tasks/M5b-shop.md Abschnitt 0b und EntitySpawnPacket.TypeId).</summary>
    public int TypeId { get; }

    /// <summary>Nur Kind=GroundItem: ItemId (TypeId) und Aufwertungsstufe dieser Boden-Instanz.</summary>
    public int ItemUpgradeLevel { get; set; }

    /// <summary>Nur Kind=GroundItem: EntityId des Spielers, dem der Drop bis LootOwnershipEndsAtMs gehört
    /// (Killer; seine Gruppe darf ebenfalls aufheben, siehe World.TryPickupItem). null = frei für alle.</summary>
    public int? LootOwnerEntityId { get; set; }

    /// <summary>Nur Kind=GroundItem: bis zu diesem Zeitpunkt (monotone Serverzeit, ms) gilt LootOwnerEntityId.</summary>
    public long LootOwnershipEndsAtMs { get; set; }

    /// <summary>Nur Kind=GroundItem: ab diesem Zeitpunkt wird der Drop entfernt (0 = nie, z. B. in Tests).</summary>
    public long DespawnAtMs { get; set; }

    /// <summary>Gruppenrollen-Bonus in Prozent (data/party.json, gesetzt von GameServer), angewendet in
    /// World.RecomputeDerivedStats nach Ausrüstung und Kampfrausch. 0 = kein Bonus.</summary>
    public int PartyAttackBonusPercent { get; set; }
    public int PartyDefenseBonusPercent { get; set; }

    /// <summary>Nur Kind=Npc: technischer Dienst (Shop/Schmied), aus data/npcs.json.</summary>
    public NpcService Service { get; set; }

    /// <summary>Nur bei Kind=Monster gesetzt - die Id des SpawnPoint aus spawns.json, an dem dieses Monster
    /// entstanden ist (siehe World.SpawnMonster/GameServer-Konstruktor). Null bei Spielern UND bei per
    /// Dev-Tools-Debug-Befehl gespawnten Monstern (siehe GameServer.HandleDebugSpawnMonster) - Letztere sind
    /// bewusst an KEINEN SpawnPoint gebunden, damit ihr Tod (M4b) keinen Respawn an einem SpawnPoint auslöst,
    /// der eigentlich schon ein "echtes" Monster hat. M4b darf sich beim Respawn-Scheduling ausschließlich auf
    /// SpawnPointId.HasValue verlassen, nie auf Kind allein.</summary>
    public int? SpawnPointId { get; }

    /// <summary>Nur bei Kind=Monster MIT gesetzter SpawnPointId aussagekräftig (aus SpawnPoint.RespawnSeconds,
    /// siehe World.SpawnMonster/GameServer-Konstruktor) - Sekunden bis zum Respawn nach dem Tod (M4b). Wird
    /// bei jedem Respawn an die neue Entity weitergereicht, damit derselbe SpawnPoint bei erneutem Tod wieder
    /// korrekt respawnt (siehe World.ScheduleMonsterRespawn). 0/ungenutzt bei Spielern und bei per
    /// Dev-Tools-Debug-Befehl gespawnten Monstern (SpawnPointId == null, respawnen nie).</summary>
    public int RespawnSeconds { get; }

    /// <summary>Wird von GameServer direkt nach dem Join aus der geladenen Character-Zeile gesetzt. Bei
    /// Monstern mit Default-Werten befüllt (siehe EntitySpawnPacket-Kommentar), keine Bedeutung.</summary>
    public Appearance Appearance { get; set; }

    public double X { get; private set; }
    public double Y { get; private set; }

    public bool IsMoving { get; private set; }
    public int TargetX { get; private set; }
    public int TargetY { get; private set; }
    public int Speed { get; private set; }

    public int IntX => (int)System.Math.Round(X);
    public int IntY => (int)System.Math.Round(Y);

    /// <summary>Wandzeit (siehe GameServer._nowMs) des letzten Positions-Speicherversuchs - vom Game-Loop-
    /// Thread gesetzt (GameServer.EnqueueDuePositionSaves), rein Buchhaltung für die 30s-Speicherentscheidung,
    /// keine Aussage darüber, ob der Hintergrund-Task den Schreibvorgang schon abgeschlossen hat. Nur bei
    /// Kind=Player relevant (siehe CharacterId).</summary>
    public long LastSaveAtMs { get; set; }

    /// <summary>Nur bei Kind=Monster gesetzt (Spawn-Position, fester Bezugspunkt fürs Wandern, siehe
    /// World.RunWanderAi) - bei Kind=Player die eigene Spawn-Position, ungenutzt.</summary>
    public int WanderAnchorX { get; }
    public int WanderAnchorY { get; }

    /// <summary>Nur bei Kind=Monster gesetzt (aus dem Mob-Typ, siehe MobCatalog/World.SpawnMonster) - bei
    /// Kind=Player 0/ungenutzt.</summary>
    public int WanderRadiusWire { get; }
    public int WanderIntervalMinMs { get; }
    public int WanderIntervalMaxMs { get; }

    /// <summary>Wandzeit (siehe GameServer._nowMs), wann die nächste Wander-Zielwahl fällig ist (siehe
    /// World.RunWanderAi) - von World gesetzt, nur bei Kind=Monster relevant.</summary>
    public long NextWanderAtMs { get; set; }

    /// <summary>Eigene Bewegungsgeschwindigkeit (1/100 Welteinheiten/s) - bei Kind=Player immer
    /// GameConstants.PlayerSpeed, bei Kind=Monster aus dem Mob-Typ (mobs.json SpeedWire). World.SetDestination
    /// nutzt das statt eines hartkodierten Werts, damit Spieler und Monster denselben Bewegungsmechanismus
    /// mit jeweils eigener Geschwindigkeit teilen.</summary>
    public int MovementSpeed { get; }

    public int MaxHp { get; set; }
    public int CurrentHp { get; set; }

    /// <summary>Grundwerte ohne Ausrüstungs-Boni (M5a; seit M6a bei Spielern zusätzlich inklusive
    /// Level-/Werte-Formel, siehe World.RecomputeDerivedStats) - bei Spielern aus ClassDef, bei Monstern
    /// identisch zu Attack/Defense (Monster rüsten nie etwas aus). Attack/Defense sind mutable und werden nach
    /// jeder Änderung (Equip/Unequip, Levelaufstieg, Punkteverteilung) auf BaseAttack/BaseDefense plus die
    /// Summe aller vier ausgerüsteten Item-Boni neu berechnet - TryAttack liest weiterhin nur Attack/Defense,
    /// unverändert.</summary>
    public int BaseAttack { get; set; }
    public int BaseDefense { get; set; }
    public int Attack { get; set; }
    public int Defense { get; set; }

    /// <summary>Nur bei Kind=Player aussagekräftig (aus ClassDef) - Monster/NPCs greifen mit ihren eigenen
    /// Werten aus MobCatalog bzw. gar nicht (NPCs), bei Kind=Npc 0/ungenutzt.</summary>
    public int MeleeRangeWire { get; }
    public int AttackCooldownMs { get; }

    /// <summary>Wandzeit (siehe GameServer._nowMs), ab wann der nächste Angriff dieser Entity erlaubt ist -
    /// von World.TryAttack gesetzt, nur bei Kind=Player relevant.</summary>
    public long AttackReadyAtMs { get; set; }

    /// <summary>Wandzeit, wann ein toter Spieler respawnt (0 = kein ausstehender Respawn) - siehe
    /// World.Tick/World.ApplyDamage. Nur bei Kind=Player relevant, Monster werden bei 0 Leben stattdessen
    /// sofort aus World entfernt (siehe World.ApplyDamage).</summary>
    public long RespawnAtMs { get; set; }

    /// <summary>M4c: greift dieser Mob-Typ von sich aus an, sobald ein Spieler in AggroRangeWire kommt (siehe
    /// World-Tick-Monster-KI)? AggroRangeWire nur bei Aggressive=true aussagekräftig. Passive Mobs
    /// (Aggressive=false) erfassen ein Ziel nur reaktiv nach einem Treffer (siehe TryAttack). Nur bei
    /// Kind=Monster gesetzt.</summary>
    public bool Aggressive { get; }
    public int AggroRangeWire { get; }

    /// <summary>M4c: Distanz vom WanderAnchor, ab der ein verfolgtes Ziel aufgegeben wird (siehe
    /// World-Tick-Monster-KI). Nur bei Kind=Monster gesetzt.</summary>
    public int LeashRangeWire { get; }

    /// <summary>M4c: aktuell verfolgtes/angegriffenes Ziel (immer eine Spieler-EntityId) - null bedeutet "kein
    /// Ziel, normale Wander-KI aktiv" (siehe World.RunWanderAi). Nur bei Kind=Monster gesetzt. Ersetzt für die
    /// Dauer eines aktiven Ziels die normale Wander-Zufallsbewegung - dasselbe nullable-Feld-Muster wie
    /// SpawnPointId/CharacterId, kein paralleler Zustandscontainer (siehe World-Tick-Monster-KI-Kommentar).</summary>
    public int? TargetEntityId { get; set; }

    public bool IsDead => CurrentHp <= 0;

    /// <summary>Nur bei Kind=Monster gesetzt (aus dem Mob-Typ, siehe MobCatalog) - Yang-Spanne, die beim Tod
    /// ausgezahlt wird (siehe World.ApplyDamage). Bei Kind=Player 0/ungenutzt.</summary>
    public int YangMin { get; }
    public int YangMax { get; }

    /// <summary>M5a: mögliche Drops beim Tod dieses Monsters (ItemId, ChancePercent), aus dem Mob-Typ (siehe
    /// MobCatalog.MobDrop) - bewusst als rohe Tupel-Liste statt eines Lumiis.Data-Typs, damit Entity frei von
    /// Lumiis.Data-Referenzen bleibt (wie WanderRadiusWire usw.). Leer bei Kind=Player.</summary>
    public IReadOnlyList<(int ItemId, int ChancePercent)> Drops { get; }

    /// <summary>Laufender Yang-Gesamtbetrag - nur bei Kind=Player relevant, beim Join aus Character.Yang
    /// geladen (siehe GameServer.CompleteJoin), von World.TryAttack bei Monstertod erhöht.</summary>
    public long Yang { get; set; }

    /// <summary>Inventar (M5a) - 20 Plätze, 0 = leer (Item-IDs aus data/items.json). Nur bei Kind=Player
    /// sinnvoll befüllt (siehe GameServer.CompleteJoin) - Monster haben ein leeres, ungenutztes Array.</summary>
    public int[] Inventory { get; } = new int[GameConstants.InventorySize];

    /// <summary>M6c: Aufwertungsstufe 0-9 je Inventarplatz, parallel zu Inventory indexiert (0 = nicht
    /// aufgewertet). Wird zusammen mit Inventory beim Anlegen/Ablegen verschoben (siehe World.TryEquip/
    /// TryUnequip), damit eine Item-Instanz ihre Stufe behält.</summary>
    public int[] InventoryUpgradeLevels { get; } = new int[GameConstants.InventorySize];

    /// <summary>Ausgerüstete Items (M5a, Item-IDs aus data/items.json, 0 = Slot leer) - nur bei Kind=Player
    /// relevant. Werden von World.TryEquip/TryUnequip gepflegt, beim Join zusätzlich aus der Character-Zeile
    /// übernommen (siehe GameServer.CompleteJoin).</summary>
    public int EquippedArmorItemId { get; set; }
    public int EquippedHelmetItemId { get; set; }
    public int EquippedWeaponItemId { get; set; }
    public int EquippedShieldItemId { get; set; }

    /// <summary>M6c: Aufwertungsstufe 0-9 der jeweiligen ausgerüsteten Item-Instanz (nur sinnvoll, solange
    /// die zugehörige Equipped...ItemId != 0). Wird von World.TryUpgrade erhöht, von TryEquip/TryUnequip
    /// mit dem Item verschoben und bei einer Zerstörung auf 0 zurückgesetzt.</summary>
    public int EquippedArmorUpgradeLevel { get; set; }
    public int EquippedHelmetUpgradeLevel { get; set; }
    public int EquippedWeaponUpgradeLevel { get; set; }
    public int EquippedShieldUpgradeLevel { get; set; }

    /// <summary>Liest ItemId + Stufe eines Ausrüstungs-Slots (0/0 bei leerem Slot).</summary>
    public (int ItemId, int UpgradeLevel) GetEquipped(EquipmentSlot slot)
    {
        return slot switch
        {
            EquipmentSlot.Armor => (EquippedArmorItemId, EquippedArmorUpgradeLevel),
            EquipmentSlot.Helmet => (EquippedHelmetItemId, EquippedHelmetUpgradeLevel),
            EquipmentSlot.Weapon => (EquippedWeaponItemId, EquippedWeaponUpgradeLevel),
            EquipmentSlot.Shield => (EquippedShieldItemId, EquippedShieldUpgradeLevel),
            _ => (0, 0)
        };
    }

    /// <summary>Setzt ItemId + Stufe eines Ausrüstungs-Slots gemeinsam (0/0 = leeren).</summary>
    public void SetEquipped(EquipmentSlot slot, int itemId, int upgradeLevel)
    {
        switch (slot)
        {
            case EquipmentSlot.Armor:
                EquippedArmorItemId = itemId;
                EquippedArmorUpgradeLevel = itemId == 0 ? 0 : upgradeLevel;
                break;
            case EquipmentSlot.Helmet:
                EquippedHelmetItemId = itemId;
                EquippedHelmetUpgradeLevel = itemId == 0 ? 0 : upgradeLevel;
                break;
            case EquipmentSlot.Weapon:
                EquippedWeaponItemId = itemId;
                EquippedWeaponUpgradeLevel = itemId == 0 ? 0 : upgradeLevel;
                break;
            case EquipmentSlot.Shield:
                EquippedShieldItemId = itemId;
                EquippedShieldUpgradeLevel = itemId == 0 ? 0 : upgradeLevel;
                break;
        }
    }

    /// <summary>Mappt einen Ausrüstungs-ItemType auf den passenden EquipmentSlot (null für Nicht-Ausrüstung).</summary>
    public static EquipmentSlot? SlotForItemType(ItemType itemType)
    {
        return itemType switch
        {
            ItemType.Armor => EquipmentSlot.Armor,
            ItemType.Helmet => EquipmentSlot.Helmet,
            ItemType.Weapon => EquipmentSlot.Weapon,
            ItemType.Shield => EquipmentSlot.Shield,
            _ => null
        };
    }

    /// <summary>M5b: Sortiment eines NPC-Händlers (ItemIds aus data/npcs.json, über NpcDefinition.ShopItemIds
    /// von World.SpawnNpc gesetzt - analog zu Appearance dort, nicht über den Konstruktor, da nur bei
    /// Kind=Npc relevant). Leer bei Kind=Player/Monster; "hat ein Sortiment" in World.TryOpenShop prüft
    /// genau dieses Feld.</summary>
    public IReadOnlyList<int> ShopItemIds { get; set; } = System.Array.Empty<int>();

    // ---- M6a: Fortschritt (Level/XP/Werte/SP/Ausweichen/Regeneration, siehe CLAUDE.md Abschnitt
    // "Fortschritt: Level, XP, Werte") ----

    /// <summary>Klassen-Formelkonstanten (BaseHp/HpPerHt/DodgeCapBp/... siehe Lumiis.Data.Classes.
    /// ClassDefinition) - nur bei Kind=Player gesetzt, aus der ClassCatalog beim Join (siehe
    /// World.TryJoin/RecomputeDerivedStats). Null bedeutet "keine Formel-Werte" (bestehende Tests ohne
    /// Fortschritts-/Kampfbezug, die new Entity(...)/World.TryJoin ohne classDef aufrufen) - MaxHp/Attack/
    /// Defense bleiben dann bei den direkt übergebenen Konstruktor-Werten, RecomputeDerivedStats ist dann ein
    /// No-op (siehe dort). Bewusst als Referenz statt einzelner Felder gehalten - World kennt Lumiis.Data
    /// bereits (CollisionMap/ItemCatalog), eine Kopie aller ~20 Formelkonstanten auf jede Entity wäre
    /// unnötige Duplikation.</summary>
    public ClassDefinition? ClassDef { get; }

    /// <summary>Bei Kind=Player: Charakter-Level (1..ProgressionCatalog.MaxLevel), aus der DB beim Join
    /// geladen (siehe World.TryJoin), erhöht durch World.AwardXp/Dev-Tools-DebugSetLevel. Bei Kind=Monster:
    /// fixer Mob-Level aus MobCatalog (siehe World.SpawnMonster) - für den Levelunterschieds-Faktor in
    /// World.AwardXp, ändert sich nie. Bei Kind=Npc 0/ungenutzt.</summary>
    public int Level { get; set; } = 1;

    public long Xp { get; set; }
    public int SpentSt { get; set; }
    public int SpentHt { get; set; }
    public int SpentDx { get; set; }
    public int SpentIq { get; set; }
    public int UnspentStatPoints { get; set; }
    public int UnspentSkillPoints { get; set; }

    /// <summary>Summe aus Klassen-Basiswert (ClassDef, 0 wenn kein ClassDef) und verteilten Punkten - reine
    /// Lesehilfe, kein eigener Zustand.</summary>
    public int St => (ClassDef?.BaseSt ?? 0) + SpentSt;
    public int Ht => (ClassDef?.BaseHt ?? 0) + SpentHt;
    public int Dx => (ClassDef?.BaseDx ?? 0) + SpentDx;
    public int Iq => (ClassDef?.BaseIq ?? 0) + SpentIq;

    /// <summary>Zweite Ressource (M6a) - analog zu MaxHp/CurrentHp, siehe World.RecomputeDerivedStats. Nur
    /// bei Kind=Player sinnvoll befüllt (0 bei Monster/Npc, ClassDef ist dort null).</summary>
    public int MaxSp { get; set; }
    public int CurrentSp { get; set; }

    /// <summary>Ausweichchance in Basispunkten (100 = 1 Prozent, siehe World.TryAttack) - aus Dx berechnet
    /// (World.RecomputeDerivedStats), bei Monstern/NPCs immer 0 (nie neu berechnet, ClassDef ist null).</summary>
    public int DodgeBp { get; set; }

    /// <summary>Wandzeit des letzten Kampfereignisses dieser Entity (Schaden genommen, Schaden ausgeteilt
    /// oder einem Treffer ausgewichen) - World.Tick regeneriert erst RegenDelayMs danach (siehe
    /// World.RunRegeneration). Nur bei Kind=Player relevant.</summary>
    public long LastCombatAtMs { get; set; }

    /// <summary>Wandzeit, wann die nächste Regenerations-Prüfung fällig ist (siehe World.RunRegeneration) -
    /// dasselbe Fälligkeits-Muster wie NextWanderAtMs bei Monstern. Nur bei Kind=Player relevant.</summary>
    public long NextRegenAtMs { get; set; }

    /// <summary>Nur bei Kind=Monster aussagekräftig (aus MobCatalog, siehe World.SpawnMonster) - Basis-XP für
    /// World.AwardXp, vor Anwendung des Levelunterschieds-Faktors. 0/ungenutzt bei Kind=Player/Npc.</summary>
    public int BaseXp { get; }

    // ---- M6b: Skills (Lernstufen/Cooldowns/aktive Buffs, siehe CLAUDE.md Abschnitt "Skills") ----

    /// <summary>Gelernte Skillstufen (SkillId -> Level 1-17) - kein Eintrag = nicht gelernt (Stufe 0), analog
    /// zum Inventar-Array (0 = leer). Nur bei Kind=Player befüllt, aus der DB beim Join geladen (siehe
    /// GameServer.CompleteJoin).</summary>
    public Dictionary<int, int> SkillLevels { get; } = new();

    /// <summary>Wandzeit (siehe GameServer._nowMs), ab wann der jeweilige Skill wieder einsetzbar ist
    /// (SkillId -> readyAtMs) - vier UNABHÄNGIGE Cooldowns pro Charakter, getrennt vom normalen
    /// Angriffs-Cooldown (AttackReadyAtMs). Kein Eintrag = noch nie benutzt (sofort bereit). Nur bei
    /// Kind=Player relevant.</summary>
    public Dictionary<int, long> SkillCooldownReadyAtMs { get; } = new();

    /// <summary>Aktive Buffs (BuffType -> Wirkungsstärke aus der Skill-Stufe [AttackBonusPercent bzw.
    /// CooldownReductionPercent] + Ablaufzeitpunkt in Wandzeit) - höchstens ein Eintrag pro BuffType, ein
    /// erneuter Cast desselben Typs ersetzt den bisherigen (kein Stapeln, siehe World.TryUseSkill).
    /// Überleben KEINEN Disconnect (kein eigener Aufräum-Code nötig - die ganze Entity verschwindet mit dem
    /// Disconnect aus World, siehe World.Remove). Nur bei Kind=Player relevant.</summary>
    public Dictionary<BuffType, (int Strength, long ExpiresAtMs)> ActiveBuffs { get; } = new();

    private Entity(int id, EntityKind kind, int? characterId, int typeId, int? spawnPointId, int respawnSeconds, string name, int x, int y, int movementSpeed,
        int wanderRadiusWire, int wanderIntervalMinMs, int wanderIntervalMaxMs,
        int maxHp, int attack, int defense, int meleeRangeWire, int attackCooldownMs, int yangMin, int yangMax,
        bool aggressive, int aggroRangeWire, int leashRangeWire, IReadOnlyList<(int ItemId, int ChancePercent)> drops,
        ClassDefinition? classDef, int baseXp)
    {
        Id = id;
        Kind = kind;
        CharacterId = characterId;
        TypeId = typeId;
        SpawnPointId = spawnPointId;
        RespawnSeconds = respawnSeconds;
        Name = name;
        X = x;
        Y = y;
        MovementSpeed = movementSpeed;
        WanderAnchorX = x;
        WanderAnchorY = y;
        WanderRadiusWire = wanderRadiusWire;
        WanderIntervalMinMs = wanderIntervalMinMs;
        WanderIntervalMaxMs = wanderIntervalMaxMs;
        MaxHp = maxHp;
        CurrentHp = maxHp;
        BaseAttack = attack;
        BaseDefense = defense;
        Attack = attack;
        Defense = defense;
        MeleeRangeWire = meleeRangeWire;
        AttackCooldownMs = attackCooldownMs;
        YangMin = yangMin;
        YangMax = yangMax;
        Aggressive = aggressive;
        AggroRangeWire = aggroRangeWire;
        LeashRangeWire = leashRangeWire;
        Drops = drops;
        ClassDef = classDef;
        BaseXp = baseXp;
    }

    /// <summary>classDef: null bedeutet "keine Formel-Werte" (siehe ClassDef-Kommentar) - maxHp/attack/
    /// defense sind in dem Fall die tatsächlichen Werte (wie vor M6a), sonst nur Platzhalter bis zum
    /// nächsten World.RecomputeDerivedStats-Aufruf (siehe World.TryJoin, ruft das bei gesetztem classDef
    /// selbst auf).</summary>
    public Entity(int id, int characterId, string name, int x, int y,
        int maxHp = DefaultMaxHp, int attack = DefaultAttack, int defense = DefaultDefense,
        int meleeRangeWire = DefaultMeleeRangeWire, int attackCooldownMs = DefaultAttackCooldownMs,
        ClassDefinition? classDef = null)
        : this(id, EntityKind.Player, characterId, typeId: 0, spawnPointId: null, respawnSeconds: 0, name, x, y, movementSpeed: GameConstants.PlayerSpeed,
            wanderRadiusWire: 0, wanderIntervalMinMs: 0, wanderIntervalMaxMs: 0,
            maxHp, attack, defense, meleeRangeWire, attackCooldownMs, yangMin: 0, yangMax: 0,
            aggressive: false, aggroRangeWire: 0, leashRangeWire: 0, drops: System.Array.Empty<(int, int)>(),
            classDef, baseXp: 0)
    {
    }

    /// <summary>spawnPointId: null bedeutet "nicht an einen SpawnPoint aus spawns.json gebunden" (Dev-Tools-
    /// Debug-Spawn, siehe SpawnPointId-Kommentar) - reguläre, aus der SpawnCatalog erzeugte Monster übergeben
    /// hier immer die echte SpawnPoint.Id (siehe World.SpawnMonster/GameServer-Konstruktor).
    /// respawnSeconds: nur bei gesetzter spawnPointId aussagekräftig (siehe RespawnSeconds-Kommentar).
    /// meleeRangeWire/attackCooldownMs/aggressive/aggroRangeWire/leashRangeWire: aus dem Mob-Typ (M4c, siehe
    /// MobCatalog) - Defaults nur für bestehende Tests ohne Kampf-/Aggro-Bezug. drops: mögliche Drops beim
    /// Tod (M5a, siehe MobCatalog.MobDrop) - Default leer. level/baseXp: M6a, siehe Level/BaseXp-Kommentar -
    /// Defaults nur für bestehende Tests ohne XP-Bezug.</summary>
    public static Entity CreateMonster(int id, int mobId, int? spawnPointId, string name, int x, int y, int speedWire,
        int wanderRadiusWire, int wanderIntervalMinMs, int wanderIntervalMaxMs,
        int maxHp = DefaultMaxHp, int attack = DefaultAttack, int defense = DefaultDefense, int yangMin = 0, int yangMax = 0,
        int respawnSeconds = 0, int meleeRangeWire = DefaultMeleeRangeWire, int attackCooldownMs = DefaultAttackCooldownMs,
        bool aggressive = false, int aggroRangeWire = 0, int leashRangeWire = 0,
        IReadOnlyList<(int ItemId, int ChancePercent)>? drops = null, int level = 1, int baseXp = 0)
    {
        Entity entity = new(id, EntityKind.Monster, characterId: null, mobId, spawnPointId, respawnSeconds, name, x, y, movementSpeed: speedWire,
            wanderRadiusWire, wanderIntervalMinMs, wanderIntervalMaxMs,
            maxHp, attack, defense, meleeRangeWire, attackCooldownMs, yangMin, yangMax,
            aggressive, aggroRangeWire, leashRangeWire, drops ?? System.Array.Empty<(int, int)>(),
            classDef: null, baseXp);
        entity.Level = level;
        return entity;
    }

    /// <summary>M5b: ein statischer NPC (Shop-Händler) - keine Bewegung (movementSpeed 0, kein Wander-
    /// Zustand), keine SpawnPointId (kein Respawn-Scheduling, das ist ohnehin an Kind=Monster gebunden, siehe
    /// SpawnPointId-Kommentar), keine CharacterId. Kampfwerte bleiben auf den Default-Konstanten (nie
    /// gelesen: TryAttack lehnt Kind=Npc-Ziele ab, bevor irgendein Kampfwert relevant würde, siehe
    /// docs/tasks/M5b-shop.md Abschnitt 0a). Appearance wird von außen gesetzt (siehe Entity.Appearance,
    /// GameServer-Konstruktor - analog zu Spielern/CompleteJoin, nicht wie bei Monstern ungenutzt).</summary>
    public static Entity CreateGroundItem(int id, int itemId, int upgradeLevel, string name, int x, int y)
    {
        Entity entity = new(id, EntityKind.GroundItem, characterId: null, itemId, spawnPointId: null, respawnSeconds: 0, name, x, y, movementSpeed: 0,
            wanderRadiusWire: 0, wanderIntervalMinMs: 0, wanderIntervalMaxMs: 0,
            maxHp: 1, attack: 0, defense: 0,
            meleeRangeWire: 0, attackCooldownMs: 0, yangMin: 0, yangMax: 0,
            aggressive: false, aggroRangeWire: 0, leashRangeWire: 0, drops: System.Array.Empty<(int, int)>(),
            classDef: null, baseXp: 0);
        entity.ItemUpgradeLevel = upgradeLevel;
        return entity;
    }

    public static Entity CreateNpc(int id, int npcId, string name, int x, int y)
    {
        return new Entity(id, EntityKind.Npc, characterId: null, npcId, spawnPointId: null, respawnSeconds: 0, name, x, y, movementSpeed: 0,
            wanderRadiusWire: 0, wanderIntervalMinMs: 0, wanderIntervalMaxMs: 0,
            maxHp: DefaultMaxHp, attack: DefaultAttack, defense: DefaultDefense,
            meleeRangeWire: DefaultMeleeRangeWire, attackCooldownMs: DefaultAttackCooldownMs, yangMin: 0, yangMax: 0,
            aggressive: false, aggroRangeWire: 0, leashRangeWire: 0, drops: System.Array.Empty<(int, int)>(),
            classDef: null, baseXp: 0);
    }

    public void MoveTo(int targetX, int targetY, int speed)
    {
        TargetX = targetX;
        TargetY = targetY;
        Speed = speed;
        IsMoving = true;
    }

    /// <summary>Setzt die Position hart, ohne Bewegung (Respawn, M4a) - kein Interpolationsziel, der Client
    /// erhält dafür ein EntityStopPacket (siehe GameServer), keine neue Paketform nötig.</summary>
    public void Teleport(int x, int y)
    {
        X = x;
        Y = y;
        IsMoving = false;
    }

    /// <summary>Bricht eine laufende Bewegung sofort ab (z. B. Spielertod mitten in der Bewegung), ohne die
    /// Position zu verändern - der nächste Tick meldet sonst fälschlich "angekommen".</summary>
    public void StopMoving()
    {
        IsMoving = false;
    }

    /// <summary>Schreibt die Bewegung um deltaSeconds fort. Gibt true zurück, wenn die Entity in diesem Tick exakt am Ziel angekommen ist (kein Überschießen).</summary>
    public bool Tick(double deltaSeconds)
    {
        if (!IsMoving) return false;

        double dx = TargetX - X;
        double dy = TargetY - Y;
        double distance = System.Math.Sqrt(dx * dx + dy * dy);
        double step = Speed * deltaSeconds;

        if (step >= distance)
        {
            X = TargetX;
            Y = TargetY;
            IsMoving = false;
            return true;
        }

        X += dx / distance * step;
        Y += dy / distance * step;
        return false;
    }
}
