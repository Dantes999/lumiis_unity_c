using System.Collections.Generic;

namespace Lumiis.Shared.Net
{
    public enum JoinRejectReason : byte
    {
        VersionMismatch = 1,
        InvalidToken = 2,
        TokenExpired = 3,
        CharacterNotFound = 4,
        CharacterAlreadyOnline = 5,
        ServerFull = 6
    }

    /// <summary>Unterscheidet Spieler- von Monster-Entities in EntitySpawn (siehe dort) - seit M3c.</summary>
    public enum EntityKind : byte
    {
        Player = 0,
        Monster = 1
    }

    /// <summary>Der Client wurde aufgenommen und an der genannten Position gespawnt. Appearance ist die eigene
    /// Figur - der Client berechnet sie nie selbst, siehe CLAUDE.md-Konvention.</summary>
    public sealed class JoinAcceptedPacket : IPacket
    {
        public int EntityId;
        public int X;
        public int Y;
        public Appearance Appearance;

        public PacketId Id
        {
            get { return PacketId.JoinAccepted; }
        }

        public void Write(PacketWriter writer)
        {
            writer.WriteInt32(EntityId);
            writer.WriteInt32(X);
            writer.WriteInt32(Y);
            Appearance.Write(writer);
        }

        public void Read(PacketReader reader)
        {
            EntityId = reader.ReadInt32();
            X = reader.ReadInt32();
            Y = reader.ReadInt32();
            Appearance.Read(reader);
        }
    }

    /// <summary>Der Join wurde abgelehnt; die Verbindung wird danach getrennt.</summary>
    public sealed class JoinRejectedPacket : IPacket
    {
        public JoinRejectReason Reason;

        public PacketId Id
        {
            get { return PacketId.JoinRejected; }
        }

        public void Write(PacketWriter writer)
        {
            writer.WriteByte((byte)Reason);
        }

        public void Read(PacketReader reader)
        {
            Reason = (JoinRejectReason)reader.ReadByte();
        }
    }

    /// <summary>Eine (neue oder bereits vorhandene) Entity erscheint für den Client - eine andere Figur oder
    /// (seit M3c) ein Monster, öffentliche Daten inklusive Appearance. Bei Kind=Monster ist Appearance mit
    /// neutralen/Default-Werten befüllt (keine Bedeutung, das Feld bleibt aber für beide Kinds im Paket, damit
    /// der Client nicht zwei verschiedene Paketformen unterscheiden muss) und MobId zeigt auf data/mobs.json;
    /// bei Kind=Player ist MobId ungenutzt/0.</summary>
    public sealed class EntitySpawnPacket : IPacket
    {
        public int EntityId;
        public string Name;
        public int X;
        public int Y;
        public Appearance Appearance;
        public EntityKind Kind;
        public int MobId;

        public PacketId Id
        {
            get { return PacketId.EntitySpawn; }
        }

        public void Write(PacketWriter writer)
        {
            writer.WriteInt32(EntityId);
            writer.WriteString(Name);
            writer.WriteInt32(X);
            writer.WriteInt32(Y);
            Appearance.Write(writer);
            writer.WriteByte((byte)Kind);
            writer.WriteInt32(MobId);
        }

        public void Read(PacketReader reader)
        {
            EntityId = reader.ReadInt32();
            Name = reader.ReadString();
            X = reader.ReadInt32();
            Y = reader.ReadInt32();
            Appearance.Read(reader);
            Kind = (EntityKind)reader.ReadByte();
            MobId = reader.ReadInt32();
        }
    }

    /// <summary>Eine Entity verschwindet (Disconnect o. ä.).</summary>
    public sealed class EntityDespawnPacket : IPacket
    {
        public int EntityId;

        public PacketId Id
        {
            get { return PacketId.EntityDespawn; }
        }

        public void Write(PacketWriter writer)
        {
            writer.WriteInt32(EntityId);
        }

        public void Read(PacketReader reader)
        {
            EntityId = reader.ReadInt32();
        }
    }

    /// <summary>Eine Entity bewegt sich von Start- zu Zielposition; der Client interpoliert selbst (keine Positions-Broadcasts pro Tick).</summary>
    public sealed class EntityMovePacket : IPacket
    {
        public int EntityId;
        public int StartX;
        public int StartY;
        public int TargetX;
        public int TargetY;
        public int Speed;
        public uint ServerTick;

