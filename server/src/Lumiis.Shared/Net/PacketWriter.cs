using System;
using System.Text;

namespace Lumiis.Shared.Net
{
    /// <summary>Schreibt Werte Little-Endian in einen wachsenden Puffer.</summary>
    public sealed class PacketWriter
    {
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);

        private byte[] _buffer;
        private int _length;

        public PacketWriter(int initialCapacity = 64)
        {
            _buffer = new byte[initialCapacity < 16 ? 16 : initialCapacity];
        }

        public int Length
        {
            get { return _length; }
        }

        public byte[] ToArray()
        {
            byte[] copy = new byte[_length];
            Buffer.BlockCopy(_buffer, 0, copy, 0, _length);
            return copy;
        }

        public void WriteByte(byte value)
        {
            EnsureCapacity(1);
            _buffer[_length++] = value;
        }

        public void WriteBool(bool value)
        {
            WriteByte(value ? (byte)1 : (byte)0);
        }

        public void WriteUInt16(ushort value)
        {
            EnsureCapacity(2);
            _buffer[_length++] = (byte)value;
            _buffer[_length++] = (byte)(value >> 8);
        }

        public void WriteInt16(short value)
        {
            WriteUInt16((ushort)value);
        }

        public void WriteUInt32(uint value)
        {
            EnsureCapacity(4);
            _buffer[_length++] = (byte)value;
            _buffer[_length++] = (byte)(value >> 8);
            _buffer[_length++] = (byte)(value >> 16);
            _buffer[_length++] = (byte)(value >> 24);
        }

        public void WriteInt32(int value)
        {
            WriteUInt32((uint)value);
        }

        public void WriteInt64(long value)
        {
            WriteUInt32((uint)value);
            WriteUInt32((uint)(value >> 32));
        }

        /// <summary>UTF-8 mit 2-Byte-Längenpräfix. null wird als leerer String geschrieben.</summary>
        public void WriteString(string value)
        {
            if (value == null) value = string.Empty;
            int byteCount = Utf8.GetByteCount(value);
            if (byteCount > Protocol.MaxStringBytes)
                throw new ArgumentException("String zu lang (max. " + Protocol.MaxStringBytes + " Bytes)", nameof(value));

            WriteUInt16((ushort)byteCount);
            if (byteCount == 0) return;

            EnsureCapacity(byteCount);
            Utf8.GetBytes(value, 0, value.Length, _buffer, _length);
            _length += byteCount;
        }

        private void EnsureCapacity(int additional)
        {
            int needed = _length + additional;
            if (needed <= _buffer.Length) return;
            int newSize = _buffer.Length * 2;
            if (newSize < needed) newSize = needed;
            Array.Resize(ref _buffer, newSize);
        }
    }
}
