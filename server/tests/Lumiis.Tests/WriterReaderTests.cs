using System;
using Lumiis.Shared;
using Lumiis.Shared.Net;
using Xunit;

namespace Lumiis.Tests
{
    public class WriterReaderTests
    {
        [Fact]
        public void Primitives_RoundTrip()
        {
            var w = new PacketWriter();
            w.WriteByte(200);
            w.WriteBool(true);
            w.WriteBool(false);
            w.WriteInt16(-1234);
            w.WriteUInt16(65000);
            w.WriteInt32(-123456789);
            w.WriteUInt32(4000000000u);
            w.WriteInt64(-9000000000000L);

            var r = new PacketReader(w.ToArray());
            Assert.Equal((byte)200, r.ReadByte());
            Assert.True(r.ReadBool());
            Assert.False(r.ReadBool());
            Assert.Equal((short)-1234, r.ReadInt16());
            Assert.Equal((ushort)65000, r.ReadUInt16());
            Assert.Equal(-123456789, r.ReadInt32());
            Assert.Equal(4000000000u, r.ReadUInt32());
            Assert.Equal(-9000000000000L, r.ReadInt64());
            Assert.Equal(0, r.Remaining);
        }

        [Fact]
        public void Int32_IsLittleEndian()
        {
            var w = new PacketWriter();
            w.WriteInt32(0x01020304);
            Assert.Equal(new byte[] { 4, 3, 2, 1 }, w.ToArray());
        }

        [Theory]
        [InlineData("")]
        [InlineData("Lumiis")]
        [InlineData("Gr\u00F6\u00DFe \u00E4\u00F6\u00FC")]
        [InlineData("\u65E5\u672C\u8A9E \u2713")]
        public void String_RoundTrip(string value)
        {
            var w = new PacketWriter();
            w.WriteString(value);
            var r = new PacketReader(w.ToArray());
            Assert.Equal(value, r.ReadString());
            Assert.Equal(0, r.Remaining);
        }

        [Fact]
        public void NullString_IsWrittenAsEmpty()
        {
            var w = new PacketWriter();
            w.WriteString(null);
            var r = new PacketReader(w.ToArray());
            Assert.Equal(string.Empty, r.ReadString());
        }

        [Fact]
        public void MaxLengthString_IsAllowed()
        {
            string value = new string('a', Protocol.MaxStringBytes);
            var w = new PacketWriter();
            w.WriteString(value);
            var r = new PacketReader(w.ToArray());
            Assert.Equal(value, r.ReadString());
        }

        [Fact]
        public void TooLongString_ThrowsOnWrite()
        {
            var w = new PacketWriter();
            string value = new string('a', Protocol.MaxStringBytes + 1);
            Assert.Throws<ArgumentException>(() => w.WriteString(value));
        }

        [Fact]
        public void ReadingPastEnd_Throws()
        {
            var r = new PacketReader(new byte[] { 1, 2 });
            Assert.Throws<PacketFormatException>(() => r.ReadInt32());
        }

        [Fact]
        public void StringLengthBeyondData_Throws()
        {
            // 100 Bytes angekündigt, aber nur 1 Byte folgt
            var r = new PacketReader(new byte[] { 100, 0, 65 });
            Assert.Throws<PacketFormatException>(() => r.ReadString());
        }

        [Fact]
        public void StringLengthAboveLimit_Throws()
        {
            var w = new PacketWriter();
            w.WriteUInt16((ushort)(Protocol.MaxStringBytes + 1));
            var r = new PacketReader(w.ToArray());
            Assert.Throws<PacketFormatException>(() => r.ReadString());
        }

        [Fact]
        public void InvalidUtf8_Throws()
        {
            var r = new PacketReader(new byte[] { 1, 0, 0xFF });
            Assert.Throws<PacketFormatException>(() => r.ReadString());
        }

        [Fact]
        public void InvalidBool_Throws()
        {
            var r = new PacketReader(new byte[] { 2 });
            Assert.Throws<PacketFormatException>(() => r.ReadBool());
        }

        [Fact]
        public void Writer_GrowsBeyondInitialCapacity()
        {
            var w = new PacketWriter(16);
            for (int i = 0; i < 10000; i++) w.WriteInt32(i);

            var r = new PacketReader(w.ToArray());
            for (int i = 0; i < 10000; i++) Assert.Equal(i, r.ReadInt32());
            Assert.Equal(0, r.Remaining);
        }
    }
}
