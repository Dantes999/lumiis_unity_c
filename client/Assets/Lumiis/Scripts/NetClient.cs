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

        /// <summary>Taste losgelassen: der Server stoppt die Bewegung sofort, statt noch bis zum letzten
        /// MoveTo-Ziel weiterzulaufen.</summary>
        public void SendStopMove()
        {
            if (_peer == null || !IsConnected) return;
            Send(new StopMovePacket());
        }

        /// <summary>M4a: Rechtsklick auf ein sichtbares Monster (siehe GameClient.HandleClickInput) - der
        /// Server prüft alles erneut serverautoritativ (Existenz, Leben, PvP, Cooldown, Reichweite).</summary>
        public void SendAttack(int targetEntityId)
        {
            if (_peer == null || !IsConnected) return;
            Send(new AttackRequestPacket { TargetEntityId = targetEntityId });
        }

        /// <summary>Dev-Tools (siehe CLAUDE.md "Dev-Tools", GameClient-Debug-Panel) - nur wirksam, wenn der
        /// Server mit --allow-debug-commands gestartet wurde, sonst still ohne Wirkung.</summary>
        public void SendDebugSpawnMonster(int mobId)
        {
            if (_peer == null || !IsConnected) return;
            Send(new DebugSpawnMonsterRequestPacket { MobId = mobId });
        }

        /// <summary>Multi-Map-Debug-API (GameClient.DebugChangeMap): nur wirksam mit
        /// --allow-debug-commands, sonst still ohne Wirkung (siehe HandleDebugChangeMapRequest).</summary>
        public void SendDebugChangeMap(string mapId)
        {
            if (_peer == null || !IsConnected) return;
            Send(new DebugChangeMapRequestPacket { MapId = mapId });
        }

        /// <summary>M5a: Klick auf einen belegten Ausrüstungs-Item-Slot im Debug-Panel (siehe
        /// GameClient.DrawInventoryPanel) - der Server prüft alles erneut (Slot belegt, ItemType passt).</summary>
        public void SendEquipItem(int inventorySlotIndex)
        {
            if (_peer == null || !IsConnected) return;
            Send(new EquipItemRequestPacket { InventorySlotIndex = inventorySlotIndex });
        }

        /// <summary>M5a: Ablegen-Knopf im Debug-Panel (siehe GameClient.DrawEquipmentPanel).</summary>
        public void SendUnequipItem(EquipmentSlot equipmentSlot)
        {
            if (_peer == null || !IsConnected) return;
            Send(new UnequipItemRequestPacket { EquipmentSlot = equipmentSlot });
        }

        /// <summary>M5a: Klick auf einen belegten Consumable-Slot im Debug-Panel.</summary>
        public void SendUseConsumable(int inventorySlotIndex)
        {
            if (_peer == null || !IsConnected) return;
            Send(new UseConsumableRequestPacket { InventorySlotIndex = inventorySlotIndex });
        }

        /// <summary>M6c: "Aufwerten"-Knopf im Ausrüstungs-Bereich (nur angelegte Ausrüstung) - der Server
        /// prüft Slot/Stufe/Material/Bewahrungsrolle erneut (siehe World.TryUpgrade).</summary>
        public void SendUpgradeItem(EquipmentSlot equipmentSlot, bool useProtectionScroll)
        {
            if (_peer == null || !IsConnected) return;
            Send(new UpgradeItemRequestPacket { EquipmentSlot = equipmentSlot, UseProtectionScroll = useProtectionScroll });
        }

        /// <summary>M5b: Rechtsklick auf einen sichtbaren NPC (siehe GameClient.HandleRightClickAttack).</summary>
        public void SendOpenShop(int npcEntityId)
        {
            if (_peer == null || !IsConnected) return;
            Send(new OpenShopRequestPacket { NpcEntityId = npcEntityId });
        }

        /// <summary>M5b: "Kaufen"-Knopf im Shop-Panel.</summary>
        public void SendBuyItem(int npcEntityId, int itemId)
        {
            if (_peer == null || !IsConnected) return;
            Send(new BuyItemRequestPacket { NpcEntityId = npcEntityId, ItemId = itemId });
        }

        /// <summary>M5b: "Verkaufen"-Knopf im Shop-Panel.</summary>
        public void SendSellItem(int npcEntityId, int inventorySlotIndex)
        {
            if (_peer == null || !IsConnected) return;
            Send(new SellItemRequestPacket { NpcEntityId = npcEntityId, InventorySlotIndex = inventorySlotIndex });
        }

        /// <summary>Dev-Tools (siehe CLAUDE.md "Dev-Tools", GameClient-Debug-Panel) - nur wirksam, wenn der
        /// Server mit --allow-debug-commands gestartet wurde, sonst still ohne Wirkung.</summary>
        public void SendDebugGiveYang(long amount)
        {
            if (_peer == null || !IsConnected) return;
            Send(new DebugGiveYangRequestPacket { Amount = amount });
        }

        /// <summary>M6a: "+"-Knopf neben einem Wert im Debug-Panel (siehe GameClient.DrawCharacterPanel) - der
        /// Server prüft erneut, ob UnspentStatPoints &gt; 0 sind.</summary>
        public void SendAllocateStatPoint(StatKind stat)
        {
            if (_peer == null || !IsConnected) return;
            Send(new AllocateStatPointRequestPacket { Stat = stat });
        }

        /// <summary>Dev-Tools (siehe CLAUDE.md "Dev-Tools", GameClient-Debug-Panel) - nur wirksam, wenn der
        /// Server mit --allow-debug-commands gestartet wurde, sonst still ohne Wirkung.</summary>
        public void SendDebugSetLevel(int level)
        {
            if (_peer == null || !IsConnected) return;
            Send(new DebugSetLevelRequestPacket { Level = level });
        }

        /// <summary>M6b: Taste 1-4 (echte Spielsteuerung, siehe GameClient.HandleSkillHotkeys) - der Server
        /// prüft alles serverautoritativ erneut (gelernt, SP, Skill-Cooldown, ggf. Ziel/Reichweite, siehe
        /// World.TryUseSkill). targetEntityId 0 = kein Ziel/Selbstziel.</summary>
        public void SendUseSkill(int skillId, int targetEntityId)
        {
            if (_peer == null || !IsConnected) return;
            Send(new UseSkillRequestPacket { SkillId = skillId, TargetEntityId = targetEntityId });
        }

        /// <summary>M6b: "+"-Knopf im Skills-Bereich des Debug-Panels (siehe GameClient.DrawSkillsPanel) - der
        /// Server prüft erneut, ob UnspentSkillPoints &gt; 0 und die aktuelle Stufe &lt; 17 sind.</summary>
        public void SendAllocateSkillPoint(int skillId)
        {
            if (_peer == null || !IsConnected) return;
            Send(new AllocateSkillPointRequestPacket { SkillId = skillId });
        }

        /// <summary>M7-Chat: sendet eine Chat-Nachricht. SenderName wird nicht mitgeschickt (der Server nimmt
        /// den Entity-Namen), Zielname nur bei Whisper relevant.</summary>
        public void SendChat(ChatChannel channel, string targetName, string text)
        {
            if (_peer == null || !IsConnected) return;
            Send(new ChatRequestPacket { Channel = channel, TargetName = targetName ?? "", Text = text ?? "" });
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
            Stop();
        }

        private void OnApplicationQuit()
        {
            Stop();
        }

        /// <summary>escape-menu: auf Zuruf aufrufbar (Charakter wechseln/Ausloggen), zusätzlich weiterhin von
        /// OnDestroy/OnApplicationQuit - der IsRunning-Guard macht mehrfaches Aufrufen (z. B. erst per
        /// Escape-Menü-Aktion, danach beim Szenen-Teardown) nachweislich sicher, unabhängig davon, ob
        /// NetManager.Stop() selbst intern idempotent ist.</summary>
        public void Stop()
        {
            if (_netManager != null && _netManager.IsRunning) _netManager.Stop();
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
            // Bisher landete der Trennungsgrund nur in der UI-Fehlermeldung (LoginFlow.OnDisconnected),
            // nie in der Console - für Fehlerdiagnosen (z. B. "kein JoinRequest beim Server angekommen")
            // ohne dieses Log nicht unterscheidbar, ob überhaupt ein Disconnect-Event feuerte.
            Debug.Log($"NetClient: Verbindung getrennt (Reason={disconnectInfo.Reason}, SocketErrorCode={disconnectInfo.SocketErrorCode})");
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
