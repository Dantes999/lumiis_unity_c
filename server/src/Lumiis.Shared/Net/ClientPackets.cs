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
}
