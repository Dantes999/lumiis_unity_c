namespace Lumiis.Shared.Net
{
    public enum PacketId : ushort
    {
        None = 0,

        // Client -> Server: 1..999
        JoinRequest = 1,
        MoveTo = 2,
        AttackRequest = 3,

        /// <summary>Nur wirksam, wenn der Server mit --allow-debug-commands gestartet wurde (siehe CLAUDE.md
        /// "Dev-Tools") - sonst still verworfen, als gäbe es das Paket nicht.</summary>
        DebugSpawnMonsterRequest = 4,

        EquipItemRequest = 5,
        UnequipItemRequest = 6,
        UseConsumableRequest = 7,

        // Server -> Client: ab 1000
        JoinAccepted = 1000,
        JoinRejected = 1001,
        EntitySpawn = 1002,
        EntityDespawn = 1003,
        EntityMove = 1004,
        EntityStop = 1005,
        EntityDamaged = 1006,
        EntityDied = 1007,
        AttackFailed = 1008,
        YangChanged = 1009,
        InventorySync = 1010,
        InventoryActionFailed = 1011,
    }

    /// <summary>Jede neue PacketId braucht hier eine Zeile (ein Test prüft das).</summary>
    public static class PacketFactory
    {
        public static IPacket Create(PacketId id)
        {
            switch (id)
            {
                case PacketId.JoinRequest: return new JoinRequestPacket();
                case PacketId.MoveTo: return new MoveToPacket();
                case PacketId.JoinAccepted: return new JoinAcceptedPacket();
                case PacketId.JoinRejected: return new JoinRejectedPacket();
                case PacketId.EntitySpawn: return new EntitySpawnPacket();
                case PacketId.EntityDespawn: return new EntityDespawnPacket();
                case PacketId.EntityMove: return new EntityMovePacket();
                case PacketId.EntityStop: return new EntityStopPacket();
                case PacketId.AttackRequest: return new AttackRequestPacket();
                case PacketId.DebugSpawnMonsterRequest: return new DebugSpawnMonsterRequestPacket();
                case PacketId.EntityDamaged: return new EntityDamagedPacket();
                case PacketId.EntityDied: return new EntityDiedPacket();
                case PacketId.AttackFailed: return new AttackFailedPacket();
                case PacketId.YangChanged: return new YangChangedPacket();
                case PacketId.EquipItemRequest: return new EquipItemRequestPacket();
                case PacketId.UnequipItemRequest: return new UnequipItemRequestPacket();
                case PacketId.UseConsumableRequest: return new UseConsumableRequestPacket();
                case PacketId.InventorySync: return new InventorySyncPacket();
                case PacketId.InventoryActionFailed: return new InventoryActionFailedPacket();
                default: return null;
            }
        }
    }
}
