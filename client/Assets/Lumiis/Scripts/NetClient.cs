using System;
using System.Net;
using System.Net.Sockets;
using LiteNetLib;
using Lumiis.Shared;
using Lumiis.Shared.Net;
using UnityEngine;

namespace Lumiis.Client
{
    /// <summary>Dünner LiteNetLib-Adapter: verbindet, schickt JoinRequest, leitet empfangene Pakete als Events weiter.
    /// Kennt kein Spiel-/Weltwissen (keine ClientWorld-Referenz).</summary>
    public sealed class NetClient : MonoBehaviour, INetEventListener
    {
        public event Action Connected;
        public event Action<string> Disconnected;
        public event Action<IPacket> PacketReceived;

        private NetManager _netManager;
        private NetPeer _peer;
        private string _pendingName;

        public bool IsConnected { get; private set; }
        public int Ping { get; private set; }

        /// <summary>Baut die Verbindung auf und merkt sich den Namen fürs JoinRequest nach dem Connect.
        /// Gibt false zurück, wenn der Name schon vorab ungültig ist (kein Verbindungsversuch).</summary>
        public bool Connect(string host, int port, string name)
        {
            if (!NameRules.IsValid(name))
            {
                Debug.LogWarning("NetClient.Connect: ungültiger Name '" + name + "'");
                return false;
            }

            if (_netManager != null) _netManager.Stop();

            _netManager = new NetManager(this) { UpdateTime = 15 };
            _pendingName = name;
            IsConnected = false;
            _netManager.Start();
            _peer = _netManager.Connect(host, port, Protocol.ConnectionKey);
            return true;
        }

        public void SendMoveTo(int x, int y)
        {
            if (_peer == null || !IsConnected) return;
            Send(new MoveToPacket { X = x, Y = y });
        }

        private void Send(IPacket packet)
        {
            if (_peer == null) return;
            _peer.Send(PacketCodec.Encode(packet), DeliveryMethod.ReliableOrdered);
        }

        private void Update()
        {
            if (_netManager == null) return;
            _netManager.PollEvents();
            if (_peer != null) Ping = _peer.RoundTripTime;
        }

        private void OnDestroy()
        {
            StopClient();
        }

        private void OnApplicationQuit()
        {
            StopClient();
        }

        private void StopClient()
        {
            if (_netManager != null) _netManager.Stop();
        }

        // ---- INetEventListener ----

        public void OnConnectionRequest(ConnectionRequest request)
        {
            request.Reject(); // Der Client nimmt selbst keine eingehenden Verbindungen an.
        }

        public void OnPeerConnected(NetPeer peer)
        {
            IsConnected = true;
            Send(new JoinRequestPacket { ProtocolVersion = Protocol.Version, Name = _pendingName });
            Connected?.Invoke();
        }

        public void OnPeerDisconnected(NetPeer peer, DisconnectInfo disconnectInfo)
        {
            IsConnected = false;
            _peer = null;
            Ping = 0;
            Disconnected?.Invoke(disconnectInfo.Reason.ToString());
        }

        public void OnNetworkReceive(NetPeer peer, NetPacketReader reader, byte channelNumber, DeliveryMethod deliveryMethod)
        {
            try
            {
                byte[] data = reader.GetRemainingBytes();

                IPacket packet;
                try
                {
                    packet = PacketCodec.Decode(data);
                }
                catch (PacketFormatException ex)
                {
                    Debug.LogWarning("Kaputtes Paket vom Server: " + ex.Message);
                    peer.Disconnect();
                    return;
                }

                PacketReceived?.Invoke(packet);
            }
            finally
            {
                reader.Recycle();
            }
        }

        public void OnNetworkError(IPEndPoint endPoint, SocketError socketError)
        {
            Debug.LogWarning("Netzwerkfehler: " + socketError);
        }

        public void OnNetworkReceiveUnconnected(IPEndPoint remoteEndPoint, NetPacketReader reader, UnconnectedMessageType messageType)
        {
            reader.Recycle();
        }

        public void OnNetworkLatencyUpdate(NetPeer peer, int latency)
        {
        }
    }
}