        public PacketId Id
        {
            get { return PacketId.EntityMove; }
        }

        public void Write(PacketWriter writer)
        {
            writer.WriteInt32(EntityId);
            writer.WriteInt32(StartX);
            writer.WriteInt32(StartY);
            writer.WriteInt32(TargetX);
            writer.WriteInt32(TargetY);
            writer.WriteInt32(Speed);
            writer.WriteUInt32(ServerTick);
        }

        public void Read(PacketReader reader)
        {
            EntityId = reader.ReadInt32();
            StartX = reader.ReadInt32();
            StartY = reader.ReadInt32();
            TargetX = reader.ReadInt32();
            TargetY = reader.ReadInt32();
            Speed = reader.ReadInt32();
            ServerTick = reader.ReadUInt32();
        }
    }

    /// <summary>Eine Entity ist exakt an der genannten Position zum Stillstand gekommen.</summary>
    public sealed class EntityStopPacket : IPacket
    {
        public int EntityId;
        public int X;
        public int Y;

        public PacketId Id
        {
            get { return PacketId.EntityStop; }
        }

        public void Write(PacketWriter writer)
        {
            writer.WriteInt32(EntityId);
            writer.WriteInt32(X);
            writer.WriteInt32(Y);
        }

        public void Read(PacketReader reader)
        {
            EntityId = reader.ReadInt32();
            X = reader.ReadInt32();
            Y = reader.ReadInt32();
        }
    }

    /// <summary>Grund, warum ein AttackRequestPacket abgelehnt wurde - siehe AttackFailedPacket/World.TryAttack.</summary>
    public enum AttackFailedReason : byte
    {
        TargetNotFound = 1,
        TargetAlreadyDead = 2,
        OutOfRange = 3,
        OnCooldown = 4,
        TargetIsPlayer = 5
    }

    /// <summary>Eine Entity wurde getroffen (M4a) - an alle AOI-Beobachter des Ziels (inklusive Angreifer,
    /// siehe GameServer.SendToVisible).</summary>
    public sealed class EntityDamagedPacket : IPacket
    {
        public int AttackerEntityId;
        public int TargetEntityId;
        public int Damage;
        public int TargetHpAfter;
        public int TargetMaxHp;

        public PacketId Id
        {
            get { return PacketId.EntityDamaged; }
        }

        public void Write(PacketWriter writer)
        {
            writer.WriteInt32(AttackerEntityId);
            writer.WriteInt32(TargetEntityId);
            writer.WriteInt32(Damage);
            writer.WriteInt32(TargetHpAfter);
            writer.WriteInt32(TargetMaxHp);
        }

        public void Read(PacketReader reader)
        {
            AttackerEntityId = reader.ReadInt32();
            TargetEntityId = reader.ReadInt32();
            Damage = reader.ReadInt32();
            TargetHpAfter = reader.ReadInt32();
            TargetMaxHp = reader.ReadInt32();
        }
    }

    /// <summary>Eine Entity ist gestorben (M4a) - an alle AOI-Beobachter. Bei Monstern folgt danach sofort
    /// ein EntityDespawnPacket (kein Respawn in M4a), bei Spielern nicht (siehe World-Klassenkommentar zum
    /// Spielertod - die Entity bleibt in World, nur CurrentHp bleibt 0 bis zum Respawn).</summary>
    public sealed class EntityDiedPacket : IPacket
    {
        public int EntityId;

        public PacketId Id
        {
            get { return PacketId.EntityDied; }
        }

        public void Write(PacketWriter writer)
        {
            writer.WriteInt32(EntityId);
        }

        public void Read(PacketReader reader)
        {
            EntityId = reader.ReadInt32();
        }
    }

    /// <summary>Ein AttackRequestPacket wurde abgelehnt - nur an den Angreifer.</summary>
    public sealed class AttackFailedPacket : IPacket
    {
        public AttackFailedReason Reason;

        public PacketId Id
        {
            get { return PacketId.AttackFailed; }
        }

        public void Write(PacketWriter writer)
        {
            writer.WriteByte((byte)Reason);
        }

        public void Read(PacketReader reader)
        {
            Reason = (AttackFailedReason)reader.ReadByte();
        }
    }

    /// <summary>Der aktuelle Yang-Gesamtbetrag hat sich geändert (M4a) - nur an den Empfänger.</summary>
    public sealed class YangChangedPacket : IPacket
    {
        public long NewTotal;

