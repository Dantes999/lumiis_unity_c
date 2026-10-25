using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using LiteNetLib;
using Lumiis.Data;
using Lumiis.Data.Classes;
using Lumiis.Data.Items;
using Lumiis.Data.Maps;
using Lumiis.Data.Mobs;
using Lumiis.Data.Npcs;
using Lumiis.Data.Progression;
using Lumiis.Data.Skills;
using Lumiis.Data.Upgrade;
using Lumiis.Server.Game;
using Lumiis.Server.Logging;
using Lumiis.Server.Net;
using Lumiis.Shared;
using Lumiis.Shared.Net;
using Microsoft.EntityFrameworkCore;

namespace Lumiis.Server;

/// <summary>Verbindet Spiellogik (World) und Netzwerk (LiteNetLib) zu einem autoritativen Server mit festem Tick.</summary>
public sealed class GameServer : INetEventListener
{
    private sealed class ConnectionState
    {
        public readonly NetPeer Peer;
        public int? EntityId;

        /// <summary>Multi-Map: World, in der die eigene Entity gerade steht (null bis zum erfolgreichen Join).
        /// AOI/Sichtbarkeit/Portalwechsel laufen ausschließlich über diese World (siehe UpdateVisibility).</summary>
        public World? World;
        public bool JoinPending;
        public bool Closing;
        public long CloseAtMs;

        /// <summary>Letzter bekannter Sichtbarkeits-Stand (siehe UpdateVisibility) - welche EntityIds diese
        /// Verbindung laut dem letzten Tick sieht. Start leer, auch für Entities, die schon beim Join
        /// gesendet wurden (siehe CompleteJoin) - der erste UpdateVisibility-Tick nach dem Join gleicht das an.</summary>
        public readonly HashSet<int> VisibleEntityIds = new();

        /// <summary>M7-Chat: letzter akzeptierter Chat-Zeitpunkt (monotone Serverzeit) für das 400-ms-Limit.</summary>
        public long LastChatAtMs;

        public ConnectionState(NetPeer peer, long closeAtMs)
        {
            Peer = peer;
            CloseAtMs = closeAtMs;
        }
    }

    /// <summary>M7-Party: Leader + Mitglieder als EntityIds; die Zuordnung EntityId -> Party liegt in
    /// _partyByEntityId. Kein DB-Speichern (Alpha-MVP), Gruppen lösen sich beim Verlassen/Disconnect auf.</summary>
    private sealed class PartyState
    {
        public int LeaderEntityId;
        public readonly List<int> Members = new();
    }

    /// <summary>Ergebnis eines asynchronen Charakter-Lookups (siehe LookupCharacterForJoinAsync), auf dem
    /// Game-Loop-Thread eingesammelt (DrainPendingJoinResults). Character != null: Join darf fortgesetzt
    /// werden (World.TryJoin entscheidet endgültig, z. B. CharacterAlreadyOnline). RejectReason != null:
    /// expliziter Ablehnungsgrund. Beides null: unerwarteter Fehler (siehe Log) - die Verbindung wird ohne
    /// JoinRejected getrennt, wie jeder andere Disconnect ohne JoinAccepted (siehe CLAUDE.md-Konvention).</summary>
    private sealed record JoinLookupResult(int PeerId, Character? Character, JoinRejectReason? RejectReason);

    /// <summary>Eine zu speichernde Position, per Channel an den Hintergrund-Task übergeben (siehe
    /// RunPositionSaveConsumerAsync).</summary>
    private readonly record struct PositionSaveRequest(int CharacterId, string MapId, int X, int Y);

    /// <summary>Ein zu speichernder Yang-Gesamtbetrag (M4a), per Channel an den Hintergrund-Task übergeben
    /// (siehe RunYangSaveConsumerAsync) - eigener Channel statt Wiederverwendung des Positions-Channels
    /// (unterschiedlicher Auslöser: sofort bei jeder Änderung statt periodisch, siehe HandleAttackRequest).</summary>
    private readonly record struct YangSaveRequest(int CharacterId, long NewTotal);

    /// <summary>Ein voller Inventar-/Ausrüstungs-Zustand (M5a), per Channel an den Hintergrund-Task übergeben
    /// (siehe RunInventorySaveConsumerAsync) - eigener Channel statt Wiederverwendung von Position/Yang
    /// (siehe docs/tasks/M5a-items-equipment.md Abschnitt 0b): immer der volle Snapshot (alle belegten
    /// Slots + alle vier Equipped-Ids), kein Delta - DropOldest bleibt dadurch sicher, genau wie bei Yang
    /// konvergiert der zuletzt geschriebene Stand immer zum korrekten Endstand, ein übersprungener
    /// Zwischenstand ist unkritisch.</summary>
    private readonly record struct InventorySaveRequest(int CharacterId, List<(int SlotIndex, int ItemId, int UpgradeLevel)> Slots,
        int EquippedArmorItemId, int EquippedHelmetItemId, int EquippedWeaponItemId, int EquippedShieldItemId,
        int EquippedArmorUpgradeLevel, int EquippedHelmetUpgradeLevel, int EquippedWeaponUpgradeLevel, int EquippedShieldUpgradeLevel);

    /// <summary>Voller Yang- PLUS Inventar-/Ausrüstungs-Zustand (M5b, Kauf/Verkauf ändern beide zusammen),
    /// per Channel an den Hintergrund-Task übergeben (siehe RunCharacterSaveConsumerAsync) - eigener,
    /// dritter Channel statt Wiederverwendung von YangSaveRequest/InventorySaveRequest (siehe
    /// docs/tasks/M5b-shop.md Abschnitt 0d): der Consumer schreibt beides in EINEM SaveChangesAsync-Aufruf
    /// (EF Core fasst alle Änderungen eines SaveChangesAsync automatisch in einer Transaktion zusammen),
    /// damit ein Absturz zwischen zwei separaten Writes nie einen Yang-neu/Inventar-alt-Zwischenstand (oder
    /// umgekehrt) in der DB hinterlassen kann. Monster-Kill-Yang (nur Yang) und Equip/Unequip/Consumable/
    /// Drop (nur Inventar) bleiben bewusst auf ihren bestehenden, getrennten Channels - dort ändert sich
    /// jeweils nur eine der beiden Seiten, eine kombinierte Transaktion brächte dort keinen Vorteil. Seit M6a
    /// zusätzlich der volle Fortschritts-Stand (Level/Xp/Spent*/Unspent*/CurrentHp/CurrentSp, siehe
    /// docs/tasks/M6a-progression.md Abschnitt 0b/5) - Yang/Inventar UND Fortschritt ändern sich oft
    /// gemeinsam (Levelaufstieg heilt z. B. voll), eine gemeinsame Transaktion ist deshalb weiterhin sinnvoll.
    /// Neue Auslöser seit M6a: periodisch wie Position (siehe EnqueueDuePositionSaves) und bei Disconnect
    /// (siehe OnPeerDisconnected) - diese beiden decken die kontinuierlichen HP/SP-Änderungen durch Kampf/
    /// Regeneration ab, die bewusst KEIN eigenes sofortiges Save auslösen (siehe RunRegeneration/TryAttack).
    /// Seit M6b zusätzlich die gelernten Skillstufen (voller Snapshot wie Inventar, kein Delta) - Auslöser
    /// dort: sofort bei AllocateSkillPoint (analog zu AllocateStatPoint), sonst über denselben periodischen/
    /// Disconnect-Save wie der restliche Fortschritt (Skill-Cooldowns/aktive Buffs werden NICHT persistiert,
    /// siehe Entity.ActiveBuffs-Kommentar).</summary>
    private readonly record struct CharacterSaveRequest(int CharacterId, string MapId, long Yang, List<(int SlotIndex, int ItemId, int UpgradeLevel)> Slots,
        int EquippedArmorItemId, int EquippedHelmetItemId, int EquippedWeaponItemId, int EquippedShieldItemId,
        int EquippedArmorUpgradeLevel, int EquippedHelmetUpgradeLevel, int EquippedWeaponUpgradeLevel, int EquippedShieldUpgradeLevel,
        int Level, long Xp, int SpentSt, int SpentHt, int SpentDx, int SpentIq,
        int UnspentStatPoints, int UnspentSkillPoints, int CurrentHp, int CurrentSp,
        List<(int SkillId, int Level)> SkillLevels);

    private const int MaxConsecutiveFailedTicks = 50;

    /// <summary>Fix, keine Konfiguration (siehe CLAUDE.md "Bekannte Lücken") - alle 30s wird die Position
    /// jedes verbundenen Spielers gespeichert, zusätzlich sofort bei Disconnect.</summary>
    private const long PositionSaveIntervalMs = 30_000;

    /// <summary>Alpha-Umfang kennt nur eine Klasse (siehe CLAUDE.md) - keine eigene ClassId-Spalte auf
    /// Character, die Klasse wird deshalb fest über den Namen aufgelöst (siehe CompleteJoin). Sobald es
    /// mehrere Klassen gibt, braucht Character eine echte ClassId-Spalte statt dieser Konstante.</summary>
    private const string DefaultClassName = "Krieger";

    // Monotone Standard-Uhr, geteilt über alle Instanzen (läuft seit Prozessstart, nie negativ).
    private static readonly Stopwatch DefaultClock = Stopwatch.StartNew();
    private static long DefaultNowMs() => DefaultClock.ElapsedMilliseconds;

    private readonly ServerConfig _config;
    private readonly World _world;

    /// <summary>Multi-Map: alle Karten-Welten (Id -> World). Ohne MapCatalog (bestehende Tests) enthält das
    /// Dictionary nur die eine Legacy-World "village" - Routing/Portale funktionieren dann trivial.</summary>
    private readonly Dictionary<string, World> _worlds;
    private readonly MapCatalog? _mapCatalog;
    private readonly TokenService _tokenService;
    private readonly Func<LumiisDbContext> _dbContextFactory;
    private readonly ClassCatalog? _classCatalog;

    /// <summary>Nur für den Dev-Tools-Debug-Spawn gehalten (siehe HandleDebugSpawnMonster) - der reguläre
    /// Katalog-Spawn im Konstruktor braucht die Referenz danach nicht mehr.</summary>
    private readonly MobCatalog? _mobCatalog;

    /// <summary>Multi-Map: nur für den Start-Spawn der Karten (SpawnMapContent) gehalten - Laufzeit-Handler
    /// arbeiten mit den bereits auf der Entity gespeicherten Sortimenten/Definitionen.</summary>
    private readonly NpcCatalog? _npcCatalog;

    /// <summary>An World durchgereicht (siehe World-Konstruktor) UND hier für ToAppearance/CompleteJoin
    /// gehalten (Equipped-ItemId -> LookId auflösen, M5a) sowie für ShopContentPacket (BuyPrice je
    /// Sortiments-Item, siehe HandleOpenShopRequest - NpcCatalog selbst wird dafür nicht mehr gebraucht: das
    /// Sortiment sitzt bereits auf der Entity, siehe Entity.ShopItemIds/World.SpawnNpc).</summary>
    private readonly ItemCatalog? _itemCatalog;

    /// <summary>Für BuildCharacterStatsPacket gehalten (XpToNext, siehe dort) - an World durchgereicht
    /// (siehe World-Konstruktor) für AwardXp/TryDebugSetLevel.</summary>
    private readonly ProgressionCatalog? _progressionCatalog;
    private readonly UpgradeCatalog? _upgradeCatalog;
    private readonly NetManager _netManager;
    private readonly ILogger _logger;
    private readonly Func<long> _nowMs;
    private readonly Dictionary<int, ConnectionState> _connections = new();

    /// <summary>M7-Party: EntityId -> Gruppe und offene Einladungen (Invitee -> Inviter). Kein DB-Save.</summary>
    private readonly Dictionary<int, PartyState> _partyByEntityId = new();
    private readonly Dictionary<int, int> _pendingPartyInvites = new();
    private const int MaxPartySize = 8;
    private readonly ConcurrentQueue<JoinLookupResult> _pendingJoinResults = new();
    private readonly Channel<PositionSaveRequest> _positionSaveChannel;
    private readonly Channel<YangSaveRequest> _yangSaveChannel;
    private readonly Channel<InventorySaveRequest> _inventorySaveChannel;
    private readonly Channel<CharacterSaveRequest> _characterSaveChannel;
    private readonly CancellationTokenSource _positionSaveCts = new();
    private Task? _positionSaveConsumerTask;
    private Task? _yangSaveConsumerTask;
    private Task? _inventorySaveConsumerTask;
    private Task? _characterSaveConsumerTask;
    private uint _tick;
    private int _consecutiveFailedTicks;

    /// <summary>Nur für Tests: wenn gesetzt, wird es beim Verarbeiten von MoveTo aufgerufen (zum Simulieren eines Fehlers in einem Handler).</summary>
    internal Action? TestFaultHook { get; set; }

    /// <summary>nowMs: injizierbare Uhr für Fristen (Join-Timeout, Reject-Grace). Default: monotone Wandzeit seit Prozessstart. Ticks bleiben rein für die Simulation.
    /// dbContextFactory: erzeugt pro Hintergrund-Operation einen frischen LumiisDbContext (DbContext ist nicht thread-safe/langlebig) - World und Entities bleiben
    /// dabei ausschließlich dem Game-Loop-Thread vorbehalten, siehe World-Klassenkommentar.
    /// collisionMap: null bedeutet "überall begehbar" (z. B. Tests ohne Kollisionsbezug) - Program.cs lädt
    /// beim echten Serverstart immer eine Instanz aus data/maps/village/collision.bin (siehe CLAUDE.md).
    /// mobCatalog/spawnCatalog: null bedeutet "keine Monster" (z. B. bestehende Tests ohne Monster-Bezug) -
    /// Program.cs lädt beim echten Serverstart immer beide. Monster werden hier einmalig beim Serverstart
    /// angelegt (kein aktiver Respawn in M3c, siehe docs/tasks/M3c-monsters.md).
    /// classCatalog: null bedeutet "Spieler bekommen Entity.DefaultXxx-Kampfwerte" (z. B. bestehende Tests
    /// ohne Kampfbezug, siehe CompleteJoin) - Program.cs lädt beim echten Serverstart immer eine Instanz aus
    /// data/classes.json (M4a). itemCatalog: null bedeutet "keine Items" (z. B. bestehende Tests ohne
    /// Item-Bezug) - Ausrüstung wird dann als LookId 0 (leer) dargestellt und Equip/Unequip/UseConsumable
    /// schlagen defensiv fehl (siehe World.TryEquip etc.). Program.cs lädt beim echten Serverstart immer eine
    /// Instanz aus data/items.json (M5a). npcCatalog/npcSpawnCatalog: null bedeutet "keine NPCs" (z. B.
    /// bestehende Tests ohne Shop-Bezug) - Program.cs lädt beim echten Serverstart immer beide. NPCs werden
    /// hier einmalig beim Serverstart angelegt, analog zu Monstern (siehe docs/tasks/M5b-shop.md).
    /// progressionCatalog: null bedeutet "kein XP-Gewinn/keine XpToNext-Angabe" (z. B. bestehende Tests ohne
    /// Fortschrittsbezug, siehe World-Konstruktor) - Program.cs lädt beim echten Serverstart immer eine
    /// Instanz aus data/progression.json (M6a). skillCatalog: null bedeutet "keine Skills nutzbar" (z. B.
    /// bestehende Tests ohne Skillbezug, siehe World-Konstruktor) - Program.cs lädt beim echten Serverstart
    /// immer eine Instanz aus data/skills.json (M6b). Nur an World durchgereicht, GameServer selbst braucht
    /// den Katalog nicht (anders als z. B. itemCatalog für ToAppearance). upgradeCatalog: M6c, an World
    /// durchgereicht; null bedeutet "keine Aufwertung möglich" (bestehende Tests ohne Aufwertungsbezug).
    /// random: injizierbare Zufallsquelle (M6c, für deterministische Integrationstests des Aufwertungswurfs) -
    /// wird an World weitergereicht, Default dort eine echte Instanz.</summary>
    public GameServer(ServerConfig config, ILogger logger, TokenService tokenService, Func<LumiisDbContext> dbContextFactory,
        Func<long>? nowMs = null, CollisionMap? collisionMap = null, MobCatalog? mobCatalog = null, SpawnCatalog? spawnCatalog = null,
        ClassCatalog? classCatalog = null, ItemCatalog? itemCatalog = null, NpcCatalog? npcCatalog = null, NpcSpawnCatalog? npcSpawnCatalog = null,
        ProgressionCatalog? progressionCatalog = null, SkillCatalog? skillCatalog = null, MapCatalog? mapCatalog = null,
        UpgradeCatalog? upgradeCatalog = null, Random? random = null, MetinCatalog? metinCatalog = null)
    {
        _config = config;
        _logger = logger;
        _tokenService = tokenService;
        _dbContextFactory = dbContextFactory;
        _classCatalog = classCatalog;
        _mobCatalog = mobCatalog;
        _npcCatalog = npcCatalog;
        _itemCatalog = itemCatalog;
        _progressionCatalog = progressionCatalog;
        _mapCatalog = mapCatalog;
        _upgradeCatalog = upgradeCatalog;

        // Gemeinsamer EntityId-Zähler über ALLE Karten: EntityIds müssen global eindeutig bleiben, sonst
        // könnte eine Entity in Karte A dieselbe Id haben wie eine in Karte B (der Client/das Routing kennt
        // nur eine Id -> MapId-Zuordnung). Ohne MapCatalog bleibt es bei genau einer World.
        int nextEntityId = 0;
        Func<int> allocateEntityId = () => ++nextEntityId;


        _netManager = new NetManager(this) { UpdateTime = 15 };
        _nowMs = nowMs ?? DefaultNowMs;

        if (mapCatalog != null)
        {
            _worlds = new Dictionary<string, World>(StringComparer.Ordinal);
            foreach (MapDefinition map in mapCatalog.Maps)
            {
                var world = new World(config.MaxPlayers, collisionMap: null, random: random, itemCatalog: itemCatalog,
                    progressionCatalog: progressionCatalog, skillCatalog: skillCatalog,
                    mapDefinition: map, entityIdGenerator: allocateEntityId, upgradeCatalog: upgradeCatalog,
                    mobCatalog: mobCatalog, metinCatalog: metinCatalog);
                _worlds[map.Id] = world;
                SpawnMapContent(world, map.Spawns, map.NpcPlacements);
            }
            _world = _worlds[mapCatalog.StartMapId];
        }
        else
        {
            // Legacy-Pfad (bestehende Tests/Serverstarts ohne MapCatalog): eine World "village" mit den
            // bisherigen optionalen Katalogen - Verhalten unverändert.
            _world = new World(config.MaxPlayers, collisionMap, random: random, itemCatalog: itemCatalog,
                progressionCatalog: progressionCatalog, skillCatalog: skillCatalog,
                entityIdGenerator: allocateEntityId, upgradeCatalog: upgradeCatalog,
                mobCatalog: mobCatalog, metinCatalog: metinCatalog);
            _worlds = new Dictionary<string, World>(StringComparer.Ordinal) { { _world.MapId, _world } };
            if (spawnCatalog != null && mobCatalog != null)
                SpawnMapContent(_world, spawnCatalog.Spawns, Array.Empty<NpcPlacement>());
            if (npcSpawnCatalog != null && npcCatalog != null)
                SpawnMapContent(_world, Array.Empty<SpawnPoint>(), npcSpawnCatalog.Placements);
        }

        // Kapazität = MaxPlayers: höchstens einmal pro Spieler pro 30s-Zyklus (oder einmalig bei
        // Disconnect) wird ein Save enqueued, mehr gleichzeitig ausstehende Anfragen sollten unter
        // normalem Betrieb nie vorkommen. FullMode=DropOldest statt eines blockierenden Writers, falls
        // der Hintergrund-Task doch einmal ins Hintertreffen gerät (z. B. DB kurzzeitig langsam): die
        // älteste, noch nicht geschriebene Position wird verworfen, die neueste gewinnt - Positionsdaten
        // sind ohnehin "best effort" (siehe CLAUDE.md "Bekannte Lücken"), nur der letzte bekannte Stand
        // zählt, ein übersprungener Zwischenstand ist unkritisch. GameServer selbst ist der einzige
        // Schreiber (immer vom Game-Loop-Thread: periodisch aus RunTick, sofort aus OnPeerDisconnected).
        _positionSaveChannel = Channel.CreateBounded<PositionSaveRequest>(new BoundedChannelOptions(config.MaxPlayers)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = true
        });

