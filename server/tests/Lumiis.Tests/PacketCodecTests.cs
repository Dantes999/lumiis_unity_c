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
        public void JoinAccepted_RoundTrip()
        {
            var original = new JoinAcceptedPacket { EntityId = 42, X = 50000, Y = 50000 };
            var decoded = Assert.IsType<JoinAcceptedPacket>(PacketCodec.Decode(PacketCodec.Encode(original)));
            Assert.Equal(original.EntityId, decoded.EntityId);
            Assert.Equal(original.X, decoded.X);
            Assert.Equal(original.Y, decoded.Y);
        }

        [Fact]
        public void JoinRejected_RoundTrip()
        {
            var original = new JoinRejectedPacket { Reason = JoinRejectReason.NameTaken };
            var decoded = Assert.IsType<JoinRejectedPacket>(PacketCodec.Decode(PacketCodec.Encode(original)));
            Assert.Equal(original.Reason, decoded.Reason);
        }

        [Fact]
        public void EntitySpawn_RoundTrip()
        {
            var original = new EntitySpawnPacket { EntityId = 7, Name = "Held_1", X = 100, Y = -200 };
            var decoded = Assert.IsType<EntitySpawnPacket>(PacketCodec.Decode(PacketCodec.Encode(original)));
            Assert.Equal(original.EntityId, decoded.EntityId);
            Assert.Equal(original.Name, decoded.Name);
            Assert.Equal(original.X, decoded.X);
            Assert.Equal(original.Y, decoded.Y);
        }

        [Fact]
        public void EntityDespawn_RoundTrip()
        {
            var original = new EntityDespawnPacket { EntityId = 7 };
            var decoded = Assert.IsType<EntityDespawnPacket>(PacketCodec.Decode(PacketCodec.Encode(original)));
            Assert.Equal(original.EntityId, decoded.EntityId);
        }

        [Fact]
        public void EntityMove_RoundTrip()
        {
            var original = new EntityMovePacket
            {
                EntityId = 3,
                StartX = 1000,
                StartY = 2000,
                TargetX = 5000,
                TargetY = 6000,
                Speed = 500,
                ServerTick = 123456u
            };
            var decoded = Assert.IsType<EntityMovePacket>(PacketCodec.Decode(PacketCodec.Encode(original)));
            Assert.Equal(original.EntityId, decoded.EntityId);
            Assert.Equal(original.StartX, decoded.StartX);
            Assert.Equal(original.StartY, decoded.StartY);
            Assert.Equal(original.TargetX, decoded.TargetX);
            Assert.Equal(original.TargetY, decoded.TargetY);
            Assert.Equal(original.Speed, decoded.Speed);
            Assert.Equal(original.ServerTick, decoded.ServerTick);
        }

        [Fact]
        public void EntityStop_RoundTrip()
        {
            var original = new EntityStopPacket { EntityId = 3, X = 5000, Y = 6000 };
            var decoded = Assert.IsType<EntityStopPacket>(PacketCodec.Decode(PacketCodec.Encode(original)));
            Assert.Equal(original.EntityId, decoded.EntityId);
            Assert.Equal(original.X, decoded.X);
            Assert.Equal(original.Y, decoded.Y);
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
