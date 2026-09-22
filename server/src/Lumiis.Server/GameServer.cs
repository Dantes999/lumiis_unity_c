using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using LiteNetLib;
using Lumiis.Data;
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

    private const int MaxConsecutiveFailedTicks = 50;

    // Monotone Standard-Uhr, geteilt über alle Instanzen (läuft seit Prozessstart, nie negativ).
    private static readonly Stopwatch DefaultClock = Stopwatch.StartNew();
    private static long DefaultNowMs() => DefaultClock.ElapsedMilliseconds;

    private readonly ServerConfig _config;
    private readonly World _world;
    private readonly TokenService _tokenService;
    private readonly Func<LumiisDbContext> _dbContextFactory;
    private readonly NetManager _netManager;
    private readonly ILogger _logger;
    private readonly Func<long> _nowMs;
    private readonly Dictionary<int, ConnectionState> _connections = new();
    private readonly ConcurrentQueue<JoinLookupResult> _pendingJoinResults = new();
    private uint _tick;
    private int _consecutiveFailedTicks;

    /// <summary>Nur für Tests: wenn gesetzt, wird es beim Verarbeiten von MoveTo aufgerufen (zum Simulieren eines Fehlers in einem Handler).</summary>
    internal Action? TestFaultHook { get; set; }

    /// <summary>nowMs: injizierbare Uhr für Fristen (Join-Timeout, Reject-Grace). Default: monotone Wandzeit seit Prozessstart. Ticks bleiben rein für die Simulation.
    /// dbContextFactory: erzeugt pro Hintergrund-Operation einen frischen LumiisDbContext (DbContext ist nicht thread-safe/langlebig) - World und Entities bleiben
    /// dabei ausschließlich dem Game-Loop-Thread vorbehalten, siehe World-Klassenkommentar.</summary>
    public GameServer(ServerConfig config, ILogger logger, TokenService tokenService, Func<LumiisDbContext> dbContextFactory, Func<long>? nowMs = null)
    {
        _config = config;
        _logger = logger;
        _tokenService = tokenService;
        _dbContextFactory = dbContextFactory;
        _world = new World(config.MaxPlayers);
        _netManager = new NetManager(this) { UpdateTime = 15 };
        _nowMs = nowMs ?? DefaultNowMs;
    }

    /// <summary>Tatsächlich gebundener Port (nach Start relevant, falls ServerConfig.Port 0 war).</summary>
    public int ListenPort => _netManager.LocalPort;

    public void Start()
    {
        _netManager.Start(_config.Port);
        _logger.Info($"Server gestartet auf Port {_netManager.LocalPort} (max. {_config.MaxPlayers} Spieler)");
    }

    public void Stop()
    {
        _netManager.Stop();
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
        List<Entity> arrived = _world.Tick(deltaSeconds);
        foreach (Entity entity in arrived)
            BroadcastToJoined(new EntityStopPacket { EntityId = entity.Id, X = entity.IntX, Y = entity.IntY });
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
                // TODO(M2b-3): letzte Position sofort speichern statt auf den nächsten periodischen Save
                // zu warten (Positions-Speicherung ist eigener Abschnitt der Aufgabe).
                _world.Remove(entityId);
                BroadcastToJoined(new EntityDespawnPacket { EntityId = entityId });
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
        if (!_world.TryJoin(character.Id, character.Name, character.PositionX, character.PositionY, out Entity? entity, out JoinRejectReason reason))
        {
            RejectJoin(conn, reason);
            return;
        }

        conn.EntityId = entity!.Id;
        entity.Appearance = ToAppearance(character);

        conn.Peer.SendPacket(new JoinAcceptedPacket { EntityId = entity.Id, X = entity.IntX, Y = entity.IntY, Appearance = entity.Appearance });

        // Der Joiner bekommt alle vorhandenen Entities, laufende Bewegungen zusätzlich als EntityMove ab aktueller Position.
        foreach (Entity other in _world.Entities)
        {
            if (other.Id == entity.Id) continue;

            conn.Peer.SendPacket(new EntitySpawnPacket { EntityId = other.Id, Name = other.Name, X = other.IntX, Y = other.IntY, Appearance = other.Appearance });
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

        // TODO(M3): Sichtbarkeit/AOI - aktuell sehen alle Verbindungen alle Entities.
        BroadcastToJoined(new EntitySpawnPacket { EntityId = entity.Id, Name = entity.Name, X = entity.IntX, Y = entity.IntY, Appearance = entity.Appearance }, exceptPeerId: conn.Peer.Id);

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

        BroadcastToJoined(new EntityMovePacket
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

    private void BroadcastToJoined(IPacket packet, int? exceptPeerId = null)
    {
        byte[] data = PacketCodec.Encode(packet);
        foreach (ConnectionState conn in _connections.Values)
        {
            if (conn.EntityId == null) continue;
            if (exceptPeerId.HasValue && conn.Peer.Id == exceptPeerId.Value) continue;
            conn.Peer.Send(data, DeliveryMethod.ReliableOrdered);
        }
    }
}