        // Gleicher Mechanismus wie _positionSaveChannel, aber eigener Channel (siehe YangSaveRequest-
        // Kommentar): NewTotal ist immer der volle, aktuelle Stand (kein Delta) - DropOldest verwirft
        // höchstens einen Zwischenstand, der zuletzt geschriebene Wert konvergiert trotzdem immer zum
        // korrekten Endstand, genau wie bei Positionen.
        _yangSaveChannel = Channel.CreateBounded<YangSaveRequest>(new BoundedChannelOptions(config.MaxPlayers)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = true
        });

        // Gleicher Mechanismus, eigener Channel (siehe InventorySaveRequest-Kommentar) - immer ein voller
        // Snapshot, DropOldest bleibt sicher.
        _inventorySaveChannel = Channel.CreateBounded<InventorySaveRequest>(new BoundedChannelOptions(config.MaxPlayers)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = true
        });

        // Gleicher Mechanismus, eigener Channel (siehe CharacterSaveRequest-Kommentar) - immer ein voller
        // Yang- plus Inventar-Snapshot, DropOldest bleibt sicher.
        _characterSaveChannel = Channel.CreateBounded<CharacterSaveRequest>(new BoundedChannelOptions(config.MaxPlayers)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = true
        });
    }

    /// <summary>Legt Monster/NPCs einer Karte an (Serverstart). Katalog-Existenz wurde von
    /// SpawnCatalog/NpcSpawnCatalog (bzw. MapCatalog.LoadFromDirectory) bereits validiert - hier nur noch
    /// die Entity-Erzeugung, identisch zum bisherigen Ein-Karten-Pfad.</summary>
    private void SpawnMapContent(World world, IReadOnlyList<SpawnPoint> spawns, IReadOnlyList<NpcPlacement> npcPlacements)
    {
        if (_mobCatalog != null)
        {
            foreach (SpawnPoint spawn in spawns)
            {
                _mobCatalog.TryGet(spawn.MobId, out MobDefinition mobDef);
                world.SpawnMonster(spawn.MobId, mobDef.Name, spawn.PositionX, spawn.PositionY, mobDef.SpeedWire,
                    mobDef.WanderRadiusWire, mobDef.WanderIntervalMinMs, mobDef.WanderIntervalMaxMs, _nowMs(),
                    mobDef.MaxHp, mobDef.Attack, mobDef.Defense, mobDef.YangMin, mobDef.YangMax,
                    spawnPointId: spawn.Id, respawnSeconds: spawn.RespawnSeconds,
                    meleeRangeWire: mobDef.MeleeRangeWire, attackCooldownMs: mobDef.AttackCooldownMs,
                    aggressive: mobDef.Aggressive, aggroRangeWire: mobDef.AggroRangeWire, leashRangeWire: mobDef.LeashRangeWire,
                    drops: mobDef.Drops.Select(d => (d.ItemId, d.ChancePercent)).ToList(),
                    level: mobDef.Level, baseXp: mobDef.BaseXp);
            }
        }

        if (_npcCatalog != null)
        {
            foreach (NpcPlacement placement in npcPlacements)
            {
                _npcCatalog.TryGet(placement.NpcId, out NpcDefinition npcDef);
                world.SpawnNpc(placement.NpcId, npcDef.Name, placement.PositionX, placement.PositionY, npcDef.Appearance, npcDef.ShopItemIds);
            }
        }
    }

    /// <summary>Tatsächlich gebundener Port (nach Start relevant, falls ServerConfig.Port 0 war).</summary>
    public int ListenPort => _netManager.LocalPort;

    /// <summary>Nur für Tests: direkter Zugriff auf eine Karten-World (Monster-/Entity-Trennung prüfen).</summary>
    internal World WorldForMap(string mapId) => _worlds[mapId];

    /// <summary>Nur für Tests: Karte, auf der eine Entity aktuell steht (null = unbekannt).</summary>
    internal string? MapIdOfEntity(int entityId) => FindWorldOfEntity(entityId)?.MapId;

    public void Start()
    {
        _netManager.Start(_config.Port);
        _positionSaveConsumerTask = Task.Run(() => RunPositionSaveConsumerAsync(_positionSaveCts.Token));
        _yangSaveConsumerTask = Task.Run(() => RunYangSaveConsumerAsync(_positionSaveCts.Token));
        _inventorySaveConsumerTask = Task.Run(() => RunInventorySaveConsumerAsync(_positionSaveCts.Token));
        _characterSaveConsumerTask = Task.Run(() => RunCharacterSaveConsumerAsync(_positionSaveCts.Token));
        _logger.Info($"Server gestartet auf Port {_netManager.LocalPort} (max. {_config.MaxPlayers} Spieler)");
    }

    public void Stop()
    {
        _netManager.Stop();
        _positionSaveChannel.Writer.TryComplete();
        _yangSaveChannel.Writer.TryComplete();
        _inventorySaveChannel.Writer.TryComplete();
        _characterSaveChannel.Writer.TryComplete();
        _positionSaveCts.Cancel();
        _logger.Info("Server gestoppt");
    }

    /// <summary>Ein Tick: Netzwerkereignisse verarbeiten, Timeouts prüfen, Bewegung fortschreiben. Manuell aufrufbar für Tests.
    /// Fängt Fehler im gesamten Tick ab und loggt sie, damit ein einzelner kaputter Tick den Server nicht beendet;
    /// erst nach MaxConsecutiveFailedTicks in Folge wird sauber (Fail-fast) abgebrochen.</summary>
    public void Update()
    {
        try
        {
            RunTick();
            _consecutiveFailedTicks = 0;
        }
        catch (Exception ex)
        {
            _consecutiveFailedTicks++;
            _logger.Error($"Fehler in Tick {_tick} ({_consecutiveFailedTicks}/{MaxConsecutiveFailedTicks} in Folge): {ex}");

            if (_consecutiveFailedTicks >= MaxConsecutiveFailedTicks)
            {
                throw new GameServerFatalException(
                    $"{MaxConsecutiveFailedTicks} aufeinanderfolgende fehlerhafte Ticks - Server wird beendet.", ex);
            }
        }
    }

    private void RunTick()
    {
        _netManager.PollEvents();
        DrainPendingJoinResults();
        _tick++;

        if (_connections.Count > 0)
        {
            long now = _nowMs();
            foreach (ConnectionState conn in new List<ConnectionState>(_connections.Values))
            {
                bool awaitingDeadline = conn.Closing || conn.EntityId == null;
                if (awaitingDeadline && now >= conn.CloseAtMs)
                {
                    _logger.Info($"Verbindung {conn.Peer.Id} getrennt ({(conn.Closing ? "nach JoinRejected" : "Join-Timeout")})");
                    conn.Peer.Disconnect();
                }
            }
        }

        double deltaSeconds = 1.0 / Protocol.TickRate;
        long tickNowMs = _nowMs();
        List<Entity> arrived = new();
        List<Entity> startedWandering = new();
        List<Entity> respawned = new();
        List<Entity> respawnedMonsters = new();
        List<(int AttackerEntityId, int TargetEntityId, AttackResult Result)> monsterAttacks = new();
        List<Entity> regeneratedPlayers = new();
        List<Entity> buffsExpired = new();

        // Multi-Map: jede Karte hat ihre eigene World (Entities, Kollision, Spawns, Monster-KI). Ticks werden
        // nacheinander ausgeführt; die Ergebnislisten werden zusammengeführt, weil die Paket-/Persistenz-Seite
        // darunter unverändert pro Entity arbeitet (SendToVisible filtert zusätzlich nach Karte).
        foreach (World world in _worlds.Values)
        {
            (List<Entity> worldArrived, List<Entity> worldStartedWandering, List<Entity> worldRespawned,
                List<Entity> worldRespawnedMonsters,
                List<(int AttackerEntityId, int TargetEntityId, AttackResult Result)> worldMonsterAttacks,
                List<Entity> worldRegeneratedPlayers, List<Entity> worldBuffsExpired) = world.Tick(deltaSeconds, tickNowMs);

            arrived.AddRange(worldArrived);
            startedWandering.AddRange(worldStartedWandering);
            respawned.AddRange(worldRespawned);
            respawnedMonsters.AddRange(worldRespawnedMonsters);
            monsterAttacks.AddRange(worldMonsterAttacks);
            regeneratedPlayers.AddRange(worldRegeneratedPlayers);
            buffsExpired.AddRange(worldBuffsExpired);
        }

        foreach (Entity entity in arrived)
            SendToVisible(entity.Id, new EntityStopPacket { EntityId = entity.Id, X = entity.IntX, Y = entity.IntY });

        // Spieler-Respawn (M4a): kein neues Paket nötig - EntityStopPacket bedeutet "an dieser Position zum
        // Stillstand gekommen" und der Client setzt die Position beim Empfang hart, exakt die
        // Teleport-Semantik (siehe docs/tasks/M4a-combat-core.md Abschnitt 5).
        foreach (Entity entity in respawned)
        {
            SendToVisible(entity.Id, new EntityStopPacket { EntityId = entity.Id, X = entity.IntX, Y = entity.IntY });
            // Bugfix nach M5a: Respawn setzt CurrentHp = MaxHp (siehe World.Tick), ohne dieses Paket bliebe
            // die Leben-Anzeige beim Client auf 0 stehen (letzter Stand vor dem Tod).
            SendToVisible(entity.Id, new EntityHpChangedPacket { EntityId = entity.Id, CurrentHp = entity.CurrentHp, MaxHp = entity.MaxHp });
            SendPartyChangedForEntity(entity.Id);
            // M6a: Respawn heilt HP UND SP voll (siehe World.Tick) - CharacterStatsPacket nur an den
            // Besitzer, nicht an AOI-Beobachter (anders als die beiden Pakete oben).
            FindConnectionByEntityId(entity.Id)?.Peer.SendPacket(BuildCharacterStatsPacket(entity));
        }

        // M6a: natürliche Regeneration (siehe World.RunRegeneration) - nur an den Besitzer, nur wenn sich
        // HP/SP tatsächlich geändert haben (World filtert das schon, siehe Tick-Kommentar). Kein
        // CharacterSaveRequest hier (bewusst kein Sofort-Speicher-Auslöser für Regeneration, siehe
        // docs/tasks/M6a-progression.md Abschnitt 5 - periodischer/Disconnect-Save holt den Stand ein).
        foreach (Entity entity in regeneratedPlayers)
            FindConnectionByEntityId(entity.Id)?.Peer.SendPacket(BuildCharacterStatsPacket(entity));

        // M6b: natürlich abgelaufene Buffs (siehe World.Tick) - ActiveBuffsChangedPacket mit dem
        // aktualisierten (ggf. leeren) Stand, plus CharacterStatsPacket (Attack hat sich durch
        // RecomputeDerivedStats in World.Tick bereits geändert, falls Kampfrausch betroffen war). Nur an den
        // Besitzer, wie bei Regeneration/Respawn oben.
        foreach (Entity entity in buffsExpired)
        {
            ConnectionState? conn = FindConnectionByEntityId(entity.Id);
            if (conn == null) continue;
            conn.Peer.SendPacket(BuildActiveBuffsChangedPacket(entity, tickNowMs));
            conn.Peer.SendPacket(BuildCharacterStatsPacket(entity));
        }

        // Wander-KI (World.Tick) hat fälligen Monstern ein neues Ziel gesetzt - dieselbe EntityMove-
        // Benachrichtigung wie bei einem Spieler-MoveTo (HandleMoveTo), nur ohne eingehendes Paket als Auslöser.
        foreach (Entity entity in startedWandering)
        {
            SendToVisible(entity.Id, new EntityMovePacket
            {
                EntityId = entity.Id,
                StartX = entity.IntX,
                StartY = entity.IntY,
                TargetX = entity.TargetX,
                TargetY = entity.TargetY,
                Speed = entity.Speed,
                ServerTick = _tick
            });
        }

        // Monster-Respawn (M4b): kein eigener Broadcast nötig - die neue Entity ist ab jetzt Teil von World,
        // der gleich folgende UpdateVisibility()-Diff erkennt sie für jede Verbindung in AOI-Reichweite als
        // "neu sichtbar" und verschickt EntitySpawn ganz normal (derselbe Mechanismus wie beim Dev-Tools-
        // Debug-Spawn und beim Katalog-Spawn eines neu beitretenden Spielers - kein Sonderfall). Hier nur
        // geloggt.
        foreach (Entity monster in respawnedMonsters)
            _logger.Info($"Monster {monster.Name} (Entity {monster.Id}) an SpawnPoint {monster.SpawnPointId} respawnt");

        // Monster-initiierte Angriffe (M4c, siehe World.RunMonsterAi) - dieselben Pakete wie bei einem
        // spielerinitiierten Angriff (siehe HandleAttackRequest/BroadcastAttackResult), nur ohne Yang-
        // Gutschrift (YangAwarded ist bei einem Monster als Angreifer laut World.ApplyDamage immer 0 - nur
        // ein sterbendes MONSTER zahlt Yang aus, hier ist das Ziel immer ein Spieler) und ohne AttackFailed
        // (es gibt keine Verbindung des Angreifers, an die das ginge - fehlgeschlagene Versuche filtert
        // World.RunMonsterAi bereits vor der Aufnahme in die Liste heraus).
        foreach ((int attackerEntityId, int targetEntityId, AttackResult result) in monsterAttacks)
        {
            BroadcastAttackResult(attackerEntityId, targetEntityId, result);
            _logger.Info($"Entity {attackerEntityId} trifft Entity {targetEntityId} ({result.Damage} Schaden)" +
                (result.TargetDied ? ", Ziel gestorben" : ""));
        }

        // Multi-Map: nach der Bewegung prüfen, ob ein Spieler in einem Portal seiner Karte steht. Der Wechsel
        // entfernt die Entity aus der alten World, setzt sie in die neue und schickt MapChangedPacket; die
        // AOI-Buchhaltung wird beim Wechsel geleert (siehe TransitionEntityToMap).
        ProcessPortalTransitions(tickNowMs);

        UpdateVisibility();
        EnqueueDuePositionSaves();
    }

    /// <summary>Prüft pro Karte alle Spieler auf Portalberührung und wechselt die Karte. Reine
    /// Welt-Entscheidung liegt in World.CheckPortal (inkl. Cooldown) - hier nur die kartenübergreifende
    /// Orchestrierung (GameServer hält alle Worlds).</summary>
    private void ProcessPortalTransitions(long nowMs)
    {
        foreach (World world in _worlds.Values)
        {
            List<Entity> players = world.Entities.Where(e => e.Kind == EntityKind.Player).ToList();
            foreach (Entity player in players)
            {
                PortalDefinition? portal = world.CheckPortal(player, nowMs);
                if (portal == null) continue;
                if (!_worlds.TryGetValue(portal.TargetMapId, out World? targetWorld)) continue; // von MapCatalog validiert
                TransitionEntityToMap(player, world, targetWorld, portal.TargetX, portal.TargetY, nowMs);
            }
        }
    }

    /// <summary>Versetzt eine Spieler-Entity auf eine andere Karte. Reihenfolge ist wichtig:
    /// 1. alte World entfernen, 2. alte AOI-Beobachter per Despawn informieren, 3. Position/MapId in der
    /// Entity setzen und in die neue World legen, 4. Verbindung umhängen und ihre Sichtbarkeitsliste leeren
    /// (der Client leert beim MapChanged ohnehin alles, sonst kämen falsche Despawns), 5. MapChangedPacket
    /// mit bereits gesetzter Zielposition senden, 6. sofort Position+MapId persistieren. Fremde Entities der
    /// neuen Karte erscheinen danach über den normalen AOI-Diff als EntitySpawn.</summary>
    private void TransitionEntityToMap(Entity entity, World fromWorld, World toWorld, int targetX, int targetY, long nowMs)
    {
        fromWorld.Remove(entity.Id);
        BroadcastDespawn(entity.Id);

        entity.StopMoving();
        entity.TargetEntityId = null;
        entity.Teleport(targetX, targetY);
        toWorld.AddExistingEntity(entity);

        ConnectionState? conn = FindConnectionByEntityId(entity.Id);
        if (conn != null)
        {
            conn.World = toWorld;
            conn.VisibleEntityIds.Clear();
            conn.Peer.SendPacket(new MapChangedPacket
            {
                EntityId = entity.Id,
                X = entity.IntX,
                Y = entity.IntY,
                MapId = toWorld.MapId,
                MapName = _mapCatalog?.Get(toWorld.MapId).Name ?? toWorld.MapId,
                MapWidth = GameConstants.MapWidth,
                MapHeight = GameConstants.MapHeight
            });
        }

        if (entity.CharacterId is int characterId)
        {
            _positionSaveChannel.Writer.TryWrite(new PositionSaveRequest(characterId, toWorld.MapId, entity.IntX, entity.IntY));
            _characterSaveChannel.Writer.TryWrite(BuildCharacterSaveRequest(entity));
        }

        _logger.Info($"{entity.Name} (Entity {entity.Id}) wechselt Karte {fromWorld.MapId} -> {toWorld.MapId} bei ({entity.IntX},{entity.IntY})");
    }

    /// <summary>Nicht-blockierend (Channel.Writer.TryWrite): reine Buchhaltung + Enqueue, kein DB-Zugriff
    /// auf dem Game-Loop-Thread. Siehe RunPositionSaveConsumerAsync für den eigentlichen Schreibvorgang.</summary>
    private void EnqueueDuePositionSaves()
    {
        long now = _nowMs();
        foreach (World world in _worlds.Values)
        {
            foreach (Entity entity in SelectDueForSave(world.Entities, now, PositionSaveIntervalMs))
            {
                entity.LastSaveAtMs = now;
                _positionSaveChannel.Writer.TryWrite(new PositionSaveRequest(entity.CharacterId!.Value, world.MapId, entity.IntX, entity.IntY));

                // M6a (siehe docs/tasks/M6a-progression.md Abschnitt 0b/5): dieselbe 30s-Fälligkeit wie Position -
                // damit die kontinuierlichen HP/SP-Änderungen durch Kampf/Regeneration (bewusst OHNE eigenes
                // sofortiges Save, siehe RunRegeneration/World.TryAttack) trotzdem regelmäßig persistiert werden.
                _characterSaveChannel.Writer.TryWrite(BuildCharacterSaveRequest(entity));
            }
        }
    }

    /// <summary>Reine Entscheidung (kein DB-, kein Netzwerkzugriff): welche Entities sind fällig (letzter
    /// Save mindestens intervalMs her)? Nur Spieler (CharacterId gesetzt) - Monster haben keine DB-Zeile, für
    /// sie gibt es nichts zu speichern. Statisch und pur, damit ohne echten GameServer/DB testbar.</summary>
    internal static List<Entity> SelectDueForSave(IEnumerable<Entity> entities, long nowMs, long intervalMs)
    {
        return entities.Where(e => e.CharacterId.HasValue && nowMs - e.LastSaveAtMs >= intervalMs).ToList();
    }

    /// <summary>Einmal pro Tick: World.ComputeVisibility aufrufen und pro Verbindung gegen den letzten
    /// bekannten Stand (ConnectionState.VisibleEntityIds) diffen. Neu sichtbar: EntitySpawn (plus
    /// EntityMove ab der aktuellen Position, falls die Entity gerade in Bewegung ist - gleiches Muster wie
    /// beim Join in CompleteJoin für bereits laufende Entities). Nicht mehr sichtbar: EntityDespawn.
    /// Weiterhin sichtbar: keine Aktion hier, die normalen Move/Stop-Broadcasts decken das ab (siehe
    /// HandleMoveTo/RunTick - eingeschränkt auf sichtbare Verbindungen in Abschnitt 4 dieser Aufgabe).</summary>
    private void UpdateVisibility()
    {
        // Multi-Map: Sichtbarkeit wird pro Karte berechnet und nur an Verbindungen DIESER Karte verteilt -
        // EntityIds sind zwar global eindeutig, aber eine Entity auf Karte B darf für einen Spieler auf Karte A
        // nie sichtbar werden. Verbindungen ohne World (vor dem Join) werden übersprungen.
        foreach (World world in _worlds.Values)
        {
            List<ConnectionState> worldConnections = _connections.Values
                .Where(c => c.EntityId.HasValue && ReferenceEquals(c.World, world)).ToList();
            if (worldConnections.Count == 0) continue;

            Dictionary<int, HashSet<int>> visibility = world.ComputeVisibility(GameConstants.AoiRadiusWire);

            foreach (ConnectionState conn in worldConnections)
            {
                if (conn.EntityId is not int selfId) continue;

                HashSet<int> nowVisible = visibility.TryGetValue(selfId, out HashSet<int>? v) ? v : new HashSet<int>();

                foreach (int otherId in nowVisible)
                {
                    if (conn.VisibleEntityIds.Contains(otherId)) continue;
                    if (!world.TryGet(otherId, out Entity? other)) continue; // sollte nicht vorkommen (Entity gerade erst in ComputeVisibility gesehen)

                    conn.Peer.SendPacket(new EntitySpawnPacket { EntityId = other!.Id, Name = other.Name, X = other.IntX, Y = other.IntY, Appearance = other.Appearance, Kind = other.Kind, TypeId = other.TypeId });
                    SendHpIfDamaged(conn.Peer, other);
                    if (other.IsMoving)
                    {
                        conn.Peer.SendPacket(new EntityMovePacket
                        {
                            EntityId = other.Id,
                            StartX = other.IntX,
                            StartY = other.IntY,
                            TargetX = other.TargetX,
                            TargetY = other.TargetY,
                            Speed = other.Speed,
                            ServerTick = _tick
                        });
                    }
                }

                foreach (int otherId in conn.VisibleEntityIds)
                {
                    if (!nowVisible.Contains(otherId))
                        conn.Peer.SendPacket(new EntityDespawnPacket { EntityId = otherId });
                }

                conn.VisibleEntityIds.Clear();
                conn.VisibleEntityIds.UnionWith(nowVisible);
            }
        }
    }

    /// <summary>Läuft auf einem Threadpool-Thread, NIE auf dem Game-Loop-Thread. Fehler werden geloggt,
    /// bringen den Server nicht zum Absturz (bestehende Fehlerbehandlungs-Konvention) - eine einzelne
    /// fehlgeschlagene Positions-Speicherung ist nicht kritisch, der nächste 30s-Zyklus versucht es erneut.</summary>
    private async Task RunPositionSaveConsumerAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (PositionSaveRequest request in _positionSaveChannel.Reader.ReadAllAsync(cancellationToken))
            {
                try
                {
                    await using LumiisDbContext db = _dbContextFactory();
                    Character? character = await db.Characters.FindAsync(new object[] { request.CharacterId }, cancellationToken);
                    if (character == null) continue; // Charakter inzwischen gelöscht o. Ä. - kein Fehler, einfach überspringen.

                    character.PositionX = request.X;
                    character.PositionY = request.Y;
                    character.MapId = request.MapId;
                    await db.SaveChangesAsync(cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.Error($"Fehler beim Speichern der Position für Charakter {request.CharacterId}: {ex}");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Erwarteter Abbruch beim Stop() - kein Fehler.
        }
    }

    /// <summary>Läuft auf einem Threadpool-Thread, NIE auf dem Game-Loop-Thread (M4a, siehe HandleAttackRequest).
    /// Fehler werden geloggt, bringen den Server nicht zum Absturz - eine einzelne fehlgeschlagene
    /// Yang-Speicherung ist nicht kritisch, der nächste Kill für diesen Charakter versucht es erneut mit dem
    /// dann aktuellen (höheren) Stand.</summary>
    private async Task RunYangSaveConsumerAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (YangSaveRequest request in _yangSaveChannel.Reader.ReadAllAsync(cancellationToken))
            {
                try
                {
                    await using LumiisDbContext db = _dbContextFactory();
                    Character? character = await db.Characters.FindAsync(new object[] { request.CharacterId }, cancellationToken);
                    if (character == null) continue; // Charakter inzwischen gelöscht o. Ä. - kein Fehler, einfach überspringen.

                    character.Yang = request.NewTotal;
                    await db.SaveChangesAsync(cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.Error($"Fehler beim Speichern von Yang für Charakter {request.CharacterId}: {ex}");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Erwarteter Abbruch beim Stop() - kein Fehler.
        }
    }

    /// <summary>Läuft auf einem Threadpool-Thread, NIE auf dem Game-Loop-Thread (M5a). Ersetzt alle
    /// CharacterInventoryItem-Zeilen des Charakters durch den übergebenen vollen Snapshot (kein Delta,
    /// siehe InventorySaveRequest) und schreibt die vier Equipped-Spalten - eine Transaktion pro Save über
    /// SaveChangesAsync. Fehler werden geloggt, bringen den Server nicht zum Absturz - der nächste Save für
    /// diesen Charakter (nächste Inventaränderung) versucht es erneut mit dem dann aktuellen Stand.</summary>
    private async Task RunInventorySaveConsumerAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (InventorySaveRequest request in _inventorySaveChannel.Reader.ReadAllAsync(cancellationToken))
            {
                try
                {
                    await using LumiisDbContext db = _dbContextFactory();
                    Character? character = await db.Characters.Include(c => c.InventoryItems)
                        .SingleOrDefaultAsync(c => c.Id == request.CharacterId, cancellationToken);
                    if (character == null) continue; // Charakter inzwischen gelöscht o. Ä. - kein Fehler, einfach überspringen.

                    character.EquippedArmorItemId = request.EquippedArmorItemId == 0 ? null : request.EquippedArmorItemId;
                    character.EquippedHelmetItemId = request.EquippedHelmetItemId == 0 ? null : request.EquippedHelmetItemId;
                    character.EquippedWeaponItemId = request.EquippedWeaponItemId == 0 ? null : request.EquippedWeaponItemId;
                    character.EquippedShieldItemId = request.EquippedShieldItemId == 0 ? null : request.EquippedShieldItemId;
                    character.EquippedArmorUpgradeLevel = request.EquippedArmorItemId == 0 ? 0 : request.EquippedArmorUpgradeLevel;
                    character.EquippedHelmetUpgradeLevel = request.EquippedHelmetItemId == 0 ? 0 : request.EquippedHelmetUpgradeLevel;
                    character.EquippedWeaponUpgradeLevel = request.EquippedWeaponItemId == 0 ? 0 : request.EquippedWeaponUpgradeLevel;
                    character.EquippedShieldUpgradeLevel = request.EquippedShieldItemId == 0 ? 0 : request.EquippedShieldUpgradeLevel;

                    db.CharacterInventoryItems.RemoveRange(character.InventoryItems);
                    foreach ((int slotIndex, int itemId, int upgradeLevel) in request.Slots)
                        character.InventoryItems.Add(new CharacterInventoryItem { CharacterId = request.CharacterId, SlotIndex = slotIndex, ItemId = itemId, UpgradeLevel = upgradeLevel });

                    await db.SaveChangesAsync(cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.Error($"Fehler beim Speichern des Inventars für Charakter {request.CharacterId}: {ex}");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Erwarteter Abbruch beim Stop() - kein Fehler.
        }
    }

    /// <summary>Läuft auf einem Threadpool-Thread, NIE auf dem Game-Loop-Thread (M5b, siehe
    /// HandleBuyItemRequest/HandleSellItemRequest). Schreibt Yang PLUS das komplette Inventar/die Ausrüstung
    /// in einem einzigen SaveChangesAsync-Aufruf - EF Core fasst das automatisch in einer Transaktion
    /// zusammen (siehe CharacterSaveRequest-Kommentar), ein Absturz mittendrin kann also nie einen
    /// halb-gespeicherten Zustand hinterlassen. Fehler werden geloggt, bringen den Server nicht zum Absturz -
    /// der nächste Kauf/Verkauf dieses Charakters versucht es erneut mit dem dann aktuellen Stand.</summary>
    private async Task RunCharacterSaveConsumerAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (CharacterSaveRequest request in _characterSaveChannel.Reader.ReadAllAsync(cancellationToken))
            {
                try
                {
                    await using LumiisDbContext db = _dbContextFactory();
                    Character? character = await db.Characters.Include(c => c.InventoryItems).Include(c => c.SkillLevels)
                        .SingleOrDefaultAsync(c => c.Id == request.CharacterId, cancellationToken);
                    if (character == null) continue; // Charakter inzwischen gelöscht o. Ä. - kein Fehler, einfach überspringen.

                    character.Yang = request.Yang;
                    character.MapId = request.MapId;
                    character.EquippedArmorItemId = request.EquippedArmorItemId == 0 ? null : request.EquippedArmorItemId;
                    character.EquippedHelmetItemId = request.EquippedHelmetItemId == 0 ? null : request.EquippedHelmetItemId;
                    character.EquippedWeaponItemId = request.EquippedWeaponItemId == 0 ? null : request.EquippedWeaponItemId;
                    character.EquippedShieldItemId = request.EquippedShieldItemId == 0 ? null : request.EquippedShieldItemId;
                    character.EquippedArmorUpgradeLevel = request.EquippedArmorItemId == 0 ? 0 : request.EquippedArmorUpgradeLevel;
                    character.EquippedHelmetUpgradeLevel = request.EquippedHelmetItemId == 0 ? 0 : request.EquippedHelmetUpgradeLevel;
                    character.EquippedWeaponUpgradeLevel = request.EquippedWeaponItemId == 0 ? 0 : request.EquippedWeaponUpgradeLevel;
                    character.EquippedShieldUpgradeLevel = request.EquippedShieldItemId == 0 ? 0 : request.EquippedShieldUpgradeLevel;

                    // M6a: voller Fortschritts-Stand, siehe CharacterSaveRequest-Kommentar.
                    character.Level = request.Level;
                    character.Xp = request.Xp;
                    character.SpentSt = request.SpentSt;
                    character.SpentHt = request.SpentHt;
                    character.SpentDx = request.SpentDx;
                    character.SpentIq = request.SpentIq;
                    character.UnspentStatPoints = request.UnspentStatPoints;
                    character.UnspentSkillPoints = request.UnspentSkillPoints;
                    character.CurrentHp = request.CurrentHp;
                    character.CurrentSp = request.CurrentSp;

                    db.CharacterInventoryItems.RemoveRange(character.InventoryItems);
                    foreach ((int slotIndex, int itemId, int upgradeLevel) in request.Slots)
                        character.InventoryItems.Add(new CharacterInventoryItem { CharacterId = request.CharacterId, SlotIndex = slotIndex, ItemId = itemId, UpgradeLevel = upgradeLevel });

                    // M6b: voller Skillstufen-Snapshot, kein Delta - gleiches Muster wie Inventar oben.
                    db.CharacterSkillLevels.RemoveRange(character.SkillLevels);
                    foreach ((int skillId, int level) in request.SkillLevels)
                        character.SkillLevels.Add(new CharacterSkillLevel { CharacterId = request.CharacterId, SkillId = skillId, Level = level });

                    await db.SaveChangesAsync(cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.Error($"Fehler beim Speichern von Yang+Inventar+Fortschritt+Skills für Charakter {request.CharacterId}: {ex}");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Erwarteter Abbruch beim Stop() - kein Fehler.
        }
    }

    /// <summary>Fester 25-Hz-Loop (Stopwatch, Catch-up begrenzt), bis der Token abgebrochen wird.</summary>
    public void Run(CancellationToken cancellationToken)
    {
        const int maxCatchUpTicks = 5;
        double tickInterval = 1.0 / Protocol.TickRate;
        Stopwatch stopwatch = Stopwatch.StartNew();
        double nextTick = tickInterval;

        while (!cancellationToken.IsCancellationRequested)
        {
            int ticksThisIteration = 0;
            while (stopwatch.Elapsed.TotalSeconds >= nextTick && ticksThisIteration < maxCatchUpTicks)
            {
                Update();
                nextTick += tickInterval;
                ticksThisIteration++;
            }

            if (ticksThisIteration >= maxCatchUpTicks)
                nextTick = stopwatch.Elapsed.TotalSeconds + tickInterval;

            double sleepSeconds = nextTick - stopwatch.Elapsed.TotalSeconds;
            if (sleepSeconds > 0)
                Thread.Sleep((int)(sleepSeconds * 1000));
        }
    }

    // ---- INetEventListener: rohe Verbindungs-Events, direkt in die Logik übersetzt ----

    public void OnConnectionRequest(ConnectionRequest request)
    {
        try
        {
            request.AcceptIfKey(_config.ConnectionKey);
        }
        catch (Exception ex)
        {
            _logger.Error($"Fehler in OnConnectionRequest: {ex}");
        }
    }

    public void OnPeerConnected(NetPeer peer)
    {
        try
        {
            _connections[peer.Id] = new ConnectionState(peer, _nowMs() + _config.JoinTimeoutMs);
            _logger.Info($"Verbindung {peer.Id} akzeptiert");
        }
        catch (Exception ex)
        {
            _logger.Error($"Fehler in OnPeerConnected für Verbindung {peer.Id}: {ex}");
            peer.Disconnect();
        }
    }

    public void OnPeerDisconnected(NetPeer peer, DisconnectInfo disconnectInfo)
    {
        try
        {
            if (!_connections.Remove(peer.Id, out ConnectionState? conn)) return;

            if (conn.EntityId is int entityId)
            {
                // Letzte Position sofort speichern statt auf den nächsten periodischen Save (30s) zu warten.
                // CharacterId ist hier immer gesetzt: OnPeerDisconnected feuert nur für Verbindungen
                // (conn.EntityId), Monster werden nie über eine Verbindung angelegt (siehe World.SpawnMonster).
                // M6a: ebenso sofort der volle Fortschritts-Stand (siehe CharacterSaveRequest-Kommentar) -
                // sonst könnte der letzte HP/SP-Stand vor dem Logout bis zu 30s verloren gehen.
                if (conn.World != null && conn.World.TryGet(entityId, out Entity? entity) && entity!.CharacterId.HasValue)
                {
                    _positionSaveChannel.Writer.TryWrite(new PositionSaveRequest(entity.CharacterId.Value, entity.MapId, entity.IntX, entity.IntY));
                    _characterSaveChannel.Writer.TryWrite(BuildCharacterSaveRequest(entity));
                }

                RemoveFromParty(entityId, notifyRemaining: true);
                (conn.World ?? _world).Remove(entityId);
                BroadcastDespawn(entityId);

                _logger.Info($"Entity {entityId} despawnt (Disconnect, {disconnectInfo.Reason})");
            }
        }
        catch (Exception ex)
        {
            _logger.Error($"Fehler in OnPeerDisconnected für Verbindung {peer.Id}: {ex}");
        }
    }

    public void OnNetworkReceive(NetPeer peer, NetPacketReader reader, byte channelNumber, DeliveryMethod deliveryMethod)
    {
        try
        {
            if (!_connections.TryGetValue(peer.Id, out ConnectionState? conn) || conn.Closing)
                return;

            if (reader.AvailableBytes > Protocol.MaxPacketBytes)
            {
                _logger.Warn($"Paket von Verbindung {peer.Id} zu groß ({reader.AvailableBytes} Bytes) - getrennt");
                peer.Disconnect();
                return;
            }

            byte[] data = reader.GetRemainingBytes();

            IPacket packet;
            try
            {
                packet = PacketCodec.Decode(data);
            }
            catch (PacketFormatException ex)
            {
                _logger.Warn($"Kaputtes Paket von Verbindung {peer.Id}: {ex.Message}");
                peer.Disconnect();
                return;
            }

            HandlePacket(conn, packet);
        }
        catch (Exception ex)
        {
            _logger.Error($"Fehler beim Verarbeiten eines Pakets von Verbindung {peer.Id}: {ex}");
            peer.Disconnect();
        }
        finally
        {
            reader.Recycle();
        }
    }

    public void OnNetworkError(IPEndPoint endPoint, SocketError socketError)
    {
        try
        {
            _logger.Warn($"Netzwerkfehler von {endPoint}: {socketError}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Fehler beim Loggen eines Netzwerkfehlers: {ex}");
        }
    }

    public void OnNetworkReceiveUnconnected(IPEndPoint remoteEndPoint, NetPacketReader reader, UnconnectedMessageType messageType)
    {
        reader.Recycle();
    }

    public void OnNetworkLatencyUpdate(NetPeer peer, int latency)
    {
    }

    // ---- Paketverarbeitung: übersetzt Pakete in Aufrufe an die reine Spiellogik ----

    private void HandlePacket(ConnectionState conn, IPacket packet)
    {
        switch (packet)
        {
            case JoinRequestPacket join:
                HandleJoin(conn, join);
                break;
            case MoveToPacket moveTo:
                HandleMoveTo(conn, moveTo);
                break;
            case StopMovePacket:
                HandleStopMove(conn);
                break;
            case AttackRequestPacket attack:
                HandleAttackRequest(conn, attack);
                break;
            case DebugSpawnMonsterRequestPacket debugSpawn:
                HandleDebugSpawnMonster(conn, debugSpawn);
                break;
            case EquipItemRequestPacket equip:
                HandleEquipItemRequest(conn, equip);
                break;
            case UnequipItemRequestPacket unequip:
                HandleUnequipItemRequest(conn, unequip);
                break;
            case UseConsumableRequestPacket useConsumable:
                HandleUseConsumableRequest(conn, useConsumable);
                break;
            case UpgradeItemRequestPacket upgradeItem:
                HandleUpgradeItemRequest(conn, upgradeItem);
                break;
            case OpenShopRequestPacket openShop:
                HandleOpenShopRequest(conn, openShop);
                break;
            case BuyItemRequestPacket buyItem:
                HandleBuyItemRequest(conn, buyItem);
                break;
            case SellItemRequestPacket sellItem:
                HandleSellItemRequest(conn, sellItem);
                break;
            case DebugGiveYangRequestPacket debugGiveYang:
                HandleDebugGiveYangRequest(conn, debugGiveYang);
                break;
            case AllocateStatPointRequestPacket allocateStatPoint:
                HandleAllocateStatPointRequest(conn, allocateStatPoint);
                break;
            case DebugSetLevelRequestPacket debugSetLevel:
                HandleDebugSetLevelRequest(conn, debugSetLevel);
                break;
            case UseSkillRequestPacket useSkill:
                HandleUseSkillRequest(conn, useSkill);
                break;
            case AllocateSkillPointRequestPacket allocateSkillPoint:
                HandleAllocateSkillPointRequest(conn, allocateSkillPoint);
                break;
            case ChatRequestPacket chat:
                HandleChatRequest(conn, chat);
                break;
            case DebugChangeMapRequestPacket debugChangeMap:
                HandleDebugChangeMapRequest(conn, debugChangeMap);
                break;
            default:
                _logger.Warn($"Unerwartetes Paket {packet.Id} von Verbindung {conn.Peer.Id}");
                conn.Peer.Disconnect();
                break;
        }
    }

    /// <summary>Token wird sofort (synchron, reine Kryptografie) geprüft. Der Charakter-Lookup läuft
    /// dagegen asynchron im Hintergrund (siehe LookupCharacterForJoinAsync/DrainPendingJoinResults) -
    /// World und Entities bleiben so ausschließlich dem Game-Loop-Thread vorbehalten.</summary>
    private void HandleJoin(ConnectionState conn, JoinRequestPacket join)
    {
        if (conn.EntityId != null || conn.JoinPending)
        {
            _logger.Warn($"Doppeltes JoinRequest von Verbindung {conn.Peer.Id}");
            conn.Peer.Disconnect();
            return;
        }

        if (join.ProtocolVersion != Protocol.Version)
        {
            RejectJoin(conn, JoinRejectReason.VersionMismatch);
            return;
        }

        TokenValidationResult tokenResult = _tokenService.Validate(join.Token, out int accountId);
        if (tokenResult == TokenValidationResult.Expired)
        {
            RejectJoin(conn, JoinRejectReason.TokenExpired);
            return;
        }
        if (tokenResult != TokenValidationResult.Valid)
        {
            RejectJoin(conn, JoinRejectReason.InvalidToken);
            return;
        }

        conn.JoinPending = true;
        int peerId = conn.Peer.Id;
        int characterId = join.CharacterId;
        _ = Task.Run(() => LookupCharacterForJoinAsync(peerId, accountId, characterId));
    }

    /// <summary>Läuft auf einem Threadpool-Thread, NIE auf dem Game-Loop-Thread. Fängt jeden Fehler ab und
    /// meldet ihn über die Queue zurück statt zu werfen (kein unbeobachteter Task-Fehler möglich).</summary>
    private async Task LookupCharacterForJoinAsync(int peerId, int accountId, int characterId)
    {
        try
        {
            await using LumiisDbContext db = _dbContextFactory();
            // Include(InventoryItems) ist zwingend (M5a-Bug, beim Schreiben dieses Fixes gefunden): ohne
            // Include bleibt die Navigation Collection leer, CompleteJoin würde das gesamte gespeicherte
            // Inventar beim nächsten Join stillschweigend verlieren (EquippedXItemId sind einfache Spalten
            // und wären davon NICHT betroffen - nur die separate CharacterInventoryItem-Tabelle).
            Character? character = await db.Characters.AsNoTracking().Include(c => c.InventoryItems).Include(c => c.SkillLevels)
                .SingleOrDefaultAsync(c => c.Id == characterId);

            // Eigentümerschaft prüfen: die CharacterId muss zur AccountId aus dem Token gehören, sonst wie
            // "nicht gefunden" behandeln (kein Unterschied in der Antwort, der verriete, dass die Id
            // existiert, aber jemand anderem gehört).
            if (character == null || character.AccountId != accountId)
            {
                _pendingJoinResults.Enqueue(new JoinLookupResult(peerId, null, JoinRejectReason.CharacterNotFound));
                return;
            }

            _pendingJoinResults.Enqueue(new JoinLookupResult(peerId, character, null));
        }
        catch (Exception ex)
        {
            _logger.Error($"Fehler beim Laden des Charakters {characterId} für Verbindung {peerId}: {ex}");
            _pendingJoinResults.Enqueue(new JoinLookupResult(peerId, null, null));
        }
    }

    /// <summary>Sammelt abgeschlossene Charakter-Lookups ein (siehe LookupCharacterForJoinAsync) - läuft
    /// einmal pro Tick auf dem Game-Loop-Thread, damit World nie von einem anderen Thread berührt wird.</summary>
    private void DrainPendingJoinResults()
    {
        while (_pendingJoinResults.TryDequeue(out JoinLookupResult? result))
        {
            if (!_connections.TryGetValue(result.PeerId, out ConnectionState? conn) || conn.EntityId != null)
                continue; // Verbindung inzwischen getrennt oder (sollte nicht vorkommen) schon gejoint.

            conn.JoinPending = false;

            if (result.Character == null)
            {
                if (result.RejectReason is JoinRejectReason reason)
                    RejectJoin(conn, reason);
                else
                    conn.Peer.Disconnect(); // Unerwarteter Fehler (siehe Log) - generischer Fehlschlag, wie jeder andere Disconnect ohne JoinAccepted.
                continue;
            }

            CompleteJoin(conn, result.Character);
        }
    }

    private void CompleteJoin(ConnectionState conn, Character character)
    {
        // Multi-Map: die gespeicherte MapId bestimmt die Ziel-World. Unbekannte/leere Werte (z. B. sehr alte
        // Zeilen ohne Migration) fallen bewusst auf die Startkarte zurück ("village bleibt Legacy-Fallback"),
        // statt den Join abzulehnen - ein kaputter/alter MapId-Wert soll niemanden aussperren.
        World world = ResolveJoinWorld(character.MapId);

        // Globales Spielerlimit über ALLE Karten (World.TryJoin prüft nur seine eigene Karte).
        if (_worlds.Values.Sum(w => w.Entities.Count(e => e.Kind == EntityKind.Player)) >= _config.MaxPlayers)
        {
            RejectJoin(conn, JoinRejectReason.ServerFull);
            return;
        }

        // Alpha-Umfang kennt nur "Krieger" (siehe DefaultClassName-Kommentar) - classCatalog==null oder ein
        // fehlender Eintrag (sollte bei korrekt validierter classes.json nie vorkommen) fällt auf
        // Entity.DefaultXxx zurück, genau wie TryJoin es ohne die optionalen Parameter täte.
        // Defensive: eine gespeicherte Position kann aus einer anderen (nicht mehr bekannten) Karte stammen
        // oder inzwischen blockiert sein - dann auf die validierte Startposition der Karte zurückfallen,
        // statt den Spieler unbeweglich in einer Wand zu platzieren.
        int? spawnX = character.PositionX;
        int? spawnY = character.PositionY;
        if (spawnX.HasValue && spawnY.HasValue && world.IsBlockedAtWire(spawnX.Value, spawnY.Value))
        {
            _logger.Warn($"Gespeicherte Position ({spawnX},{spawnY}) von {character.Name} ist auf Karte '{world.MapId}' blockiert - nutze Kartenstart.");
            spawnX = null;
            spawnY = null;
        }

        bool joined;
        Entity? entity;
        JoinRejectReason reason;
        if (_classCatalog != null && _classCatalog.TryGet(DefaultClassName, out ClassDefinition classDef))
        {
            joined = world.TryJoin(character.Id, character.Name, spawnX, spawnY, out entity, out reason,
                meleeRangeWire: classDef.MeleeRangeWire, attackCooldownMs: classDef.AttackCooldownMs, classDef: classDef,
                level: character.Level, xp: character.Xp,
                spentSt: character.SpentSt, spentHt: character.SpentHt, spentDx: character.SpentDx, spentIq: character.SpentIq,
                unspentStatPoints: character.UnspentStatPoints, unspentSkillPoints: character.UnspentSkillPoints,
                currentHp: character.CurrentHp, currentSp: character.CurrentSp);
        }
        else
        {
            joined = world.TryJoin(character.Id, character.Name, spawnX, spawnY, out entity, out reason);
        }

        if (!joined)
        {
            RejectJoin(conn, reason);
            return;
        }

        conn.EntityId = entity!.Id;
        conn.World = world;
        entity.Yang = character.Yang;
        entity.LastSaveAtMs = _nowMs(); // Position kommt gerade erst aus der DB - kein sofortiger Save nötig.

        // M5a: Ausrüstung + Inventar aus der Character-Zeile auf die Entity übertragen, BEVOR Appearance
        // aufgelöst wird (ToAppearance liest die Entity-Felder, nicht mehr die DB-Spalten direkt) und BEVOR
        // Attack/Defense neu berechnet werden (die Startwaffe/etwaige Ausrüstung muss sofort wirken, nicht
        // erst nach dem ersten Equip).
        entity.EquippedArmorItemId = character.EquippedArmorItemId ?? 0;
        entity.EquippedHelmetItemId = character.EquippedHelmetItemId ?? 0;
        entity.EquippedWeaponItemId = character.EquippedWeaponItemId ?? 0;
        entity.EquippedShieldItemId = character.EquippedShieldItemId ?? 0;
        entity.EquippedArmorUpgradeLevel = entity.EquippedArmorItemId == 0 ? 0 : character.EquippedArmorUpgradeLevel;
        entity.EquippedHelmetUpgradeLevel = entity.EquippedHelmetItemId == 0 ? 0 : character.EquippedHelmetUpgradeLevel;
        entity.EquippedWeaponUpgradeLevel = entity.EquippedWeaponItemId == 0 ? 0 : character.EquippedWeaponUpgradeLevel;
        entity.EquippedShieldUpgradeLevel = entity.EquippedShieldItemId == 0 ? 0 : character.EquippedShieldUpgradeLevel;
        foreach (CharacterInventoryItem inventoryItem in character.InventoryItems)
        {
            if (inventoryItem.SlotIndex >= 0 && inventoryItem.SlotIndex < entity.Inventory.Length)
            {
                entity.Inventory[inventoryItem.SlotIndex] = inventoryItem.ItemId;
                entity.InventoryUpgradeLevels[inventoryItem.SlotIndex] = inventoryItem.ItemId == 0 ? 0 : inventoryItem.UpgradeLevel;
            }
        }

        // M6b: gelernte Skillstufen aus der Character-Zeile übertragen (kein Eintrag = Stufe 0/nicht gelernt,
        // siehe CharacterSkillLevel-Kommentar) - vor RecomputeDerivedStats, auch wenn Skills selbst dort
        // aktuell nichts beeinflussen (kein aktiver Buff direkt beim Join möglich, siehe Entity.ActiveBuffs).
        foreach (CharacterSkillLevel skillLevel in character.SkillLevels)
            entity.SkillLevels[skillLevel.SkillId] = skillLevel.Level;

        world.RecomputeDerivedStats(entity.Id);
        entity.Appearance = ToAppearance(character);

        MapDefinition? mapDefinition = _mapCatalog?.Get(world.MapId);
        conn.Peer.SendPacket(new JoinAcceptedPacket
        {
            EntityId = entity.Id,
            X = entity.IntX,
            Y = entity.IntY,
            Appearance = entity.Appearance,
            MapId = world.MapId,
            MapName = mapDefinition?.Name ?? world.MapId,
            MapWidth = GameConstants.MapWidth,
            MapHeight = GameConstants.MapHeight
        });
        conn.Peer.SendPacket(BuildInventorySyncPacket(entity));

        // Bugfix (Yang-Anzeige zeigte 0 bis zum ersten Kauf/Verkauf/Kill): Yang und eigenes Leben hatten sonst
        // keinen Anfangswert beim Client - beide werden nur durch Änderungs-Pakete aktualisiert
        // (YangChangedPacket/EntityHpChangedPacket-Handler, siehe GameClient.OnPacketReceived), die beim Join
        // selbst nie verschickt wurden, obwohl der Server den geladenen Yang-Stand (character.Yang) bzw. das
        // volle Leben (Entity.CurrentHp - wird nie aus der DB geladen, jede frische Entity startet immer bei
        // MaxHp, siehe World.TryJoin/Entity-Konstruktor) längst kennt. Gleiche Fehlerklasse wie der
        // EntityHpChanged-Bugfix für fremde Entities (SendHpIfDamaged), hier für den Spieler selbst und
        // deshalb bewusst unbedingt (nicht nur bei CurrentHp < MaxHp) - beide bestehenden Pakete, kein neues
        // Protokollelement.
        conn.Peer.SendPacket(new YangChangedPacket { NewTotal = entity.Yang });
        conn.Peer.SendPacket(new EntityHpChangedPacket { EntityId = entity.Id, CurrentHp = entity.CurrentHp, MaxHp = entity.MaxHp });

        // M6a: derselbe Fehlerklasse wie oben - ohne dieses Paket zeigt der Client Level/XP/Werte erst nach
        // der ersten Änderung an (siehe docs/tasks/M6a-progression.md Abschnitt 0f). Reihenfolge: nach
        // JoinAccepted/InventorySync/YangChanged (EntityHpChanged dazwischen ist die schon bestehende
        // Reihenfolge, siehe oben).
        conn.Peer.SendPacket(BuildCharacterStatsPacket(entity));

        // Der Joiner bekommt nur die laut AOI-Radius ab seiner Startposition sichtbaren Entities (nicht mehr
        // alle), laufende Bewegungen zusätzlich als EntityMove ab aktueller Position. VisibleEntityIds wird
        // hier schon auf denselben Stand gebracht wie das Gesendete, damit der nächste UpdateVisibility-Tick
        // dieselben Entities nicht nochmal als "neu sichtbar" meldet.
        HashSet<int> visibleToJoiner = world.ComputeVisibility(GameConstants.AoiRadiusWire)
            .TryGetValue(entity.Id, out HashSet<int>? v) ? v : new HashSet<int>();

        foreach (int otherId in visibleToJoiner)
        {
            if (!world.TryGet(otherId, out Entity? other)) continue; // sollte nicht vorkommen

            conn.Peer.SendPacket(new EntitySpawnPacket { EntityId = other!.Id, Name = other.Name, X = other.IntX, Y = other.IntY, Appearance = other.Appearance, Kind = other.Kind, TypeId = other.TypeId });
            SendHpIfDamaged(conn.Peer, other);
            if (other.IsMoving)
            {
                conn.Peer.SendPacket(new EntityMovePacket
                {
                    EntityId = other.Id,
                    StartX = other.IntX,
                    StartY = other.IntY,
                    TargetX = other.TargetX,
                    TargetY = other.TargetY,
                    Speed = other.Speed,
                    ServerTick = _tick
                });
            }
        }

        conn.VisibleEntityIds.UnionWith(visibleToJoiner);

        // Andere Spieler bekommen den Joiner NICHT hier per Broadcast gemeldet: die Entity ist über
        // World.TryJoin oben bereits Teil der World, der nächste UpdateVisibility-Tick erkennt sie für jede
        // Verbindung im AOI-Radius als "neu sichtbar" und verschickt EntitySpawn ganz normal über den
        // Tick-Diff (Abschnitt 3) - kein Sonderfall nötig.
        _logger.Info($"{entity.Name} (Entity {entity.Id}, Character {character.Id}) beigetreten (Karte {world.MapId})");
    }

    /// <summary>Multi-Map: gespeicherte MapId -> World. Unbekannt/leer -> Startkarte (Legacy-Fallback), mit
    /// Warnung im Log, damit ein Datenfehler sichtbar bleibt, ohne den Join zu blockieren.</summary>
    private World ResolveJoinWorld(string? mapId)
    {
        if (mapId != null && _worlds.TryGetValue(mapId, out World? world)) return world;
        if (!string.IsNullOrEmpty(mapId) && _mapCatalog != null)
            _logger.Warn($"Unbekannte MapId '{mapId}' beim Join - Fallback auf Startkarte '{_world.MapId}'.");
        return _world;
    }

    /// <summary>M5a: Armor/Helmet/Weapon/Shield werden über ItemCatalog aus den EquippedXItemId-Spalten der
    /// Character-Zeile aufgelöst (LookId nachschlagen) - beim Join, bevor die Entity überhaupt eine eigene
    /// Appearance hat (siehe CompleteJoin). Für spätere Equip/Unequip-Änderungen siehe die Entity-Overload
    /// unten (Body/Hair ändern sich dabei nicht, nur die vier Ausrüstungs-Slots).</summary>
    private Appearance ToAppearance(Character character)
    {
        return new Appearance
        {
            BodyId = character.BodyId,
            HairStyleId = character.HairStyleId,
            HairColorId = character.HairColorId,
            ArmorId = ResolveLookId(character.EquippedArmorItemId ?? 0),
            HelmetId = ResolveLookId(character.EquippedHelmetItemId ?? 0),
            WeaponId = ResolveLookId(character.EquippedWeaponItemId ?? 0),
            ShieldId = ResolveLookId(character.EquippedShieldItemId ?? 0)
        };
    }

    /// <summary>M5a: nach Equip/Unequip (siehe HandleEquipItemRequest/HandleUnequipItemRequest) - Body/Hair
    /// bleiben unverändert (aus der bisherigen, beim Join gesetzten Appearance übernommen), nur die vier
    /// Ausrüstungs-Slots werden aus den jetzt aktuellen EquippedXItemId-Feldern der Entity neu aufgelöst.</summary>
    private Appearance ToAppearance(Entity entity)
    {
        Appearance current = entity.Appearance;
        return new Appearance
        {
            BodyId = current.BodyId,
            HairStyleId = current.HairStyleId,
            HairColorId = current.HairColorId,
            ArmorId = ResolveLookId(entity.EquippedArmorItemId),
            HelmetId = ResolveLookId(entity.EquippedHelmetItemId),
            WeaponId = ResolveLookId(entity.EquippedWeaponItemId),
            ShieldId = ResolveLookId(entity.EquippedShieldItemId)
        };
    }

    private int ResolveLookId(int itemId)
    {
        if (itemId == 0 || _itemCatalog == null || !_itemCatalog.TryGet(itemId, out ItemDefinition item))
            return 0;
        return item.LookId;
    }

    private void RejectJoin(ConnectionState conn, JoinRejectReason reason)
    {
        conn.Peer.SendPacket(new JoinRejectedPacket { Reason = reason });
        conn.Closing = true;
        conn.CloseAtMs = _nowMs() + _config.RejectGraceMs;
        _logger.Info($"Join abgelehnt ({reason}) für Verbindung {conn.Peer.Id}");
    }

    private void HandleMoveTo(ConnectionState conn, MoveToPacket moveTo)
    {
        if (conn.EntityId is not int entityId)
        {
            _logger.Warn($"MoveTo vor Join von Verbindung {conn.Peer.Id}");
            conn.Peer.Disconnect();
            return;
        }

        TestFaultHook?.Invoke();

        World world = conn.World ?? _world;
        world.SetDestination(entityId, moveTo.X, moveTo.Y);
        TryGetEntity(entityId, out Entity? entity);

        SendToVisible(entityId, new EntityMovePacket
        {
            EntityId = entityId,
            StartX = entity!.IntX,
            StartY = entity.IntY,
            TargetX = entity.TargetX,
            TargetY = entity.TargetY,
            Speed = entity.Speed,
            ServerTick = _tick
        });
    }

    /// <summary>Beendet eine laufende Spielerbewegung sofort. Der Client sendet das beim Loslassen der
    /// WASD-Taste; der Server bleibt autoritativ und schickt die Endposition als EntityStop an alle
    /// Beobachter.</summary>
    private void HandleStopMove(ConnectionState conn)
    {
        if (conn.EntityId is not int entityId) return;
        World world = conn.World ?? _world;
        if (!world.TryGet(entityId, out Entity? entity) || entity == null || entity.Kind != EntityKind.Player) return;

        entity.StopMoving();
        SendToVisible(entityId, new EntityStopPacket { EntityId = entityId, X = entity.IntX, Y = entity.IntY });
    }

    /// <summary>Netzwerk-Seite eines erfolgreichen Angriffs (M4a Spieler->Monster, seit M4c auch
    /// Monster->Spieler, siehe World.RunMonsterAi) - unabhängig davon, wer angegriffen hat: EntityDamagedPacket
    /// an alle AOI-Beobachter des Ziels - der Angreifer ist dank Nahkampfreichweite (MeleeRangeWire) &lt;
    /// AOI-Radius (siehe World.TryAttack-Reichweitenprüfung) immer selbst unter diesen Beobachtern, kein
    /// Sonderfall nötig (siehe SendToVisible-Kommentar, das gilt zusätzlich auch für die eigene Entity). Bei
    /// Tod zusätzlich EntityDiedPacket, bei Monstertod danach EntityDespawnPacket (World hat das Monster in
    /// TryAttack/ApplyDamage bereits aus World entfernt - hier nur noch die Netzwerk-Seite, wie beim
    /// Disconnect-Despawn); bei Spielertod kein Despawn (siehe World.ApplyDamage/Tick - M4a-Respawn-
    /// Mechanismus unverändert). KEIN Yang-Handling hier - das braucht die Verbindung des Angreifers
    /// (Peer/CharacterId), die es bei einem Monster als Angreifer nicht gibt; bleibt deshalb Sache von
    /// HandleAttackRequest (ohnehin nur relevant, wenn ein Spieler ein Monster tötet).</summary>
    private void BroadcastAttackResult(int attackerEntityId, int targetEntityId, AttackResult result)
    {
        // M6a: Ausweichen ersetzt EntityDamaged komplett (kein Schaden, kein Tod möglich) - siehe
        // World.TryAttack/EntityDodgedPacket-Kommentar.
        if (result.Dodged)
        {
            SendToVisible(targetEntityId, new EntityDodgedPacket { AttackerEntityId = attackerEntityId, TargetEntityId = targetEntityId });
            return;
        }

        SendToVisible(targetEntityId, new EntityDamagedPacket
        {
            AttackerEntityId = attackerEntityId,
            TargetEntityId = targetEntityId,
            Damage = result.Damage,
            TargetHpAfter = result.TargetHpAfter,
            TargetMaxHp = result.TargetMaxHp
        });
        SendPartyChangedForEntity(targetEntityId);

        if (result.TargetDied)
        {
            SendToVisible(targetEntityId, new EntityDiedPacket { EntityId = targetEntityId });

            if (result.TargetKind == EntityKind.Monster)
                BroadcastDespawn(targetEntityId);
        }
    }

    /// <summary>AttackRequestPacket-Handler (M4a): ruft World.TryAttack auf und übersetzt das Ergebnis über
    /// BroadcastAttackResult in Pakete. Zusätzlich bei Yang-Gewinn YangChangedPacket NUR an den Angreifer,
    /// plus asynchrones Speichern (eigener Channel, siehe YangSaveRequest). Fehlschlag: AttackFailedPacket
    /// NUR an den Angreifer, geloggt, Verbindung bleibt bestehen.</summary>
    private void HandleAttackRequest(ConnectionState conn, AttackRequestPacket attack)
    {
        if (conn.EntityId is not int attackerId)
        {
            _logger.Warn($"AttackRequest vor Join von Verbindung {conn.Peer.Id}");
            conn.Peer.Disconnect();
            return;
        }

        World world = conn.World ?? _world;
        AttackResult result = world.TryAttack(attackerId, attack.TargetEntityId, _nowMs());

        if (!result.Success)
        {
            conn.Peer.SendPacket(new AttackFailedPacket { Reason = result.Reason });
            _logger.Info($"Angriff von Entity {attackerId} auf Entity {attack.TargetEntityId} abgelehnt ({result.Reason})");
            return;
        }

        ApplyAttackResult(conn, attackerId, attack.TargetEntityId, result);
    }

    /// <summary>M6b-Extraktion aus HandleAttackRequest (siehe docs/tasks/M6b-skills.md Abschnitt 5): die
    /// komplette Netzwerk-/Persistenz-Seite EINES erfolgreichen Treffers (Pakete, Yang, XP, Drops, Logging) -
    /// unverändert gegenüber vorher, nur ausgelagert, damit HandleUseSkillRequest denselben Weg für
    /// Wuchtschlag (ein Treffer) UND Kreisschlag (mehrere Treffer, je Ziel ein Aufruf) wiederverwenden kann,
    /// statt den Block zu duplizieren.</summary>
    private void ApplyAttackResult(ConnectionState conn, int attackerId, int targetEntityId, AttackResult result)
    {
        BroadcastAttackResult(attackerId, targetEntityId, result);

        if (result.YangAwarded > 0 && TryGetEntity(attackerId, out Entity? attackerEntity))
        {
            conn.Peer.SendPacket(new YangChangedPacket { NewTotal = attackerEntity!.Yang });
            if (attackerEntity.CharacterId is int characterId)
                _yangSaveChannel.Writer.TryWrite(new YangSaveRequest(characterId, attackerEntity.Yang));
        }

        // M6a: XP am selben Punkt wie Yang/Drops (siehe World.TryAttack/AwardXp) - CharacterStatsPacket immer
        // bei XP-Gewinn (auch ohne Levelaufstieg, XpToNext hat sich schließlich geändert), sofortiges Save
        // aber nur bei tatsächlichem Levelaufstieg (siehe docs/tasks/M6a-progression.md Abschnitt 5 - sonst
        // greift der periodische/Disconnect-Save wie bei jeder anderen HP/SP-Änderung durch Kampf).
        if (result.XpAwarded > 0 && TryGetEntity(attackerId, out Entity? xpEntity))
        {
            conn.Peer.SendPacket(BuildCharacterStatsPacket(xpEntity!));
            if (result.LeveledUp && xpEntity!.CharacterId is int levelCharacterId)
                _characterSaveChannel.Writer.TryWrite(BuildCharacterSaveRequest(xpEntity));
            SharePartyXp(attackerId, result.XpAwarded);
        }

        // M5a: Drops (siehe World.TryAttack) - bei Erfolg InventorySyncPacket an den Angreifer selbst (das
        // Inventar hat sich geändert), bei vollem Inventar zusätzlich/stattdessen InventoryActionFailedPacket
        // NUR als Info (kein harter Fehler, der Kill oben bleibt gültig). Persistenz (siehe docs/tasks/
        // M5a-items-equipment.md Abschnitt 0b, eigener InventorySaveRequest-Channel) folgt in Abschnitt 6.
        if (result.DroppedItemIds.Count > 0 && TryGetEntity(attackerId, out Entity? looter))
        {
            conn.Peer.SendPacket(BuildInventorySyncPacket(looter!));
            _inventorySaveChannel.Writer.TryWrite(BuildInventorySaveRequest(looter!));
        }

        if (result.ItemLostToFullInventory)
            conn.Peer.SendPacket(new InventoryActionFailedPacket { Reason = InventoryActionFailedReason.InventoryFull });

        _logger.Info($"Entity {attackerId} trifft Entity {targetEntityId} ({(result.Dodged ? "ausgewichen" : $"{result.Damage} Schaden")})" +
            (result.TargetDied ? ", Ziel gestorben" : "") +
            (result.YangAwarded > 0 ? $", +{result.YangAwarded} Yang" : "") +
            (result.XpAwarded > 0 ? $", +{result.XpAwarded} XP" + (result.LeveledUp ? " (Levelaufstieg)" : "") : "") +
            (result.DroppedItemIds.Count > 0 ? $", Drop(s): {string.Join(",", result.DroppedItemIds)}" : "") +
            (result.ItemLostToFullInventory ? ", Drop verloren (Inventar voll)" : ""));
    }

    /// <summary>UseSkillRequestPacket-Handler (M6b, Taste 1-4 - echte Spielsteuerung, kein Debug-Feature):
    /// ruft World.TryUseSkill auf. Erfolg bei Schadens-Skills (SingleStrike/AreaAttack): dieselbe Netzwerk-/
    /// Persistenz-Seite wie ein normaler Angriff, einmal pro getroffenem Ziel (siehe ApplyAttackResult) -
    /// AreaAttack kann mehrere Ziele gleichzeitig treffen, jedes bekommt seine eigenen Pakete. Erfolg bei
    /// Buff-Skills (AttackBuff/AttackSpeedBuff): ActiveBuffsChangedPacket an den Nutzer selbst, dazu ein
    /// CharacterStatsPacket - reicht laut Abschnitt-5-Ermessen aus, statt eines eigenen leichteren Pakets:
    /// Attack ändert sich nur bei Kampfrausch (RecomputeDerivedStats bereits in World.TryUseSkill gelaufen),
    /// der Client bekommt CharacterStatsPacket ohnehin schon nach jedem Levelaufstieg/Equip, ein weiteres
    /// Format wäre nur Protokoll-Wildwuchs für denselben Zweck. Fehlschlag: UseSkillFailedPacket nur an den
    /// Anfragenden, Verbindung bleibt bestehen.</summary>
    private void HandleUseSkillRequest(ConnectionState conn, UseSkillRequestPacket request)
    {
        if (conn.EntityId is not int userId)
        {
            _logger.Warn($"UseSkillRequest vor Join von Verbindung {conn.Peer.Id}");
            conn.Peer.Disconnect();
            return;
        }

        long nowMs = _nowMs();
        World world = conn.World ?? _world;
        SkillUseResult result = world.TryUseSkill(userId, request.SkillId, request.TargetEntityId, nowMs);

        if (!result.Success)
        {
            conn.Peer.SendPacket(new UseSkillFailedPacket { Reason = result.Reason });
            _logger.Info($"UseSkillRequest von Entity {userId} (Skill {request.SkillId}) abgelehnt ({result.Reason})");
            return;
        }

        if (result.BuffApplied is BuffType buffType)
        {
            if (!TryGetEntity(userId, out Entity? user)) return; // sollte nicht vorkommen.
            conn.Peer.SendPacket(BuildActiveBuffsChangedPacket(user!, nowMs));
            conn.Peer.SendPacket(BuildCharacterStatsPacket(user!));
            _logger.Info($"Entity {userId} setzt Skill {request.SkillId} ein (Buff {buffType})");
            return;
        }

        foreach ((int targetEntityId, AttackResult hitResult) in result.Hits)
            ApplyAttackResult(conn, userId, targetEntityId, hitResult);

        _logger.Info($"Entity {userId} setzt Skill {request.SkillId} ein ({result.Hits.Count} Treffer)");
    }

    /// <summary>AllocateSkillPointRequestPacket-Handler (M6b): ruft World.TryAllocateSkillPoint auf - wie bei
    /// AllocateStatPointRequestPacket (M6a) kein eigenes Fehlerpaket, eine ungültige Anfrage (keine Punkte,
    /// unbekannter Skill, schon Stufe 17) wird verworfen und geloggt, der Server antwortet in JEDEM Fall mit
    /// dem aktuellen CharacterStatsPacket. Sofortiges Speichern nur bei tatsächlichem Erfolg.</summary>
    private void HandleAllocateSkillPointRequest(ConnectionState conn, AllocateSkillPointRequestPacket request)
    {
        if (conn.EntityId is not int entityId)
        {
            _logger.Warn($"AllocateSkillPointRequest vor Join von Verbindung {conn.Peer.Id}");
            conn.Peer.Disconnect();
            return;
        }

        World world = conn.World ?? _world;
        bool success = world.TryAllocateSkillPoint(entityId, request.SkillId);
        if (!success)
            _logger.Info($"AllocateSkillPointRequest von Entity {entityId} (Skill {request.SkillId}) abgelehnt (keine Punkte, unbekannter Skill oder bereits Stufe {SkillCatalog.MaxSkillLevel})");

        if (!TryGetEntity(entityId, out Entity? entity)) return; // sollte nicht vorkommen.

        conn.Peer.SendPacket(BuildCharacterStatsPacket(entity!));
        if (success && entity!.CharacterId is int characterId)
            _characterSaveChannel.Writer.TryWrite(BuildCharacterSaveRequest(entity));
    }

    private void HandleChatRequest(ConnectionState conn, ChatRequestPacket request)
    {
        if (conn.EntityId is not int entityId)
        {
            _logger.Warn($"ChatRequest vor Join von Verbindung {conn.Peer.Id}");
            conn.Peer.Disconnect();
            return;
        }

        if (!TryGetEntity(entityId, out Entity? sender) || sender == null)
            return;

        long now = _nowMs();
        if (conn.LastChatAtMs != 0 && now - conn.LastChatAtMs < 400)
            return; // Rate-Limit: still verwerfen, kein Disconnect.

        string text = (request.Text ?? "").Trim();
        if (text.Length == 0) return;
        if (text.Length > 200) text = text.Substring(0, 200);
        conn.LastChatAtMs = now;

        if (HandlePartyCommand(conn, sender, text))
            return;

        ChatChannel channel = request.Channel;
        if (channel == ChatChannel.System)
            return; // Systemnachrichten kommen nur vom Server.

        if (channel == ChatChannel.Whisper)
        {
            string targetName = (request.TargetName ?? "").Trim();
            Entity? target = FindPlayerEntityByName(targetName);
            if (target == null)
            {
                SendSystemChat(conn, "Spieler nicht gefunden: " + targetName);
                return;
            }

            var whisper = new ChatMessagePacket
            {
                Channel = ChatChannel.Whisper,
                SenderName = sender.Name,
                Text = text
            };
            byte[] data = PacketCodec.Encode(whisper);
            conn.Peer.Send(data, DeliveryMethod.ReliableOrdered);
            FindConnectionByEntityId(target.Id)?.Peer.Send(data, DeliveryMethod.ReliableOrdered);
            return;
        }

        var message = new ChatMessagePacket
        {
            Channel = channel,
            SenderName = sender.Name,
            Text = text
        };
        BroadcastChat(message, conn.World);
    }

    /// <summary>Broadcast einer Chat-Nachricht. Global geht an alle verbundenen Spieler, Karte nur an
    /// Verbindungen derselben World (Multi-Map) - System/Whisper laufen nicht über diesen Pfad.</summary>
    private void BroadcastChat(ChatMessagePacket message, World? sourceWorld = null)
    {
        byte[] data = PacketCodec.Encode(message);
        foreach (ConnectionState conn in _connections.Values)
        {
            if (conn.EntityId == null) continue;
            if (message.Channel == ChatChannel.Map && sourceWorld != null && !ReferenceEquals(conn.World, sourceWorld))
                continue;
            conn.Peer.Send(data, DeliveryMethod.ReliableOrdered);
        }
    }

    private void SendSystemChat(ConnectionState conn, string text)
    {
        conn.Peer.SendPacket(new ChatMessagePacket
        {
            Channel = ChatChannel.System,
            SenderName = "System",
            Text = text
        });
    }

    private Entity? FindPlayerEntityByName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        foreach (World world in _worlds.Values)
        {
            foreach (Entity entity in world.Entities)
            {
                if (entity.Kind == EntityKind.Player && string.Equals(entity.Name, name, StringComparison.OrdinalIgnoreCase))
                    return entity;
            }
        }
        return null;
    }

    /// <summary>M7-Party-MVP über Chat-Befehle: /invite Name, /accept, /decline, /leave, /party.
    /// Kein DB-Save, keine Rollen; max. 8 Mitglieder, Leiter ist der Einladende. Gibt true zurück, wenn der
    /// Text ein bekannter Befehl war (dann nicht als Chat gebroadcastet wird).</summary>
    private bool HandlePartyCommand(ConnectionState conn, Entity sender, string text)
    {
        if (!text.StartsWith("/", StringComparison.Ordinal)) return false;

        string[] parts = text.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        string command = parts[0].ToLowerInvariant();
        string argument = parts.Length > 1 ? parts[1].Trim() : "";

        switch (command)
        {
            case "/party":
            case "/gruppe":
                SendPartySnapshot(conn, sender.Id);
                return true;

            case "/invite":
            case "/einladen":
                InviteToParty(conn, sender, argument);
                return true;

            case "/accept":
            case "/annehmen":
                AcceptPartyInvite(conn, sender);
                return true;

            case "/decline":
            case "/ablehnen":
                DeclinePartyInvite(conn, sender);
                return true;

            case "/leave":
            case "/verlassen":
                LeaveParty(conn, sender.Id);
                return true;

            default:
                SendSystemChat(conn, "Unbekannter Befehl. Verfügbar: /invite Name, /accept, /decline, /leave, /party");
                return true;
        }
    }

    private void InviteToParty(ConnectionState conn, Entity sender, string targetName)
    {
        if (string.IsNullOrWhiteSpace(targetName))
        {
            SendSystemChat(conn, "Verwendung: /invite <Name>");
            return;
        }

        Entity? target = FindPlayerEntityByName(targetName);
        if (target == null)
        {
            SendSystemChat(conn, "Spieler nicht gefunden: " + targetName);
            return;
        }
        if (target.Id == sender.Id)
        {
            SendSystemChat(conn, "Du kannst dich nicht selbst einladen.");
            return;
        }
        if (_partyByEntityId.ContainsKey(target.Id))
        {
            SendSystemChat(conn, target.Name + " ist bereits in einer Gruppe.");
            return;
        }

        if (_partyByEntityId.TryGetValue(sender.Id, out PartyState? existing))
        {
            if (existing.LeaderEntityId != sender.Id)
            {
                SendSystemChat(conn, "Nur der Gruppenleiter kann einladen.");
                return;
            }
            if (existing.Members.Count >= MaxPartySize)
            {
                SendSystemChat(conn, "Die Gruppe ist voll (max. " + MaxPartySize + ").");
                return;
            }
        }

        _pendingPartyInvites[target.Id] = sender.Id;
        SendSystemChat(conn, "Einladung an " + target.Name + " gesendet.");
        ConnectionState? targetConn = FindConnectionByEntityId(target.Id);
        if (targetConn != null)
            SendSystemChat(targetConn, sender.Name + " lädt dich in eine Gruppe ein. /accept oder /decline");
    }

    private void AcceptPartyInvite(ConnectionState conn, Entity sender)
    {
        if (_partyByEntityId.ContainsKey(sender.Id))
        {
            SendSystemChat(conn, "Du bist bereits in einer Gruppe.");
            return;
        }
        if (!_pendingPartyInvites.Remove(sender.Id, out int inviterId))
        {
            SendSystemChat(conn, "Keine offene Einladung.");
            return;
        }
        if (!TryGetEntity(inviterId, out Entity? inviter) || inviter == null || inviter.Kind != EntityKind.Player)
        {
            SendSystemChat(conn, "Der Einladende ist nicht mehr online.");
            return;
        }

        if (!_partyByEntityId.TryGetValue(inviterId, out PartyState? party))
        {
            party = new PartyState { LeaderEntityId = inviterId };
            party.Members.Add(inviterId);
            _partyByEntityId[inviterId] = party;
        }

        if (party.Members.Count >= MaxPartySize)
        {
            SendSystemChat(conn, "Die Gruppe ist voll (max. " + MaxPartySize + ").");
            return;
        }

        if (!party.Members.Contains(sender.Id))
            party.Members.Add(sender.Id);
        _partyByEntityId[sender.Id] = party;

        SendSystemChat(conn, "Du bist der Gruppe von " + inviter.Name + " beigetreten.");
        SendPartyChanged(party);
    }

    private void DeclinePartyInvite(ConnectionState conn, Entity sender)
    {
        if (!_pendingPartyInvites.Remove(sender.Id, out int inviterId))
        {
            SendSystemChat(conn, "Keine offene Einladung.");
            return;
        }

        SendSystemChat(conn, "Einladung abgelehnt.");
        ConnectionState? inviterConn = FindConnectionByEntityId(inviterId);
        if (inviterConn != null)
            SendSystemChat(inviterConn, sender.Name + " hat die Einladung abgelehnt.");
    }

    private void LeaveParty(ConnectionState conn, int entityId)
    {
        if (!_partyByEntityId.ContainsKey(entityId))
        {
            conn.Peer.SendPacket(new PartyChangedPacket { LeaderEntityId = 0 });
            SendSystemChat(conn, "Du bist in keiner Gruppe.");
            return;
        }

        RemoveFromParty(entityId, notifyRemaining: true);
        conn.Peer.SendPacket(new PartyChangedPacket { LeaderEntityId = 0 });
        SendSystemChat(conn, "Du hast die Gruppe verlassen.");
    }

    private void RemoveFromParty(int entityId, bool notifyRemaining)
    {
        _pendingPartyInvites.Remove(entityId);
        foreach (int invitee in new List<int>(_pendingPartyInvites.Keys))
        {
            if (_pendingPartyInvites[invitee] == entityId)
                _pendingPartyInvites.Remove(invitee);
        }

        if (!_partyByEntityId.Remove(entityId, out PartyState? party)) return;
        party.Members.Remove(entityId);
        if (party.Members.Count == 0) return;

        if (party.LeaderEntityId == entityId)
            party.LeaderEntityId = party.Members[0];

        if (notifyRemaining)
            SendPartyChanged(party);
    }

    private void SendPartySnapshot(ConnectionState conn, int entityId)
    {
        if (_partyByEntityId.TryGetValue(entityId, out PartyState? party))
            SendPartyChanged(party);
        else
            conn.Peer.SendPacket(new PartyChangedPacket { LeaderEntityId = 0 });
    }

    private void SendPartyChanged(PartyState party)
    {
        var packet = new PartyChangedPacket { LeaderEntityId = party.LeaderEntityId };
        foreach (int memberId in party.Members)
        {
            if (TryGetEntity(memberId, out Entity? entity) && entity != null)
                packet.Members.Add((memberId, entity.Name, entity.Level, entity.CurrentHp, entity.MaxHp));
        }

        byte[] data = PacketCodec.Encode(packet);
        foreach (int memberId in party.Members)
            FindConnectionByEntityId(memberId)?.Peer.Send(data, DeliveryMethod.ReliableOrdered);
    }

    private void SendPartyChangedForEntity(int entityId)
    {
        if (_partyByEntityId.TryGetValue(entityId, out PartyState? party))
            SendPartyChanged(party);
    }

    /// <summary>M7-Party: andere Gruppenmitglieder erhalten die Hälfte des Kill-XP (mind. 1) und ein
    /// CharacterStatsPacket; bei Levelaufstieg wird sofort gespeichert. Kein Distanz-Check (eine Karte).</summary>
    private void SharePartyXp(int attackerId, long xpAwarded)
    {
        if (xpAwarded <= 0 || !_partyByEntityId.TryGetValue(attackerId, out PartyState? party)) return;
        long share = Math.Max(1, xpAwarded / 2);

        foreach (int memberId in party.Members)
        {
            if (memberId == attackerId) continue;
            if (!TryGetEntity(memberId, out Entity? member) || member == null) continue;

            World? memberWorld = FindWorldOfEntity(memberId);
            if (memberWorld == null) continue;
            (long gained, bool leveledUp) = memberWorld.GrantXp(member, share);
            if (gained <= 0) continue;

            ConnectionState? memberConn = FindConnectionByEntityId(memberId);
            memberConn?.Peer.SendPacket(BuildCharacterStatsPacket(member));
            if (leveledUp && member.CharacterId is int characterId)
                _characterSaveChannel.Writer.TryWrite(BuildCharacterSaveRequest(member));
        }
    }

    /// <summary>Baut ein volles InventorySyncPacket aus dem aktuellen Entity-Zustand (M5a) - Resync, kein
    /// Delta (siehe docs/tasks/M5a-items-equipment.md Abschnitt 3), wiederverwendet von HandleAttackRequest
    /// (Drops) sowie den Equip/Unequip/UseConsumable-Handlern (Abschnitt 6).</summary>
    private static InventorySyncPacket BuildInventorySyncPacket(Entity entity)
    {
        var packet = new InventorySyncPacket
        {
            EquippedArmorItemId = entity.EquippedArmorItemId,
            EquippedHelmetItemId = entity.EquippedHelmetItemId,
            EquippedWeaponItemId = entity.EquippedWeaponItemId,
            EquippedShieldItemId = entity.EquippedShieldItemId,
            EquippedArmorUpgradeLevel = entity.EquippedArmorUpgradeLevel,
            EquippedHelmetUpgradeLevel = entity.EquippedHelmetUpgradeLevel,
            EquippedWeaponUpgradeLevel = entity.EquippedWeaponUpgradeLevel,
            EquippedShieldUpgradeLevel = entity.EquippedShieldUpgradeLevel
        };

        for (int slotIndex = 0; slotIndex < entity.Inventory.Length; slotIndex++)
        {
            int itemId = entity.Inventory[slotIndex];
            if (itemId != 0)
                packet.Slots.Add((slotIndex, itemId, entity.InventoryUpgradeLevels[slotIndex]));
        }

        return packet;
    }

    /// <summary>M6a: baut den vollen eigenen Fortschritts-/Kampfwert-Stand (siehe CharacterStatsPacket-
    /// Kommentar) - XpToNext kommt aus ProgressionCatalog (0 ohne Katalog oder auf MaxLevel).</summary>
    private CharacterStatsPacket BuildCharacterStatsPacket(Entity entity)
    {
        var packet = new CharacterStatsPacket
        {
            Level = entity.Level,
            Xp = entity.Xp,
            XpToNext = _progressionCatalog != null && entity.Level < _progressionCatalog.MaxLevel
                ? _progressionCatalog.XpToNextLevel(entity.Level) : 0,
            St = entity.St,
            Ht = entity.Ht,
            Dx = entity.Dx,
            Iq = entity.Iq,
            UnspentStatPoints = entity.UnspentStatPoints,
            UnspentSkillPoints = entity.UnspentSkillPoints,
            MaxHp = entity.MaxHp,
            CurrentHp = entity.CurrentHp,
            MaxSp = entity.MaxSp,
            CurrentSp = entity.CurrentSp,
            Attack = entity.Attack,
            Defense = entity.Defense,
            DodgeBp = entity.DodgeBp
        };

        // M6b: alle gelernten Skills (Stufe > 0 - Entity.SkillLevels enthält per TryAllocateSkillPoint ohnehin
        // nie eine 0-Stufe, siehe dort).
        foreach (KeyValuePair<int, int> kvp in entity.SkillLevels)
            packet.Skills.Add((kvp.Key, kvp.Value));

        return packet;
    }

    /// <summary>M6b: voller eigener aktive-Buffs-Stand (siehe ActiveBuffsChangedPacket-Kommentar) - RemainingMs
    /// statt eines absoluten Zeitstempels, deshalb braucht der Aufbau nowMs (Projekt-Konvention: keine
    /// Client-Uhrensynchronisation). Leere Liste = keine aktiven Buffs (z. B. direkt nach Ablauf des letzten).</summary>
    private static ActiveBuffsChangedPacket BuildActiveBuffsChangedPacket(Entity entity, long nowMs)
    {
        var packet = new ActiveBuffsChangedPacket();
        foreach (KeyValuePair<BuffType, (int Strength, long ExpiresAtMs)> kvp in entity.ActiveBuffs)
            packet.Buffs.Add((kvp.Key, Math.Max(0, kvp.Value.ExpiresAtMs - nowMs)));
        return packet;
    }

    /// <summary>Baut den vollen Persistenz-Snapshot aus dem aktuellen Entity-Zustand (M5a, siehe
    /// InventorySaveRequest) - CharacterId muss gesetzt sein (nur Spieler-Entities lösen Inventaraktionen
    /// aus, siehe Handle-Methoden unten).</summary>
    private static InventorySaveRequest BuildInventorySaveRequest(Entity entity)
    {
        var slots = new List<(int SlotIndex, int ItemId, int UpgradeLevel)>();
        for (int slotIndex = 0; slotIndex < entity.Inventory.Length; slotIndex++)
        {
            int itemId = entity.Inventory[slotIndex];
            if (itemId != 0)
                slots.Add((slotIndex, itemId, entity.InventoryUpgradeLevels[slotIndex]));
        }

        return new InventorySaveRequest(entity.CharacterId!.Value, slots,
            entity.EquippedArmorItemId, entity.EquippedHelmetItemId, entity.EquippedWeaponItemId, entity.EquippedShieldItemId,
            entity.EquippedArmorUpgradeLevel, entity.EquippedHelmetUpgradeLevel, entity.EquippedWeaponUpgradeLevel, entity.EquippedShieldUpgradeLevel);
    }

    /// <summary>M5b: wie BuildInventorySaveRequest, zusätzlich Yang - für den kombinierten
    /// CharacterSaveRequest-Channel (Kauf/Verkauf, Levelaufstieg/Punkteverteilung, periodisch, Disconnect -
    /// siehe CharacterSaveRequest-Kommentar). Seit M6a zusätzlich der volle Fortschritts-Stand, seit
    /// Multi-Map zusätzlich die Karten-Id der Entity.</summary>
    private CharacterSaveRequest BuildCharacterSaveRequest(Entity entity)
    {
        var slots = new List<(int SlotIndex, int ItemId, int UpgradeLevel)>();
        for (int slotIndex = 0; slotIndex < entity.Inventory.Length; slotIndex++)
        {
            int itemId = entity.Inventory[slotIndex];
            if (itemId != 0)
                slots.Add((slotIndex, itemId, entity.InventoryUpgradeLevels[slotIndex]));
        }

        var skillLevels = new List<(int SkillId, int Level)>();
        foreach (KeyValuePair<int, int> kvp in entity.SkillLevels)
            skillLevels.Add((kvp.Key, kvp.Value));

        return new CharacterSaveRequest(entity.CharacterId!.Value, entity.MapId, entity.Yang, slots,
            entity.EquippedArmorItemId, entity.EquippedHelmetItemId, entity.EquippedWeaponItemId, entity.EquippedShieldItemId,
            entity.EquippedArmorUpgradeLevel, entity.EquippedHelmetUpgradeLevel, entity.EquippedWeaponUpgradeLevel, entity.EquippedShieldUpgradeLevel,
            entity.Level, entity.Xp, entity.SpentSt, entity.SpentHt, entity.SpentDx, entity.SpentIq,
            entity.UnspentStatPoints, entity.UnspentSkillPoints, entity.CurrentHp, entity.CurrentSp, skillLevels);
    }

    /// <summary>EquipItemRequestPacket-Handler (M5a): ruft World.TryEquip auf. Erfolg: siehe
    /// ApplyInventoryAndAppearanceChange. Fehlschlag: InventoryActionFailedPacket nur an den Anfragenden,
    /// Verbindung bleibt bestehen.</summary>
    private void HandleEquipItemRequest(ConnectionState conn, EquipItemRequestPacket request)
    {
        if (conn.EntityId is not int entityId)
        {
            _logger.Warn($"EquipItemRequest vor Join von Verbindung {conn.Peer.Id}");
            conn.Peer.Disconnect();
            return;
        }

        World world = conn.World ?? _world;
        (bool success, InventoryActionFailedReason reason) = world.TryEquip(entityId, request.InventorySlotIndex);
        if (!success)
        {
            conn.Peer.SendPacket(new InventoryActionFailedPacket { Reason = reason });
            return;
        }

        ApplyInventoryAndAppearanceChange(conn, entityId);
    }

    /// <summary>UnequipItemRequestPacket-Handler (M5a): ruft World.TryUnequip auf, sonst wie
    /// HandleEquipItemRequest.</summary>
    private void HandleUnequipItemRequest(ConnectionState conn, UnequipItemRequestPacket request)
    {
        if (conn.EntityId is not int entityId)
        {
            _logger.Warn($"UnequipItemRequest vor Join von Verbindung {conn.Peer.Id}");
            conn.Peer.Disconnect();
            return;
        }

        World world = conn.World ?? _world;
        (bool success, InventoryActionFailedReason reason) = world.TryUnequip(entityId, request.EquipmentSlot);
        if (!success)
        {
            conn.Peer.SendPacket(new InventoryActionFailedPacket { Reason = reason });
            return;
        }

        ApplyInventoryAndAppearanceChange(conn, entityId);
    }

    /// <summary>UseConsumableRequestPacket-Handler (M5a): ruft World.TryUseConsumable auf. Erfolg:
    /// InventorySyncPacket an den Anfragenden plus asynchrones Speichern - bewusst KEIN Appearance-/
    /// HP-Sync-Paket (Ausrüstung ändert sich dabei nie, HP-Anzeige beim Client ist ohnehin nur reaktiv über
    /// EntityDamaged, siehe docs/tasks/M5a-items-equipment.md - "echte Health-Bars" ist wie in M4a nicht im
    /// Umfang). Fehlschlag: InventoryActionFailedPacket nur an den Anfragenden.</summary>
    private void HandleUseConsumableRequest(ConnectionState conn, UseConsumableRequestPacket request)
    {
        if (conn.EntityId is not int entityId)
        {
            _logger.Warn($"UseConsumableRequest vor Join von Verbindung {conn.Peer.Id}");
            conn.Peer.Disconnect();
            return;
        }

        World world = conn.World ?? _world;
        (bool success, InventoryActionFailedReason reason) = world.TryUseConsumable(entityId, request.InventorySlotIndex, _nowMs());
        if (!success)
        {
            conn.Peer.SendPacket(new InventoryActionFailedPacket { Reason = reason });
            return;
        }

        if (TryGetEntity(entityId, out Entity? entity))
        {
            conn.Peer.SendPacket(BuildInventorySyncPacket(entity!));
            _inventorySaveChannel.Writer.TryWrite(BuildInventorySaveRequest(entity!));
            // Bugfix nach M5a: Heiltrank ändert CurrentHp ohne Treffer - ohne dieses Paket bliebe die
            // Leben-Anzeige beim Client (und bei allen AOI-Beobachtern) auf dem alten Wert stehen.
            SendToVisible(entityId, new EntityHpChangedPacket { EntityId = entityId, CurrentHp = entity!.CurrentHp, MaxHp = entity.MaxHp });
            SendPartyChangedForEntity(entityId);
            // M6a: CharacterStatsPacket trägt CurrentHp ebenfalls (siehe docs/tasks/M6a-progression.md
            // Abschnitt 5) - kein sofortiges CharacterSaveRequest (Heiltrank ist wie jeder Treffer bewusst
            // KEIN Sofort-Speicher-Auslöser, siehe RunRegeneration-Kommentar), der periodische/Disconnect-
            // Save holt den neuen Stand ein.
            conn.Peer.SendPacket(BuildCharacterStatsPacket(entity));
        }
    }

    /// <summary>M6c: UpgradeItemRequestPacket-Handler. Die reine Logik (Vorprüfung, Verbrauch, Wurf,
    /// Zerstörung) sitzt in World.TryUpgrade. Ein abgeschlossener Versuch (Erfolg, Fehlschlag mit Rolle oder
    /// Zerstörung) liefert UpgradeItemResult + InventorySync + CharacterStats an den Anfragenden, bei einer
    /// Zerstörung zusätzlich AppearanceChanged an alle AOI-Beobachter (ausgerüstete Waffe/Rüstung verschwindet)
    /// und wird SOFORT über den kombinierten CharacterSaveRequest-Channel gespeichert - das Ergebnis ist zu
    /// wichtig für den periodischen 30s-Save. Eine abgelehnte Vorprüfung liefert nur
    /// UpgradeActionFailedPacket (kein Verbrauch, Verbindung bleibt bestehen).</summary>
    private void HandleUpgradeItemRequest(ConnectionState conn, UpgradeItemRequestPacket request)
    {
        if (conn.EntityId is not int entityId)
        {
            _logger.Warn($"UpgradeItemRequest vor Join von Verbindung {conn.Peer.Id}");
            conn.Peer.Disconnect();
            return;
        }

        World world = conn.World ?? _world;
        if (!world.TryGet(entityId, out Entity? entity))
            return;

        (int itemIdBefore, int levelBefore) = entity!.GetEquipped(request.EquipmentSlot);
        string itemName = itemIdBefore != 0 && _itemCatalog != null && _itemCatalog.TryGet(itemIdBefore, out ItemDefinition item)
            ? item.Name
            : "Ausrüstung";

        UpgradeAttemptResult result = world.TryUpgrade(entityId, request.EquipmentSlot, request.UseProtectionScroll, _nowMs());
        if (result.Rejected)
        {
            conn.Peer.SendPacket(new UpgradeActionFailedPacket { Reason = result.FailureReason });
            _logger.Info($"Aufwertung abgelehnt für Verbindung {conn.Peer.Id}: {result.FailureReason}");
            return;
        }

        conn.Peer.SendPacket(new UpgradeItemResultPacket
        {
            EquipmentSlot = request.EquipmentSlot,
            Success = result.Success,
            Destroyed = result.Destroyed,
            NewUpgradeLevel = result.NewUpgradeLevel
        });
        conn.Peer.SendPacket(BuildInventorySyncPacket(entity!));

        if (result.Destroyed)
        {
            entity!.Appearance = ToAppearance(entity);
            SendToVisible(entityId, new AppearanceChangedPacket { EntityId = entityId, Appearance = entity.Appearance });
        }

        conn.Peer.SendPacket(BuildCharacterStatsPacket(entity!));
        _characterSaveChannel.Writer.TryWrite(BuildCharacterSaveRequest(entity!));

        string message;
        if (result.Success)
            message = $"{itemName} ist jetzt +{result.NewUpgradeLevel}!";
        else if (result.Destroyed)
            message = $"Aufwertung fehlgeschlagen - {itemName} wurde zerstört!";
        else
            message = "Aufwertung fehlgeschlagen, durch die Bewahrungsrolle gerettet.";

        conn.Peer.SendPacket(new ChatMessagePacket { Channel = ChatChannel.System, SenderName = "System", Text = message });
        _logger.Info($"Aufwertung {request.EquipmentSlot} (vorher +{levelBefore}) für Verbindung {conn.Peer.Id}: " +
                     $"success={result.Success}, destroyed={result.Destroyed}, neu=+{result.NewUpgradeLevel}");
    }

    /// <summary>Gemeinsame Netzwerk-Seite von Equip/Unequip (M5a) - beide ändern das Inventar UND
    /// möglicherweise die sichtbare Ausrüstung: InventorySyncPacket an den Anfragenden selbst,
    /// AppearanceChangedPacket an alle AOI-Beobachter inklusive der Entity selbst (siehe SendToVisible -
    /// eigenes Paket statt EntityDespawn+EntitySpawn, siehe PacketId.AppearanceChanged-Kommentar), plus
    /// asynchrones Speichern (siehe InventorySaveRequest).</summary>
    private void ApplyInventoryAndAppearanceChange(ConnectionState conn, int entityId)
    {
        if (!TryGetEntity(entityId, out Entity? entity)) return; // sollte nicht vorkommen - Entity gerade erst erfolgreich geändert.

        entity!.Appearance = ToAppearance(entity);
        conn.Peer.SendPacket(BuildInventorySyncPacket(entity));
        SendToVisible(entityId, new AppearanceChangedPacket { EntityId = entityId, Appearance = entity.Appearance });
        // M6a: World.TryEquip/TryUnequip haben bereits RecomputeDerivedStats aufgerufen (Attack/Defense evtl.
        // geändert, MaxHp/MaxSp/DodgeBp bleiben gleich - siehe RecomputeDerivedStats-Kommentar) - kein
        // eigenes sofortiges CharacterSaveRequest nötig, Level/Xp/Werte/HP/SP ändern sich hier nie.
        conn.Peer.SendPacket(BuildCharacterStatsPacket(entity));
        _inventorySaveChannel.Writer.TryWrite(BuildInventorySaveRequest(entity));
    }

    /// <summary>OpenShopRequestPacket-Handler (M5b): ruft World.TryOpenShop auf. Erfolg: ShopContentPacket
    /// nur an den Anfragenden - Sortiment wird hier aus npcEntity.ShopItemIds + ItemCatalog aufgelöst (kein
    /// erneuter NpcCatalog-Zugriff nötig, siehe Entity.ShopItemIds-Kommentar). Fehlschlag:
    /// ShopActionFailedPacket nur an den Anfragenden, Verbindung bleibt bestehen, geloggt.</summary>
    private void HandleOpenShopRequest(ConnectionState conn, OpenShopRequestPacket request)
    {
        if (conn.EntityId is not int entityId)
        {
            _logger.Warn($"OpenShopRequest vor Join von Verbindung {conn.Peer.Id}");
            conn.Peer.Disconnect();
            return;
        }

        World world = conn.World ?? _world;
        (bool success, ShopActionFailedReason reason) = world.TryOpenShop(entityId, request.NpcEntityId);
        if (!success)
        {
            conn.Peer.SendPacket(new ShopActionFailedPacket { Reason = reason });
            _logger.Info($"OpenShopRequest von Entity {entityId} auf NPC {request.NpcEntityId} abgelehnt ({reason})");
            return;
        }

        if (!TryGetEntity(request.NpcEntityId, out Entity? npc)) return; // sollte nicht vorkommen - TryOpenShop hat das gerade bestätigt.

        var content = new ShopContentPacket { NpcEntityId = request.NpcEntityId };
        foreach (int itemId in npc!.ShopItemIds)
        {
            if (_itemCatalog != null && _itemCatalog.TryGet(itemId, out ItemDefinition item))
                content.Items.Add((itemId, item.BuyPrice));
        }
        conn.Peer.SendPacket(content);
    }

    /// <summary>BuyItemRequestPacket-Handler (M5b): ruft World.TryBuy auf, sonst wie
    /// HandleOpenShopRequest - Erfolg siehe SendShopActionSuccess.</summary>
    private void HandleBuyItemRequest(ConnectionState conn, BuyItemRequestPacket request)
    {
        if (conn.EntityId is not int entityId)
        {
            _logger.Warn($"BuyItemRequest vor Join von Verbindung {conn.Peer.Id}");
            conn.Peer.Disconnect();
            return;
        }

        World world = conn.World ?? _world;
        (bool success, ShopActionFailedReason reason) = world.TryBuy(entityId, request.NpcEntityId, request.ItemId);
        if (!success)
        {
            conn.Peer.SendPacket(new ShopActionFailedPacket { Reason = reason });
            _logger.Info($"BuyItemRequest von Entity {entityId} (Item {request.ItemId}, NPC {request.NpcEntityId}) abgelehnt ({reason})");
            return;
        }

        SendShopActionSuccess(conn, entityId);
        _logger.Info($"Entity {entityId} kauft Item {request.ItemId} bei NPC {request.NpcEntityId}");
    }

    /// <summary>SellItemRequestPacket-Handler (M5b): ruft World.TrySell auf, sonst wie
    /// HandleOpenShopRequest - Erfolg siehe SendShopActionSuccess.</summary>
    private void HandleSellItemRequest(ConnectionState conn, SellItemRequestPacket request)
    {
        if (conn.EntityId is not int entityId)
        {
            _logger.Warn($"SellItemRequest vor Join von Verbindung {conn.Peer.Id}");
            conn.Peer.Disconnect();
            return;
        }

        World world = conn.World ?? _world;
        (bool success, ShopActionFailedReason reason) = world.TrySell(entityId, request.NpcEntityId, request.InventorySlotIndex);
        if (!success)
        {
            conn.Peer.SendPacket(new ShopActionFailedPacket { Reason = reason });
            _logger.Info($"SellItemRequest von Entity {entityId} (Slot {request.InventorySlotIndex}, NPC {request.NpcEntityId}) abgelehnt ({reason})");
            return;
        }

        SendShopActionSuccess(conn, entityId);
        _logger.Info($"Entity {entityId} verkauft Inventarplatz {request.InventorySlotIndex} bei NPC {request.NpcEntityId}");
    }

    /// <summary>Gemeinsame Erfolgs-Netzwerk-Seite von Buy/Sell (M5b): InventorySyncPacket + YangChangedPacket
    /// an den Spieler selbst (kein neues Erfolgspaket, siehe docs/tasks/M5b-shop.md Abschnitt 2), plus
    /// asynchrones Speichern von Yang UND Inventar/Ausrüstung in einer Transaktion (kombinierter
    /// CharacterSaveRequest, siehe Abschnitt 0d/RunCharacterSaveConsumerAsync).</summary>
    private void SendShopActionSuccess(ConnectionState conn, int entityId)
    {
        if (!TryGetEntity(entityId, out Entity? entity)) return; // sollte nicht vorkommen - Entity gerade erst erfolgreich geändert.

        conn.Peer.SendPacket(BuildInventorySyncPacket(entity!));
        conn.Peer.SendPacket(new YangChangedPacket { NewTotal = entity!.Yang });
        _characterSaveChannel.Writer.TryWrite(BuildCharacterSaveRequest(entity));
    }

    /// <summary>Dev-Tool (siehe CLAUDE.md "Dev-Tools"): nur wirksam, wenn der Server mit
    /// --allow-debug-commands gestartet wurde - sonst still verworfen, analog zu HandleDebugSpawnMonster.
    /// Schreibt Amount über die gemeinsame Yang-Methode (World.CreditYang, MaxYang-Deckel) gut und meldet
    /// YangChangedPacket - nutzt bewusst den bestehenden, reinen Yang-Channel (kein Inventarbezug hier,
    /// anders als bei Kauf/Verkauf).</summary>
    private void HandleDebugGiveYangRequest(ConnectionState conn, DebugGiveYangRequestPacket request)
    {
        if (!_config.AllowDebugCommands)
        {
            _logger.Warn($"DebugGiveYangRequest von Verbindung {conn.Peer.Id} ohne --allow-debug-commands ignoriert");
            return;
        }

        if (conn.EntityId is not int entityId || !TryGetEntity(entityId, out Entity? entity))
        {
            _logger.Warn($"DebugGiveYangRequest vor Join von Verbindung {conn.Peer.Id}");
            return;
        }

        World world = conn.World ?? _world;
        world.CreditYang(entityId, request.Amount);
        conn.Peer.SendPacket(new YangChangedPacket { NewTotal = entity!.Yang });
        if (entity.CharacterId is int characterId)
            _yangSaveChannel.Writer.TryWrite(new YangSaveRequest(characterId, entity.Yang));

        _logger.Info($"Dev-Tools: Entity {entityId} bekommt {request.Amount} Yang (neuer Stand {entity.Yang})");
    }

    /// <summary>AllocateStatPointRequestPacket-Handler (M6a): ruft World.TryAllocateStatPoint auf. Anders als
    /// die meisten anderen Handler gibt es hier KEIN eigenes Fehlerpaket - eine ungültige Anfrage (keine
    /// Punkte, unbekannter Stat) wird verworfen und geloggt, der Server antwortet in JEDEM Fall mit dem
    /// aktuellen CharacterStatsPacket (bei Fehlschlag unverändert), siehe
    /// docs/tasks/M6a-progression.md Abschnitt 5. Sofortiges Speichern nur bei tatsächlichem Erfolg.</summary>
    private void HandleAllocateStatPointRequest(ConnectionState conn, AllocateStatPointRequestPacket request)
    {
        if (conn.EntityId is not int entityId)
        {
            _logger.Warn($"AllocateStatPointRequest vor Join von Verbindung {conn.Peer.Id}");
            conn.Peer.Disconnect();
            return;
        }

        World world = conn.World ?? _world;
        bool success = world.TryAllocateStatPoint(entityId, request.Stat);
        if (!success)
            _logger.Info($"AllocateStatPointRequest von Entity {entityId} (Stat {request.Stat}) abgelehnt (keine Punkte oder unbekannter Wert)");

        if (!TryGetEntity(entityId, out Entity? entity)) return; // sollte nicht vorkommen.

        conn.Peer.SendPacket(BuildCharacterStatsPacket(entity!));
        if (success && entity!.CharacterId is int characterId)
            _characterSaveChannel.Writer.TryWrite(BuildCharacterSaveRequest(entity));
    }

    /// <summary>Dev-Tool (siehe CLAUDE.md "Dev-Tools"): nur wirksam, wenn der Server mit
    /// --allow-debug-commands gestartet wurde - sonst still verworfen, analog zu HandleDebugGiveYangRequest.
    /// Ruft World.TryDebugSetLevel auf (Level/Xp/Spent* zurückgesetzt, Unspent* nachgezählt, HP/SP voll -
    /// dient auch als Stat-Reset zum Testen). Sendet bei Erfolg CharacterStatsPacket und speichert sofort
    /// (wie ein echter Levelaufstieg).</summary>
    private void HandleDebugSetLevelRequest(ConnectionState conn, DebugSetLevelRequestPacket request)
    {
        if (!_config.AllowDebugCommands)
        {
            _logger.Warn($"DebugSetLevelRequest von Verbindung {conn.Peer.Id} ohne --allow-debug-commands ignoriert");
            return;
        }

        if (conn.EntityId is not int entityId)
        {
            _logger.Warn($"DebugSetLevelRequest vor Join von Verbindung {conn.Peer.Id}");
            return;
        }

        World world = conn.World ?? _world;
        if (!world.TryDebugSetLevel(entityId, request.Level))
        {
            _logger.Warn($"DebugSetLevelRequest mit ungültigem Level {request.Level} von Verbindung {conn.Peer.Id} ignoriert");
            return;
        }

        if (!TryGetEntity(entityId, out Entity? entity)) return; // sollte nicht vorkommen.

        conn.Peer.SendPacket(BuildCharacterStatsPacket(entity!));
        if (entity!.CharacterId is int characterId)
            _characterSaveChannel.Writer.TryWrite(BuildCharacterSaveRequest(entity));

        _logger.Info($"Dev-Tools: Entity {entityId} auf Level {request.Level} gesetzt");
    }

    /// <summary>Dev-Tool (Multi-Map, Protocol.Version 17): versetzt den eigenen Charakter auf die Startposition
    /// der angegebenen Karte - nur mit --allow-debug-commands, sonst still verworfen (wie die anderen
    /// Debug-Befehle, siehe HandleDebugSpawnMonster-Kommentar). Debug-API zum Testen des Kartenwechsels ohne
    /// Portal (der Client nutzt sie aus dem Debug-Panel).</summary>
    private void HandleDebugChangeMapRequest(ConnectionState conn, DebugChangeMapRequestPacket request)
    {
        if (!_config.AllowDebugCommands)
        {
            _logger.Warn($"DebugChangeMapRequest von Verbindung {conn.Peer.Id} ohne --allow-debug-commands ignoriert");
            return;
        }

        if (conn.EntityId is not int entityId || conn.World == null
            || !conn.World.TryGet(entityId, out Entity? entity) || entity == null)
        {
            _logger.Warn($"DebugChangeMapRequest vor Join von Verbindung {conn.Peer.Id}");
            return;
        }

        if (!_worlds.TryGetValue(request.MapId ?? "", out World? targetWorld))
        {
            _logger.Warn($"DebugChangeMapRequest mit unbekannter MapId '{request.MapId}' von Verbindung {conn.Peer.Id}");
            return;
        }

        if (ReferenceEquals(targetWorld, conn.World))
        {
            entity.Teleport(targetWorld.StartX, targetWorld.StartY);
            SendToVisible(entityId, new EntityStopPacket { EntityId = entityId, X = entity.IntX, Y = entity.IntY });
            return;
        }

        TransitionEntityToMap(entity, conn.World, targetWorld, targetWorld.StartX, targetWorld.StartY, _nowMs());
        _logger.Info($"Dev-Tools: Entity {entityId} auf Karte '{targetWorld.MapId}' versetzt");
    }

    /// <summary>Dev-Tools (siehe CLAUDE.md "Dev-Tools"): nur wirksam, wenn der Server mit
    /// --allow-debug-commands gestartet wurde - sonst wird das Paket still verworfen (geloggt, keine Antwort,
    /// keine Disconnect), als gäbe es diesen Befehl nicht (siehe ServerConfig.AllowDebugCommands-Kommentar).
    /// Bewusst kein AttackFailed-artiges Ablehnungspaket: der Client sendet dieses Paket ohnehin nur aus dem
    /// #if UNITY_EDITOR || DEVELOPMENT_BUILD-Debug-Panel, ein Ablehnungspaket würde also nur einen neuen,
    /// production-irrelevanten Protokollfall für ein Dev-Only-Feature erfordern. Spawnt bei aktivierter Option
    /// ein Monster des angegebenen Mob-Typs nahe der Position des Absenders (World.FindNearbyWalkablePoint) -
    /// OHNE SpawnPointId (siehe Entity.SpawnPointId-Kommentar): stirbt es, passiert exakt das, was auch sonst
    /// beim Monstertod passiert (aktuell sofortiges Entfernen, kein Respawn-Scheduling, siehe M4a/M4b).</summary>
    private void HandleDebugSpawnMonster(ConnectionState conn, DebugSpawnMonsterRequestPacket debugSpawn)
    {
        if (!_config.AllowDebugCommands)
        {
            _logger.Warn($"DebugSpawnMonsterRequest von Verbindung {conn.Peer.Id} ohne --allow-debug-commands ignoriert");
            return;
        }

        if (conn.EntityId is not int senderId || !TryGetEntity(senderId, out Entity? sender))
        {
            _logger.Warn($"DebugSpawnMonsterRequest vor Join von Verbindung {conn.Peer.Id}");
            return;
        }

        if (_mobCatalog == null || !_mobCatalog.TryGet(debugSpawn.MobId, out MobDefinition mobDef))
        {
            _logger.Warn($"DebugSpawnMonsterRequest mit unbekannter MobId {debugSpawn.MobId} von Verbindung {conn.Peer.Id}");
            return;
        }

        World world = conn.World ?? _world;
        (int x, int y) = world.FindNearbyWalkablePoint(sender!.IntX, sender.IntY, offsetMin: 100, offsetMax: 300);
        world.SpawnMonster(debugSpawn.MobId, mobDef.Name, x, y, mobDef.SpeedWire,
            mobDef.WanderRadiusWire, mobDef.WanderIntervalMinMs, mobDef.WanderIntervalMaxMs, _nowMs(),
            mobDef.MaxHp, mobDef.Attack, mobDef.Defense, mobDef.YangMin, mobDef.YangMax, // spawnPointId bewusst weggelassen (Default null).
            drops: mobDef.Drops.Select(d => (d.ItemId, d.ChancePercent)).ToList(),
            level: mobDef.Level, baseXp: mobDef.BaseXp); // M6a: Debug-Monster geben XP wie Katalog-Monster (0c/Abschnitt 5).

        _logger.Info($"Dev-Tools: {mobDef.Name} bei ({x},{y}) gespawnt (angefordert von Entity {senderId})");
    }

    /// <summary>Bugfix nach M5a: EntitySpawnPacket trägt kein Leben - eine Entity, die schon vor dem Betreten
    /// der AOI eines Beobachters beschädigt war (z. B. ein Monster mitten im Kampf, oder ein Spieler, der sich
    /// nach einem Treffer wieder nähert), würde ohne diesen Zusatz-Versand als "voll geheilt" erscheinen, bis
    /// sie erneut getroffen wird. Wird direkt nach jedem EntitySpawnPacket für eine fremde Entity aufgerufen
    /// (CompleteJoin/UpdateVisibility) - kein Sonderfall, einfach ein no-op bei vollem Leben.</summary>
    private static void SendHpIfDamaged(NetPeer peer, Entity entity)
    {
        if (entity.CurrentHp < entity.MaxHp)
            peer.SendPacket(new EntityHpChangedPacket { EntityId = entity.Id, CurrentHp = entity.CurrentHp, MaxHp = entity.MaxHp });
    }

    /// <summary>Benachrichtigt alle Verbindungen, die diese Entity laut ihrem letzten VisibleEntityIds-Stand
    /// aktuell sehen, über ihr Verschwinden - und räumt die Buchhaltung gleich mit auf (HashSet.Remove liefert
    /// beides in einem Aufruf). Setzt voraus, dass die Entity bereits aus World entfernt wurde (siehe
    /// OnPeerDisconnected/World.ApplyDamage bei Monstertod) - kümmert sich nur um die Netzwerk-Seite.</summary>
    private void BroadcastDespawn(int entityId)
    {
        foreach (ConnectionState conn in _connections.Values)
        {
            if (conn.VisibleEntityIds.Remove(entityId))
                conn.Peer.SendPacket(new EntityDespawnPacket { EntityId = entityId });
        }
    }

    /// <summary>Schickt ein Paket über eine bestimmte Entity (Move/Stop) an alle Verbindungen, die diese
    /// Entity laut ihrem letzten VisibleEntityIds-Stand sehen (siehe UpdateVisibility) - PLUS immer an die
    /// eigene Verbindung dieser Entity selbst: ComputeVisibility schließt die eigene Entity bewusst aus der
    /// eigenen Sichtbarkeitsmenge aus (siehe dort), der Client rendert seine eigene Figur aber genauso über
    /// EntityMove/EntityStop wie jede andere (keine Client-Prediction, siehe CLAUDE.md).</summary>
    private void SendToVisible(int entityId, IPacket packet)
    {
        byte[] data = PacketCodec.Encode(packet);
        foreach (ConnectionState conn in _connections.Values)
        {
            if (conn.EntityId == null) continue;
            if (conn.EntityId == entityId || conn.VisibleEntityIds.Contains(entityId))
                conn.Peer.Send(data, DeliveryMethod.ReliableOrdered);
        }
    }

    /// <summary>M6a: findet die Verbindung, die eine bestimmte Spieler-Entity hält - für Pakete, die NUR an
    /// den Besitzer gehen (CharacterStatsPacket) und außerhalb eines eingehenden Pakets ausgelöst werden
    /// (Respawn/Regeneration in RunTick, kein conn im Aufrufkontext vorhanden, anders als bei den
    /// Request-Handlern). O(Verbindungen) - wie SendToVisible bei unserer Spielergrößenordnung
    /// vernachlässigbar, nur für seltene Ereignisse pro Tick aufgerufen. Null, wenn die Entity keine
    /// (mehr eine) Verbindung hat (sollte für die aktuellen Aufrufer nicht vorkommen).</summary>
    /// <summary>Multi-Map: sucht eine Entity in allen Karten-Welten (EntityIds sind global eindeutig, siehe
    /// Konstruktor). O(Karten) - bei unserer Kartengrößenordnung vernachlässigbar.</summary>
    private bool TryGetEntity(int entityId, out Entity? entity)
    {
        foreach (World world in _worlds.Values)
        {
            if (world.TryGet(entityId, out entity)) return true;
        }
        entity = null;
        return false;
    }

    private World? FindWorldOfEntity(int entityId)
    {
        foreach (World world in _worlds.Values)
        {
            if (world.TryGet(entityId, out _)) return world;
        }
        return null;
    }

    private ConnectionState? FindConnectionByEntityId(int entityId)
    {
        foreach (ConnectionState conn in _connections.Values)
        {
            if (conn.EntityId == entityId) return conn;
        }
        return null;
    }
}
