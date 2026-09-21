using LiteNetLib;
using Lumiis.Shared.Net;

namespace Lumiis.Server.Net;

/// <summary>Dünner Adapter: kodiert ein IPacket und verschickt es reliable-ordered.</summary>
public static class NetPacketExtensions
{
    public static void SendPacket(this NetPeer peer, IPacket packet)
    {
        peer.Send(PacketCodec.Encode(packet), DeliveryMethod.ReliableOrdered);
    }
}
