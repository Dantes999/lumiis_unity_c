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

        /// <summary>M6a: ein Statuspunkt wird verteilt (siehe CLAUDE.md "Fortschritt: Level, XP, Werte").</summary>
        AllocateStatPointRequest = 12,

        /// <summary>Nur wirksam mit --allow-debug-commands, sonst still verworfen (siehe
        /// DebugSpawnMonsterRequest-Kommentar).</summary>
        DebugSetLevelRequest = 13,

        /// <summary>M6b: Taste 1-4 (echte Spielsteuerung, siehe CLAUDE.md "Skills").</summary>
        UseSkillRequest = 14,

        /// <summary>M6b: ein Fertigkeitspunkt wird auf einen Skill verteilt.</summary>
        AllocateSkillPointRequest = 15,

        /// <summary>M7-Chat: Chat-Nachricht senden (Karte/Global/Flüstern).</summary>
        ChatRequest = 16,

        /// <summary>Multi-Map (Protocol.Version 18): nur wirksam mit --allow-debug-commands, sonst still
        /// verworfen (siehe DebugSpawnMonsterRequest-Kommentar). Versetzt den eigenen Charakter auf die
        /// Startposition der angegebenen Karte - Debug-API zum Testen des Kartenwechsels ohne Portal.</summary>
        DebugChangeMapRequest = 17,

        /// <summary>M6c: angelegte Ausrüstung aufwerten (Schleifstein, optional Bewahrungsrolle).</summary>
        UpgradeItemRequest = 18,

        /// <summary>Taste losgelassen: laufende Bewegung sofort beenden (Protocol.Version 18).</summary>
        StopMove = 19,

        /// <summary>Boden-Drop aufheben (Protocol.Version 18).</summary>
        PickupItemRequest = 20,

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

        /// <summary>M6a: voller eigener Fortschritts-/Kampfwert-Stand (siehe CLAUDE.md "Fortschritt: Level,
        /// XP, Werte") - nur an den Besitzer.</summary>
        CharacterStats = 1016,

        /// <summary>M6a: ein Angriff wurde ausgewichen - an alle AOI-Beobachter des Ziels.</summary>
        EntityDodged = 1017,

        /// <summary>M6b: ein UseSkillRequest wurde abgelehnt - nur an den Anfragenden.</summary>
        UseSkillFailed = 1018,

        /// <summary>M6b: der eigene aktive-Buffs-Stand hat sich geändert (Skill gewirkt oder Buff
        /// abgelaufen) - nur an den Besitzer.</summary>
        ActiveBuffsChanged = 1019,

        /// <summary>M7-Chat: Chat-Nachricht (Karte/Global/Flüstern/System).</summary>
        ChatMessage = 1020,

        /// <summary>M7-Party: voller Gruppenstand (Resync).</summary>
        PartyChanged = 1021,

        /// <summary>Multi-Map (Protocol.Version 18): der eigene Charakter hat die Karte gewechselt (Portal
        /// oder Dev-Tool). Der Client leert seine Entities/Umgebung und baut Boden/Portale der neuen Karte
        /// neu auf; die Zielposition ist bereits enthalten (kein separates EntityStop nötig).</summary>
        MapChanged = 1022,

        /// <summary>M6c: Ergebnis eines abgeschlossenen Aufwertungsversuchs (auch Fehlschlag/Zerstörung) -
        /// nur an den Anfragenden, siehe UpgradeItemResultPacket.</summary>
        UpgradeItemResult = 1023,

        /// <summary>M6c: Aufwertung gar nicht erst versucht (leerer Slot, Maximalstufe, fehlendes Material/
        /// fehlende Schutzrolle) - nur an den Anfragenden, Verbindung bleibt bestehen.</summary>
        UpgradeActionFailed = 1024,
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
                case PacketId.StopMove: return new StopMovePacket();
                case PacketId.PickupItemRequest: return new PickupItemRequestPacket();
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
                case PacketId.AllocateStatPointRequest: return new AllocateStatPointRequestPacket();
                case PacketId.DebugSetLevelRequest: return new DebugSetLevelRequestPacket();
                case PacketId.CharacterStats: return new CharacterStatsPacket();
                case PacketId.EntityDodged: return new EntityDodgedPacket();
                case PacketId.UseSkillRequest: return new UseSkillRequestPacket();
                case PacketId.AllocateSkillPointRequest: return new AllocateSkillPointRequestPacket();
                case PacketId.UseSkillFailed: return new UseSkillFailedPacket();
                case PacketId.ActiveBuffsChanged: return new ActiveBuffsChangedPacket();
                case PacketId.ChatRequest: return new ChatRequestPacket();
                case PacketId.ChatMessage: return new ChatMessagePacket();
                case PacketId.PartyChanged: return new PartyChangedPacket();
                case PacketId.DebugChangeMapRequest: return new DebugChangeMapRequestPacket();
                case PacketId.MapChanged: return new MapChangedPacket();
                case PacketId.UpgradeItemRequest: return new UpgradeItemRequestPacket();
                case PacketId.UpgradeItemResult: return new UpgradeItemResultPacket();
                case PacketId.UpgradeActionFailed: return new UpgradeActionFailedPacket();
                default: return null;
            }
        }
    }
}
