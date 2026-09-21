using System;
using System.Text;

namespace Lumiis.Shared.Net
{
    /// <summary>Wird geworfen, wenn ein empfangenes Paket fehlerhaft ist.</summary>
    public sealed class PacketFormatException : Exception
    {
        public PacketFormatException(string message) : base(message) { }
    }

    /// <summary>Liest Little-Endian-Werte mit strengen Grenzprüfungen.</summary>
    public sealed class PacketReader
    {
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);

        private readonly byte[] _data;
        private readonly int _end;
        private int _position;

        public PacketReader(byte[] data) : this(data, 0, data == null ? 0 : data.Length) { }

        public PacketReader(byte[] data, int offset, int count)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (offset < 0 || count < 0 || offset > data.Length - count)
                throw new ArgumentOutOfRangeException(nameof(count));
            _data = data;
            _position = offset;
            _end = offset + count;
        }

        public int Remaining
        {
            get { return _end - _position; }
        }

        private int Take(int count)
        {
            if (count < 0 || count > _end - _position)
                throw new PacketFormatException("Paket zu kurz");
            int start = _position;
            _position += count;
            return start;
        }

        public byte ReadByte()
        {
            return _data[Take(1)];
        }

        public bool ReadBool()
        {
            byte value = ReadByte();
            if (value > 1) throw new PacketFormatException("Ungültiger Bool-Wert");
            return value == 1;
        }

        public ushort ReadUInt16()
        {
            int p = Take(2);
            return (ushort)(_data[p] | (_data[p + 1] << 8));
        }

        public short ReadInt16()
        {
            return (short)ReadUInt16();
        }

        public uint ReadUInt32()
        {
            int p = Take(4);
            return (uint)(_data[p] | (_data[p + 1] << 8) | (_data[p + 2] << 16) | (_data[p + 3] << 24));
        }

        public int ReadInt32()
        {
            return (int)ReadUInt32();
        }

        public long ReadInt64()
        {
            ulong low = ReadUInt32();
            ulong high = ReadUInt32();
            return (long)(low | (high << 32));
        }

        public string ReadString()
        {
            int length = ReadUInt16();
            if (length > Protocol.MaxStringBytes)
                throw new PacketFormatException("String zu lang");
            if (length == 0) return string.Empty;

            int start = Take(length);
            try
            {
                return Utf8.GetString(_data, start, length);
            }
            catch (ArgumentException)
            {
                throw new PacketFormatException("Ungültiges UTF-8");
            }
        }
    }
}
