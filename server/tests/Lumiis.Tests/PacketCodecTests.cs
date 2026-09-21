using System;
using Lumiis.Shared;
using Lumiis.Shared.Net;
using Xunit;

namespace Lumiis.Tests
{
    public class PacketCodecTests
    {
        [Fact]
        public void JoinRequest_RoundTrip()
        {
            var original = new JoinRequestPacket { ProtocolVersion = Protocol.Version, Name = "\u00C4rger" };
            var decoded = Assert.IsType<JoinRequestPacket>(PacketCodec.Decode(PacketCodec.Encode(original)));
            Assert.Equal(original.ProtocolVersion, decoded.ProtocolVersion);
            Assert.Equal(original.Name, decoded.Name);
        }

        [Fact]
        public void MoveTo_RoundTrip()
        {
            var original = new MoveToPacket { X = -12345, Y = 987654 };
            var decoded = Assert.IsType<MoveToPacket>(PacketCodec.Decode(PacketCodec.Encode(original)));
            Assert.Equal(-12345, decoded.X);
            Assert.Equal(987654, decoded.Y);
        }

        [Fact]
        public void Decode_HonorsOffsetAndCount()
        {
            byte[] body = PacketCodec.Encode(new MoveToPacket { X = 1, Y = 2 });
            byte[] framed = new byte[body.Length + 3];
            Buffer.BlockCopy(body, 0, framed, 2, body.Length);

            var decoded = Assert.IsType<MoveToPacket>(PacketCodec.Decode(framed, 2, body.Length));
            Assert.Equal(1, decoded.X);
            Assert.Equal(2, decoded.Y);
        }

        [Fact]
        public void UnknownId_Throws()
        {
            Assert.Throws<PacketFormatException>(() => PacketCodec.Decode(new byte[] { 0xFF, 0xFF }));
        }

        [Fact]
        public void NoneId_IsRejected()
        {
            Assert.Throws<PacketFormatException>(() => PacketCodec.Decode(new byte[] { 0, 0 }));
        }

        [Fact]
        public void TrailingBytes_Throw()
        {
            byte[] data = PacketCodec.Encode(new MoveToPacket { X = 1, Y = 2 });
            Array.Resize(ref data, data.Length + 1);
            Assert.Throws<PacketFormatException>(() => PacketCodec.Decode(data));
        }

        [Fact]
        public void EmptyData_Throws()
        {
            Assert.Throws<PacketFormatException>(() => PacketCodec.Decode(new byte[0]));
        }

        [Fact]
        public void EveryPacketId_HasFactoryEntry()
        {
            foreach (PacketId id in Enum.GetValues(typeof(PacketId)))
            {
                if (id == PacketId.None) continue;
                IPacket packet = PacketFactory.Create(id);
                Assert.NotNull(packet);
                Assert.Equal(id, packet.Id);
            }
        }
    }
}
