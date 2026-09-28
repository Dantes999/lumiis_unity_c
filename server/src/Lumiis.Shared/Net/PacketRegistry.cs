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

        /// <summary>M5b: Rechtsklick auf einen NPC (siehe CLAUDE.md "Shop und NPCs").</summary>
        OpenShopRequest = 8,
        BuyItemRequest = 9,
        SellItemRequest = 10,

        /// <summary>Nur wirksam mit --allow-debug-commands, sonst still verworfen (siehe
        /// DebugSpawnMonsterRequest-Kommentar).</summary>
        DebugGiveYangRequest = 11,

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

        /// <summary>M5a: Aussehen einer Entity hat sich geändert (Equip/Unequip einer Waffe/Rüstung/Helm/
        /// Schild) - an alle AOI-Beobachter inklusive der Entity selbst (siehe GameServer.SendToVisible),
        /// eigenes Paket statt EntityDespawn+EntitySpawn (würde clientseitigen Animations-/Blickrichtungs-
        /// zustand unnötig zurücksetzen).</summary>
        AppearanceChanged = 1012,

        /// <summary>Bugfix nach M5a: Leben-Änderung ohne Treffer (Heiltrank, Spieler-Respawn, sowie direkt
        /// nach EntitySpawn einer bereits verletzten Entity, die neu in die AOI eintritt - EntitySpawnPacket
        /// trägt kein Leben) - an alle AOI-Beobachter inklusive der Entity selbst (siehe
        /// GameServer.SendToVisible). EntityDamagedPacket bleibt für den Schadensfall zuständig.</summary>
        EntityHpChanged = 1013,

        /// <summary>M5b: Antwort auf OpenShopRequest, nur an den Anfragenden - Sortiment des NPCs (siehe
        /// CLAUDE.md "Shop und NPCs").</summary>
        ShopContent = 1014,

        /// <summary>M5b: ein Shop-Request (Open/Buy/Sell) wurde abgelehnt - nur an den Anfragenden.</summary>
        ShopActionFailed = 1015,
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
                case PacketId.AppearanceChanged: return new AppearanceChangedPacket();
                case PacketId.EntityHpChanged: return new EntityHpChangedPacket();
                case PacketId.OpenShopRequest: return new OpenShopRequestPacket();
                case PacketId.BuyItemRequest: return new BuyItemRequestPacket();
                case PacketId.SellItemRequest: return new SellItemRequestPacket();
                case PacketId.DebugGiveYangRequest: return new DebugGiveYangRequestPacket();
                case PacketId.ShopContent: return new ShopContentPacket();
                case PacketId.ShopActionFailed: return new ShopActionFailedPacket();
                default: return null;
            }
        }
    }
}
