namespace Lumiis.Shared.Net
{
    /// <summary>Erste Nachricht des Clients nach dem Verbinden.</summary>
    public sealed class JoinRequestPacket : IPacket
    {
        public int ProtocolVersion;
        public string Name;

        public PacketId Id
        {
            get { return PacketId.JoinRequest; }
        }

        public void Write(PacketWriter writer)
        {
            writer.WriteInt32(ProtocolVersion);
            writer.WriteString(Name);
        }

        public void Read(PacketReader reader)
        {
            ProtocolVersion = reader.ReadInt32();
            Name = reader.ReadString();
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
}
