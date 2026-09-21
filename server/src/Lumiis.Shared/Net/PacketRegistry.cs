namespace Lumiis.Shared.Net
{
    public enum PacketId : ushort
    {
        None = 0,

        // Client -> Server: 1..999
        JoinRequest = 1,
        MoveTo = 2,

        // Server -> Client: ab 1000
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
                default: return null;
            }
        }
    }
}
