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
        private string _pendingToken;
        private int _pendingCharacterId;

        public bool IsConnected { get; private set; }
        public int Ping { get; private set; }

        /// <summary>Baut die Verbindung auf und merkt sich Token+CharacterId fürs JoinRequest nach dem
        /// Connect (M2b-Protokoll: Server verlangt Token statt Name). Gibt false zurück, wenn das Token
        /// schon vorab leer ist (kein Verbindungsversuch) - die eigentliche Gültigkeitsprüfung macht ohnehin
        /// immer der Server.</summary>
        public bool Connect(string host, int port, string token, int characterId)
        {
            if (string.IsNullOrEmpty(token))
            {
                Debug.LogWarning("NetClient.Connect: leeres Token");
                return false;
            }

            if (_netManager != null) _netManager.Stop();

            _netManager = new NetManager(this) { UpdateTime = 15 };
            _pendingToken = token;
            _pendingCharacterId = characterId;
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

        /// <summary>M4a: Rechtsklick auf ein sichtbares Monster (siehe GameClient.HandleClickInput) - der
        /// Server prüft alles erneut serverautoritativ (Existenz, Leben, PvP, Cooldown, Reichweite).</summary>
        public void SendAttack(int targetEntityId)
        {
            if (_peer == null || !IsConnected) return;
            Send(new AttackRequestPacket { TargetEntityId = targetEntityId });
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
            Send(new JoinRequestPacket { ProtocolVersion = Protocol.Version, Token = _pendingToken, CharacterId = _pendingCharacterId });
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
