namespace Lumiis.Shared.Net
{
    public enum JoinRejectReason : byte
    {
        VersionMismatch = 1,
        InvalidName = 2,
        NameTaken = 3,
        ServerFull = 4
    }

    /// <summary>Der Client wurde aufgenommen und an der genannten Position gespawnt.</summary>
    public sealed class JoinAcceptedPacket : IPacket
    {
        public int EntityId;
        public int X;
        public int Y;

        public PacketId Id
        {
            get { return PacketId.JoinAccepted; }
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

    /// <summary>Eine (neue oder bereits vorhandene) Entity erscheint für den Client.</summary>
    public sealed class EntitySpawnPacket : IPacket
    {
        public int EntityId;
        public string Name;
        public int X;
        public int Y;

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
        }

        public void Read(PacketReader reader)
        {
            EntityId = reader.ReadInt32();
            Name = reader.ReadString();
            X = reader.ReadInt32();
            Y = reader.ReadInt32();
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
}
