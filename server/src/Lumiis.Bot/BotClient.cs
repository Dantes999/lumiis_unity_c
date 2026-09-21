using System;
using LiteNetLib;
using Lumiis.Shared;
using Lumiis.Shared.Net;

namespace Lumiis.Bot;

/// <summary>Ein simulierter Client: joint, läuft dann alle 1-3 s zu einem zufälligen Ziel. Knapp geloggt.</summary>
public sealed class BotClient : INetEventListener
{
    private readonly string _name;
    private readonly int _radiusUnits;
    private readonly NetManager _netManager;
    private readonly Random _random = new();

    private NetPeer? _peer;
    private bool _joined;
    private int _spawnX;
    private int _spawnY;
    private DateTime _nextMoveAtUtc;

    public BotClient(string name, int radiusUnits)
    {
        _name = name;
        _radiusUnits = radiusUnits;
        _netManager = new NetManager(this) { UpdateTime = 15 };
    }

    public void Connect(string host, int port)
    {
        _netManager.Start();
        _peer = _netManager.Connect(host, port, Protocol.ConnectionKey);
    }

    /// <summary>Wird aus dem gemeinsamen Poll-Loop im Hauptthread aufgerufen.</summary>
    public void Poll()
    {
        _netManager.PollEvents();

        if (_joined && DateTime.UtcNow >= _nextMoveAtUtc)
        {
            SendRandomMoveTo();
            ScheduleNextMove();
        }
    }

    private void ScheduleNextMove()
    {
        double seconds = 1.0 + _random.NextDouble() * 2.0; // 1..3 s
        _nextMoveAtUtc = DateTime.UtcNow.AddSeconds(seconds);
    }

    private void SendRandomMoveTo()
    {
        BotTargetPicker.PickTarget(_spawnX, _spawnY, _radiusUnits, _random, out int x, out int y);
        Send(new MoveToPacket { X = x, Y = y });
    }

    private void Send(IPacket packet)
    {
        _peer?.Send(PacketCodec.Encode(packet), DeliveryMethod.ReliableOrdered);
    }

    // ---- INetEventListener ----

    public void OnConnectionRequest(ConnectionRequest request)
    {
        request.Reject();
    }

    public void OnPeerConnected(NetPeer peer)
    {
        Console.WriteLine($"[{_name}] verbunden, sende JoinRequest");
        Send(new JoinRequestPacket { ProtocolVersion = Protocol.Version, Name = _name });
    }

    public void OnPeerDisconnected(NetPeer peer, DisconnectInfo disconnectInfo)
    {
        string joinHint = _joined ? "" : " (kein JoinAccepted erhalten - Join fehlgeschlagen)";
        Console.WriteLine($"[{_name}] getrennt ({disconnectInfo.Reason}){joinHint}");
    }

    public void OnNetworkReceive(NetPeer peer, NetPacketReader reader, byte channelNumber, DeliveryMethod deliveryMethod)
    {
        byte[] data = reader.GetRemainingBytes();
        reader.Recycle();

        IPacket packet;
        try
        {
            packet = PacketCodec.Decode(data);
        }
        catch (PacketFormatException ex)
        {
            Console.WriteLine($"[{_name}] kaputtes Paket vom Server: {ex.Message}");
            return;
        }

        switch (packet)
        {
            case JoinAcceptedPacket accepted:
                _joined = true;
                _spawnX = accepted.X;
                _spawnY = accepted.Y;
                Console.WriteLine($"[{_name}] JoinAccepted: Entity {accepted.EntityId} @ ({accepted.X},{accepted.Y})");
                ScheduleNextMove();
                break;
            case JoinRejectedPacket rejected:
                Console.WriteLine($"[{_name}] JoinRejected: {rejected.Reason}");
                break;
            case EntitySpawnPacket spawn:
                Console.WriteLine($"[{_name}] Spawn: {spawn.Name} (Entity {spawn.EntityId}) @ ({spawn.X},{spawn.Y})");
                break;
            case EntityDespawnPacket despawn:
                Console.WriteLine($"[{_name}] Despawn: Entity {despawn.EntityId}");
                break;
            case EntityMovePacket move:
                Console.WriteLine($"[{_name}] Move: Entity {move.EntityId} -> ({move.TargetX},{move.TargetY})");
                break;
            case EntityStopPacket stop:
                Console.WriteLine($"[{_name}] Stop: Entity {stop.EntityId} @ ({stop.X},{stop.Y})");
                break;
        }
    }

    public void OnNetworkError(System.Net.IPEndPoint endPoint, System.Net.Sockets.SocketError socketError)
    {
        Console.WriteLine($"[{_name}] Netzwerkfehler: {socketError}");
    }

    public void OnNetworkReceiveUnconnected(System.Net.IPEndPoint remoteEndPoint, NetPacketReader reader, UnconnectedMessageType messageType)
    {
        reader.Recycle();
    }

    public void OnNetworkLatencyUpdate(NetPeer peer, int latency)
    {
    }
}
