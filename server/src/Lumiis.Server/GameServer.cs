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
using Lumiis.Data.Maps;
using Lumiis.Data.Mobs;
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
        public bool JoinPending;
        public bool Closing;
        public long CloseAtMs;

        /// <summary>Letzter bekannter Sichtbarkeits-Stand (siehe UpdateVisibility) - welche EntityIds diese
        /// Verbindung laut dem letzten Tick sieht. Start leer, auch für Entities, die schon beim Join
        /// gesendet wurden (siehe CompleteJoin) - der erste UpdateVisibility-Tick nach dem Join gleicht das an.</summary>
        public readonly HashSet<int> VisibleEntityIds = new();

        public ConnectionState(NetPeer peer, long closeAtMs)
        {
            Peer = peer;
            CloseAtMs = closeAtMs;
        }
    }

    /// <summary>Ergebnis eines asynchronen Charakter-Lookups (siehe LookupCharacterForJoinAsync), auf dem
    /// Game-Loop-Thread eingesammelt (DrainPendingJoinResults). Character != null: Join darf fortgesetzt
    /// werden (World.TryJoin entscheidet endgültig, z. B. CharacterAlreadyOnline). RejectReason != null:
    /// expliziter Ablehnungsgrund. Beides null: unerwarteter Fehler (siehe Log) - die Verbindung wird ohne
    /// JoinRejected getrennt, wie jeder andere Disconnect ohne JoinAccepted (siehe CLAUDE.md-Konvention).</summary>
    private sealed record JoinLookupResult(int PeerId, Character? Character, JoinRejectReason? RejectReason);

    /// <summary>Eine zu speichernde Position, per Channel an den Hintergrund-Task übergeben (siehe
    /// RunPositionSaveConsumerAsync).</summary>
    private readonly record struct PositionSaveRequest(int CharacterId, int X, int Y);

    /// <summary>Ein zu speichernder Yang-Gesamtbetrag (M4a), per Channel an den Hintergrund-Task übergeben
    /// (siehe RunYangSaveConsumerAsync) - eigener Channel statt Wiederverwendung des Positions-Channels
    /// (unterschiedlicher Auslöser: sofort bei jeder Änderung statt periodisch, siehe HandleAttackRequest).</summary>
    private readonly record struct YangSaveRequest(int CharacterId, long NewTotal);

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
    private readonly TokenService _tokenService;
    private readonly Func<LumiisDbContext> _dbContextFactory;
    private readonly ClassCatalog? _classCatalog;

    /// <summary>Nur für den Dev-Tools-Debug-Spawn gehalten (siehe HandleDebugSpawnMonster) - der reguläre
    /// Katalog-Spawn im Konstruktor braucht die Referenz danach nicht mehr.</summary>
    private readonly MobCatalog? _mobCatalog;
    private readonly NetManager _netManager;
    private readonly ILogger _logger;
    private readonly Func<long> _nowMs;
    private readonly Dictionary<int, ConnectionState> _connections = new();
    private readonly ConcurrentQueue<JoinLookupResult> _pendingJoinResults = new();
    private readonly Channel<PositionSaveRequest> _positionSaveChannel;
    private readonly Channel<YangSaveRequest> _yangSaveChannel;
    private readonly CancellationTokenSource _positionSaveCts = new();
    private Task? _positionSaveConsumerTask;
    private Task? _yangSaveConsumerTask;
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
    /// data/classes.json (M4a).</summary>
    public GameServer(ServerConfig config, ILogger logger, TokenService tokenService, Func<LumiisDbContext> dbContextFactory,
        Func<long>? nowMs = null, CollisionMap? collisionMap = null, MobCatalog? mobCatalog = null, SpawnCatalog? spawnCatalog = null,
        ClassCatalog? classCatalog = null)
    {
        _config = config;
        _logger = logger;
        _tokenService = tokenService;
        _dbContextFactory = dbContextFactory;
        _classCatalog = classCatalog;
        _mobCatalog = mobCatalog;
        _world = new World(config.MaxPlayers, collisionMap);
        _netManager = new NetManager(this) { UpdateTime = 15 };
        _nowMs = nowMs ?? DefaultNowMs;

        if (spawnCatalog != null && mobCatalog != null)
        {
            foreach (SpawnPoint spawn in spawnCatalog.Spawns)
            {
                mobCatalog.TryGet(spawn.MobId, out MobDefinition mobDef); // Existenz bereits von SpawnCatalog.LoadFromFile geprüft.
                _world.SpawnMonster(spawn.MobId, mobDef.Name, spawn.PositionX, spawn.PositionY, mobDef.SpeedWire,
                    mobDef.WanderRadiusWire, mobDef.WanderIntervalMinMs, mobDef.WanderIntervalMaxMs, _nowMs(),
                    mobDef.MaxHp, mobDef.Attack, mobDef.Defense, mobDef.YangMin, mobDef.YangMax,
                    spawnPointId: spawn.Id, respawnSeconds: spawn.RespawnSeconds);
            }
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
    }

    /// <summary>Tatsächlich gebundener Port (nach Start relevant, falls ServerConfig.Port 0 war).</summary>
    public int ListenPort => _netManager.LocalPort;

    public void Start()
    {
        _netManager.Start(_config.Port);
        _positionSaveConsumerTask = Task.Run(() => RunPositionSaveConsumerAsync(_positionSaveCts.Token));
        _yangSaveConsumerTask = Task.Run(() => RunYangSaveConsumerAsync(_positionSaveCts.Token));
        _logger.Info($"Server gestartet auf Port {_netManager.LocalPort} (max. {_config.MaxPlayers} Spieler)");
    }

    public void Stop()
    {
        _netManager.Stop();
        _positionSaveChannel.Writer.TryComplete();
        _yangSaveChannel.Writer.TryComplete();
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
        (List<Entity> arrived, List<Entity> startedWandering, List<Entity> respawned, List<Entity> respawnedMonsters) = _world.Tick(deltaSeconds, _nowMs());
        foreach (Entity entity in arrived)
            SendToVisible(entity.Id, new EntityStopPacket { EntityId = entity.Id, X = entity.IntX, Y = entity.IntY });

        // Spieler-Respawn (M4a): kein neues Paket nötig - EntityStopPacket bedeutet "an dieser Position zum
        // Stillstand gekommen" und der Client setzt die Position beim Empfang hart, exakt die
        // Teleport-Semantik (siehe docs/tasks/M4a-combat-core.md Abschnitt 5).
        foreach (Entity entity in respawned)
            SendToVisible(entity.Id, new EntityStopPacket { EntityId = entity.Id, X = entity.IntX, Y = entity.IntY });

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

        UpdateVisibility();
        EnqueueDuePositionSaves();
    }

    /// <summary>Nicht-blockierend (Channel.Writer.TryWrite): reine Buchhaltung + Enqueue, kein DB-Zugriff
    /// auf dem Game-Loop-Thread. Siehe RunPositionSaveConsumerAsync für den eigentlichen Schreibvorgang.</summary>
    private void EnqueueDuePositionSaves()
    {
        long now = _nowMs();
        foreach (Entity entity in SelectDueForSave(_world.Entities, now, PositionSaveIntervalMs))
        {
            entity.LastSaveAtMs = now;
            _positionSaveChannel.Writer.TryWrite(new PositionSaveRequest(entity.CharacterId!.Value, entity.IntX, entity.IntY));
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
        Dictionary<int, HashSet<int>> visibility = _world.ComputeVisibility(GameConstants.AoiRadiusWire);

        foreach (ConnectionState conn in _connections.Values)
        {
            if (conn.EntityId is not int selfId) continue;

            HashSet<int> nowVisible = visibility.TryGetValue(selfId, out HashSet<int>? v) ? v : new HashSet<int>();

            foreach (int otherId in nowVisible)
            {
                if (conn.VisibleEntityIds.Contains(otherId)) continue;
                if (!_world.TryGet(otherId, out Entity? other)) continue; // sollte nicht vorkommen (Entity gerade erst in ComputeVisibility gesehen)

                conn.Peer.SendPacket(new EntitySpawnPacket { EntityId = other!.Id, Name = other.Name, X = other.IntX, Y = other.IntY, Appearance = other.Appearance, Kind = other.Kind, MobId = other.MobId });
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
                if (_world.TryGet(entityId, out Entity? entity) && entity!.CharacterId.HasValue)
                    _positionSaveChannel.Writer.TryWrite(new PositionSaveRequest(entity.CharacterId.Value, entity.IntX, entity.IntY));

                _world.Remove(entityId);
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
            case AttackRequestPacket attack:
                HandleAttackRequest(conn, attack);
                break;
            case DebugSpawnMonsterRequestPacket debugSpawn:
                HandleDebugSpawnMonster(conn, debugSpawn);
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
            Character? character = await db.Characters.AsNoTracking().SingleOrDefaultAsync(c => c.Id == characterId);

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
        // Alpha-Umfang kennt nur "Krieger" (siehe DefaultClassName-Kommentar) - classCatalog==null oder ein
        // fehlender Eintrag (sollte bei korrekt validierter classes.json nie vorkommen) fällt auf
        // Entity.DefaultXxx zurück, genau wie TryJoin es ohne die optionalen Parameter täte.
        bool joined;
        Entity? entity;
        JoinRejectReason reason;
        if (_classCatalog != null && _classCatalog.TryGet(DefaultClassName, out ClassDefinition classDef))
        {
            joined = _world.TryJoin(character.Id, character.Name, character.PositionX, character.PositionY, out entity, out reason,
                classDef.MaxHp, classDef.Attack, classDef.Defense, classDef.MeleeRangeWire, classDef.AttackCooldownMs);
        }
        else
        {
            joined = _world.TryJoin(character.Id, character.Name, character.PositionX, character.PositionY, out entity, out reason);
        }

        if (!joined)
        {
            RejectJoin(conn, reason);
            return;
        }

        conn.EntityId = entity!.Id;
        entity.Appearance = ToAppearance(character);
        entity.Yang = character.Yang;
        entity.LastSaveAtMs = _nowMs(); // Position kommt gerade erst aus der DB - kein sofortiger Save nötig.

        conn.Peer.SendPacket(new JoinAcceptedPacket { EntityId = entity.Id, X = entity.IntX, Y = entity.IntY, Appearance = entity.Appearance });

        // Der Joiner bekommt nur die laut AOI-Radius ab seiner Startposition sichtbaren Entities (nicht mehr
        // alle), laufende Bewegungen zusätzlich als EntityMove ab aktueller Position. VisibleEntityIds wird
        // hier schon auf denselben Stand gebracht wie das Gesendete, damit der nächste UpdateVisibility-Tick
        // dieselben Entities nicht nochmal als "neu sichtbar" meldet.
        HashSet<int> visibleToJoiner = _world.ComputeVisibility(GameConstants.AoiRadiusWire)
            .TryGetValue(entity.Id, out HashSet<int>? v) ? v : new HashSet<int>();

        foreach (int otherId in visibleToJoiner)
        {
            if (!_world.TryGet(otherId, out Entity? other)) continue; // sollte nicht vorkommen

            conn.Peer.SendPacket(new EntitySpawnPacket { EntityId = other!.Id, Name = other.Name, X = other.IntX, Y = other.IntY, Appearance = other.Appearance, Kind = other.Kind, MobId = other.MobId });
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
        _logger.Info($"{entity.Name} (Entity {entity.Id}, Character {character.Id}) beigetreten");
    }

    private static Appearance ToAppearance(Character character)
    {
        return new Appearance
        {
            BodyId = character.BodyId,
            HairStyleId = character.HairStyleId,
            HairColorId = character.HairColorId,
            ArmorId = character.ArmorId,
            HelmetId = character.HelmetId,
            WeaponId = character.WeaponId,
            ShieldId = character.ShieldId
        };
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

        _world.SetDestination(entityId, moveTo.X, moveTo.Y);
        _world.TryGet(entityId, out Entity? entity);

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

    /// <summary>AttackRequestPacket-Handler (M4a): ruft World.TryAttack auf und übersetzt das Ergebnis in
    /// Pakete. Erfolg: EntityDamagedPacket an alle AOI-Beobachter des Ziels - der Angreifer ist dank
    /// Nahkampfreichweite (MeleeRangeWire) &lt; AOI-Radius (siehe World.TryAttack-Reichweitenprüfung) immer
    /// selbst unter diesen Beobachtern, kein Sonderfall nötig (siehe SendToVisible-Kommentar, das gilt
    /// zusätzlich auch für die eigene Entity). Bei Tod zusätzlich EntityDiedPacket, bei Monstertod danach
    /// EntityDespawnPacket (World hat das Monster in TryAttack/ApplyDamage bereits aus World entfernt - hier
    /// nur noch die Netzwerk-Seite, wie beim Disconnect-Despawn). Bei Yang-Gewinn YangChangedPacket NUR an
    /// den Angreifer, plus asynchrones Speichern (eigener Channel, siehe YangSaveRequest). Fehlschlag:
    /// AttackFailedPacket NUR an den Angreifer, geloggt, Verbindung bleibt bestehen.</summary>
    private void HandleAttackRequest(ConnectionState conn, AttackRequestPacket attack)
    {
        if (conn.EntityId is not int attackerId)
        {
            _logger.Warn($"AttackRequest vor Join von Verbindung {conn.Peer.Id}");
            conn.Peer.Disconnect();
            return;
        }

        AttackResult result = _world.TryAttack(attackerId, attack.TargetEntityId, _nowMs());

        if (!result.Success)
        {
            conn.Peer.SendPacket(new AttackFailedPacket { Reason = result.Reason });
            _logger.Info($"Angriff von Entity {attackerId} auf Entity {attack.TargetEntityId} abgelehnt ({result.Reason})");
            return;
        }

        SendToVisible(attack.TargetEntityId, new EntityDamagedPacket
        {
            AttackerEntityId = attackerId,
            TargetEntityId = attack.TargetEntityId,
            Damage = result.Damage,
            TargetHpAfter = result.TargetHpAfter,
            TargetMaxHp = result.TargetMaxHp
        });

        if (result.TargetDied)
        {
            SendToVisible(attack.TargetEntityId, new EntityDiedPacket { EntityId = attack.TargetEntityId });

            if (result.TargetKind == EntityKind.Monster)
                BroadcastDespawn(attack.TargetEntityId);
        }

        if (result.YangAwarded > 0 && _world.TryGet(attackerId, out Entity? attackerEntity))
        {
            conn.Peer.SendPacket(new YangChangedPacket { NewTotal = attackerEntity!.Yang });
            if (attackerEntity.CharacterId is int characterId)
                _yangSaveChannel.Writer.TryWrite(new YangSaveRequest(characterId, attackerEntity.Yang));
        }

        _logger.Info($"Entity {attackerId} trifft Entity {attack.TargetEntityId} ({result.Damage} Schaden)" +
            (result.TargetDied ? ", Ziel gestorben" : "") +
            (result.YangAwarded > 0 ? $", +{result.YangAwarded} Yang" : ""));
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

        if (conn.EntityId is not int senderId || !_world.TryGet(senderId, out Entity? sender))
        {
            _logger.Warn($"DebugSpawnMonsterRequest vor Join von Verbindung {conn.Peer.Id}");
            return;
        }

        if (_mobCatalog == null || !_mobCatalog.TryGet(debugSpawn.MobId, out MobDefinition mobDef))
        {
            _logger.Warn($"DebugSpawnMonsterRequest mit unbekannter MobId {debugSpawn.MobId} von Verbindung {conn.Peer.Id}");
            return;
        }

        (int x, int y) = _world.FindNearbyWalkablePoint(sender!.IntX, sender.IntY, offsetMin: 100, offsetMax: 300);
        _world.SpawnMonster(debugSpawn.MobId, mobDef.Name, x, y, mobDef.SpeedWire,
            mobDef.WanderRadiusWire, mobDef.WanderIntervalMinMs, mobDef.WanderIntervalMaxMs, _nowMs(),
            mobDef.MaxHp, mobDef.Attack, mobDef.Defense, mobDef.YangMin, mobDef.YangMax); // spawnPointId bewusst weggelassen (Default null).

        _logger.Info($"Dev-Tools: {mobDef.Name} bei ({x},{y}) gespawnt (angefordert von Entity {senderId})");
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
}