        public PacketId Id
        {
            get { return PacketId.YangChanged; }
        }

        public void Write(PacketWriter writer)
        {
            writer.WriteInt64(NewTotal);
        }

        public void Read(PacketReader reader)
        {
            NewTotal = reader.ReadInt64();
        }
    }

    /// <summary>Voller Inventar-/Ausrüstungs-Zustand (M5a) - nur an den Empfänger, bei Join und nach jeder
    /// Änderung neu verschickt (voller Resync statt Delta, das Inventar ist mit maximal 20 Plätzen klein
    /// genug dafür). Slots enthält nur belegte Plätze (SlotIndex, ItemId) - ein fehlender SlotIndex bedeutet
    /// leer, genau wie 0 bei den vier Equipped-Feldern.</summary>
    public sealed class InventorySyncPacket : IPacket
    {
        public List<(int SlotIndex, int ItemId)> Slots = new List<(int SlotIndex, int ItemId)>();
        public int EquippedArmorItemId;
        public int EquippedHelmetItemId;
        public int EquippedWeaponItemId;
        public int EquippedShieldItemId;

        public PacketId Id
        {
            get { return PacketId.InventorySync; }
        }

        public void Write(PacketWriter writer)
        {
            writer.WriteByte((byte)Slots.Count);
            foreach ((int slotIndex, int itemId) in Slots)
            {
                writer.WriteByte((byte)slotIndex);
                writer.WriteInt32(itemId);
            }
            writer.WriteInt32(EquippedArmorItemId);
            writer.WriteInt32(EquippedHelmetItemId);
            writer.WriteInt32(EquippedWeaponItemId);
            writer.WriteInt32(EquippedShieldItemId);
        }

        public void Read(PacketReader reader)
        {
            int count = reader.ReadByte();
            Slots = new List<(int SlotIndex, int ItemId)>(count);
            for (int i = 0; i < count; i++)
            {
                int slotIndex = reader.ReadByte();
                int itemId = reader.ReadInt32();
                Slots.Add((slotIndex, itemId));
            }
            EquippedArmorItemId = reader.ReadInt32();
            EquippedHelmetItemId = reader.ReadInt32();
            EquippedWeaponItemId = reader.ReadInt32();
            EquippedShieldItemId = reader.ReadInt32();
        }
    }

    /// <summary>Grund, warum ein Inventar-/Ausrüstungs-Request abgelehnt wurde (M5a) - siehe
    /// InventoryActionFailedPacket/World.TryEquip/TryUnequip/TryUseConsumable. InventoryFull wird zusätzlich
    /// (als reine Info, kein harter Fehler) verschickt, wenn ein Monster-Drop mangels freiem Platz verloren
    /// geht - siehe World.TryAttack.</summary>
    public enum InventoryActionFailedReason : byte
    {
        InvalidSlot = 1,
        EmptySlot = 2,
        WrongItemType = 3,
        NotConsumable = 4,
        InventoryFull = 5,
        TargetAtFullHp = 6
    }

    /// <summary>Ein Inventar-/Ausrüstungs-Request wurde abgelehnt - nur an den Anfragenden.</summary>
    public sealed class InventoryActionFailedPacket : IPacket
    {
        public InventoryActionFailedReason Reason;

        public PacketId Id
        {
            get { return PacketId.InventoryActionFailed; }
        }

        public void Write(PacketWriter writer)
        {
            writer.WriteByte((byte)Reason);
        }

        public void Read(PacketReader reader)
        {
            Reason = (InventoryActionFailedReason)reader.ReadByte();
        }
    }

    /// <summary>Das Aussehen einer Entity hat sich geändert (M5a, Equip/Unequip) - an alle AOI-Beobachter
    /// inklusive der Entity selbst, siehe PacketId.AppearanceChanged-Kommentar.</summary>
    public sealed class AppearanceChangedPacket : IPacket
    {
        public int EntityId;
        public Appearance Appearance;

        public PacketId Id
        {
            get { return PacketId.AppearanceChanged; }
        }

        public void Write(PacketWriter writer)
        {
            writer.WriteInt32(EntityId);
            Appearance.Write(writer);
        }

        public void Read(PacketReader reader)
        {
            EntityId = reader.ReadInt32();
            Appearance.Read(reader);
        }
    }
}
