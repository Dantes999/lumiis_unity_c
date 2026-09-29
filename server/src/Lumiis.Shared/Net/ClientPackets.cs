namespace Lumiis.Shared.Net
{
    /// <summary>Erste Nachricht des Clients nach dem Verbinden. Ab Protocol.Version 3: Token statt Name -
    /// der Server validiert das Token lokal (Lumiis.Data.TokenService, kein HTTP-Aufruf zu Auth) und lädt
    /// den Charakter per CharacterId + der im Token enthaltenen AccountId (Eigentümerschaft geprüft).</summary>
    public sealed class JoinRequestPacket : IPacket
    {
        public int ProtocolVersion;
        public string Token;
        public int CharacterId;

        public PacketId Id
        {
            get { return PacketId.JoinRequest; }
        }

        public void Write(PacketWriter writer)
        {
            writer.WriteInt32(ProtocolVersion);
            writer.WriteString(Token);
            writer.WriteInt32(CharacterId);
        }

        public void Read(PacketReader reader)
        {
            ProtocolVersion = reader.ReadInt32();
            Token = reader.ReadString();
            CharacterId = reader.ReadInt32();
        }
    }

    /// <summary>Der Client will zu einem Zielpunkt laufen (Weltkoordinaten in 1/100 Einheiten).</summary>
    public sealed class MoveToPacket : IPacket
    {
        public int X;
        public int Y;

        public PacketId Id
        {
            get { return PacketId.MoveTo; }
        }

        public void Write(PacketWriter writer)
        {
            writer.WriteInt32(X);
            writer.WriteInt32(Y);
        }

        public void Read(PacketReader reader)
        {
            X = reader.ReadInt32();
            Y = reader.ReadInt32();
        }
    }

    /// <summary>Der Client greift eine sichtbare Entity an (M4a). Der Server prüft alles serverautoritativ
    /// erneut (Existenz, Leben, PvP, Cooldown, Reichweite) - siehe World.TryAttack.</summary>
    public sealed class AttackRequestPacket : IPacket
    {
        public int TargetEntityId;

        public PacketId Id
        {
            get { return PacketId.AttackRequest; }
        }

        public void Write(PacketWriter writer)
        {
            writer.WriteInt32(TargetEntityId);
        }

        public void Read(PacketReader reader)
        {
            TargetEntityId = reader.ReadInt32();
        }
    }

    /// <summary>Dev-Tool (siehe CLAUDE.md "Dev-Tools"): spawnt ein Monster nahe der Absenderposition. Nur
    /// wirksam, wenn der Server mit --allow-debug-commands gestartet wurde - sonst still verworfen.</summary>
    public sealed class DebugSpawnMonsterRequestPacket : IPacket
    {
        public int MobId;

        public PacketId Id
        {
            get { return PacketId.DebugSpawnMonsterRequest; }
        }

        public void Write(PacketWriter writer)
        {
            writer.WriteInt32(MobId);
        }

        public void Read(PacketReader reader)
        {
            MobId = reader.ReadInt32();
        }
    }

    /// <summary>Welcher der vier Ausrüstungs-Slots (M5a) - siehe UnequipItemRequestPacket.</summary>
    public enum EquipmentSlot : byte
    {
        Armor = 1,
        Helmet = 2,
        Weapon = 3,
        Shield = 4
    }

    /// <summary>Der Client will das Item aus dem angegebenen Inventarplatz anlegen (M5a). Der Server prüft
    /// alles serverautoritativ erneut (Slot belegt, passender ItemType) - siehe World.TryEquip.</summary>
    public sealed class EquipItemRequestPacket : IPacket
    {
        public int InventorySlotIndex;

        public PacketId Id
        {
            get { return PacketId.EquipItemRequest; }
        }

        public void Write(PacketWriter writer)
        {
            writer.WriteInt32(InventorySlotIndex);
        }

        public void Read(PacketReader reader)
        {
            InventorySlotIndex = reader.ReadInt32();
        }
    }

    /// <summary>Der Client will das Item aus dem angegebenen Ausrüstungs-Slot ablegen (M5a) - landet auf der
    /// ersten freien Inventarstelle, siehe World.TryUnequip.</summary>
    public sealed class UnequipItemRequestPacket : IPacket
    {
        public EquipmentSlot EquipmentSlot;

        public PacketId Id
        {
            get { return PacketId.UnequipItemRequest; }
        }

        public void Write(PacketWriter writer)
        {
            writer.WriteByte((byte)EquipmentSlot);
        }

        public void Read(PacketReader reader)
        {
            EquipmentSlot = (EquipmentSlot)reader.ReadByte();
        }
    }

    /// <summary>Der Client will das Item aus dem angegebenen Inventarplatz benutzen (M5a, z. B. Heiltrank) -
    /// siehe World.TryUseConsumable.</summary>
    public sealed class UseConsumableRequestPacket : IPacket
    {
        public int InventorySlotIndex;

        public PacketId Id
        {
            get { return PacketId.UseConsumableRequest; }
        }

        public void Write(PacketWriter writer)
        {
            writer.WriteInt32(InventorySlotIndex);
        }

        public void Read(PacketReader reader)
        {
            InventorySlotIndex = reader.ReadInt32();
        }
    }

    /// <summary>Rechtsklick auf einen sichtbaren NPC (M5b) - der Server prüft Existenz/Kind/Reichweite
    /// erneut, siehe World.TryOpenShop.</summary>
    public sealed class OpenShopRequestPacket : IPacket
    {
        public int NpcEntityId;

        public PacketId Id
        {
            get { return PacketId.OpenShopRequest; }
        }

        public void Write(PacketWriter writer)
        {
            writer.WriteInt32(NpcEntityId);
        }

        public void Read(PacketReader reader)
        {
            NpcEntityId = reader.ReadInt32();
        }
    }

    /// <summary>Kauf-Knopf im Shop-Panel (M5b) - siehe World.TryBuy.</summary>
    public sealed class BuyItemRequestPacket : IPacket
    {
        public int NpcEntityId;
        public int ItemId;

        public PacketId Id
        {
            get { return PacketId.BuyItemRequest; }
        }

        public void Write(PacketWriter writer)
        {
            writer.WriteInt32(NpcEntityId);
            writer.WriteInt32(ItemId);
        }

        public void Read(PacketReader reader)
        {
            NpcEntityId = reader.ReadInt32();
            ItemId = reader.ReadInt32();
        }
    }

    /// <summary>Verkaufen-Knopf im Shop-Panel (M5b) - siehe World.TrySell.</summary>
    public sealed class SellItemRequestPacket : IPacket
    {
        public int NpcEntityId;
        public int InventorySlotIndex;

        public PacketId Id
        {
            get { return PacketId.SellItemRequest; }
        }

        public void Write(PacketWriter writer)
        {
            writer.WriteInt32(NpcEntityId);
            writer.WriteInt32(InventorySlotIndex);
        }

        public void Read(PacketReader reader)
        {
            NpcEntityId = reader.ReadInt32();
            InventorySlotIndex = reader.ReadInt32();
        }
    }

    /// <summary>Dev-Tool (siehe CLAUDE.md "Dev-Tools"): schreibt Amount über die gemeinsame Yang-Methode
    /// (World.CreditYang, MaxYang-Deckel) gut. Nur wirksam mit --allow-debug-commands, sonst still
    /// verworfen, analog zu DebugSpawnMonsterRequestPacket.</summary>
    public sealed class DebugGiveYangRequestPacket : IPacket
    {
        public long Amount;

        public PacketId Id
        {
            get { return PacketId.DebugGiveYangRequest; }
        }

        public void Write(PacketWriter writer)
        {
            writer.WriteInt64(Amount);
        }

        public void Read(PacketReader reader)
        {
            Amount = reader.ReadInt64();
        }
    }

    /// <summary>Einer der vier verteilbaren Werte (M6a, siehe CLAUDE.md Abschnitt "Fortschritt: Level, XP,
    /// Werte") - siehe AllocateStatPointRequestPacket.</summary>
    public enum StatKind : byte
    {
        St = 1,
        Ht = 2,
        Dx = 3,
        Iq = 4
    }

    /// <summary>Der Client will einen Statuspunkt auf den genannten Wert verteilen (M6a) - der Server prüft
    /// erneut, ob UnspentStatPoints > 0 ist (siehe World.TryAllocateStatPoint). Eine ungültige Anfrage
    /// (keine Punkte, unbekannter Wert) führt zu keiner Trennung, nur zu einem erneuten
    /// CharacterStatsPacket mit dem unveränderten Stand (siehe GameServer.HandleAllocateStatPointRequest).</summary>
    public sealed class AllocateStatPointRequestPacket : IPacket
    {
        public StatKind Stat;

        public PacketId Id
        {
            get { return PacketId.AllocateStatPointRequest; }
        }

        public void Write(PacketWriter writer)
        {
            writer.WriteByte((byte)Stat);
        }

        public void Read(PacketReader reader)
        {
            Stat = (StatKind)reader.ReadByte();
        }
    }

    /// <summary>Dev-Tool (siehe CLAUDE.md "Dev-Tools"): setzt Level = Level, Xp/Spent* zurückgesetzt,
    /// Unspent* entsprechend nachgezählt (dient auch als Stat-Reset zum Testen, siehe
    /// World.ApplyDebugLevel). Nur wirksam mit --allow-debug-commands, sonst still verworfen, analog zu
    /// DebugSpawnMonsterRequestPacket/DebugGiveYangRequestPacket.</summary>
    public sealed class DebugSetLevelRequestPacket : IPacket
    {
        public int Level;

        public PacketId Id
        {
            get { return PacketId.DebugSetLevelRequest; }
        }

        public void Write(PacketWriter writer)
        {
            writer.WriteInt32(Level);
        }

        public void Read(PacketReader reader)
        {
            Level = reader.ReadInt32();
        }
    }
}
