using System;
using System.Collections.Generic;
using LiteNetLib;
using Lumiis.Shared.Net;

namespace Lumiis.Tests.Integration
{
    /// <summary>Minimaler Test-Client: echtes LiteNetLib, wird manuell gepumpt (kein eigener Thread, keine Sleeps als Wartemechanismus).</summary>
    public sealed class TestClient : INetEventListener, IDisposable
    {
        private readonly NetManager _netManager;

        public NetPeer? Peer { get; private set; }
        public List<IPacket> Received { get; } = new List<IPacket>();
        public bool Disconnected { get; private set; }
        public DisconnectInfo? DisconnectInfo { get; private set; }

        public TestClient()
        {
            _netManager = new NetManager(this) { UpdateTime = 15 };
            _netManager.Start();
        }

        public void Connect(int port)
        {
            Peer = _netManager.Connect("127.0.0.1", port, "lumiis");
        }

        public void Poll()
        {
            _netManager.PollEvents();
        }

        public void Send(IPacket packet)
        {
            Peer?.Send(PacketCodec.Encode(packet), DeliveryMethod.ReliableOrdered);
        }

        public void SendRaw(byte[] data)
        {
            Peer?.Send(data, DeliveryMethod.ReliableOrdered);
        }

        public void Dispose()
        {
            _netManager.Stop();
        }

        // ---- INetEventListener ----

        public void OnConnectionRequest(ConnectionRequest request) => request.Reject();

        public void OnPeerConnected(NetPeer peer)
        {
        }

        public void OnPeerDisconnected(NetPeer peer, DisconnectInfo disconnectInfo)
        {
            Disconnected = true;
            DisconnectInfo = disconnectInfo;
        }

        public void OnNetworkReceive(NetPeer peer, NetPacketReader reader, byte channelNumber, DeliveryMethod deliveryMethod)
        {
            byte[] data = reader.GetRemainingBytes();
            reader.Recycle();

            try
            {
                Received.Add(PacketCodec.Decode(data));
            }
            catch (PacketFormatException)
            {
                // In Tests, die kaputte Pakete selbst verschicken, wird nichts empfangen erwartet.
            }
        }

        public void OnNetworkError(System.Net.IPEndPoint endPoint, System.Net.Sockets.SocketError socketError)
        {
        }

        public void OnNetworkReceiveUnconnected(System.Net.IPEndPoint remoteEndPoint, NetPacketReader reader, UnconnectedMessageType messageType)
        {
            reader.Recycle();
        }

        public void OnNetworkLatencyUpdate(NetPeer peer, int latency)
        {
        }
    }
}
