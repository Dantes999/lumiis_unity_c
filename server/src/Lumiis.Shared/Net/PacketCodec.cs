using System;

namespace Lumiis.Shared.Net
{
    public interface IPacket
    {
        PacketId Id { get; }
        void Write(PacketWriter writer);
        void Read(PacketReader reader);
    }

    /// <summary>Wandelt Pakete in Bytes um und zurück (2 Byte Paket-ID + Nutzdaten).</summary>
    public static class PacketCodec
    {
        public static byte[] Encode(IPacket packet)
        {
            if (packet == null) throw new ArgumentNullException(nameof(packet));
            PacketWriter writer = new PacketWriter();
            writer.WriteUInt16((ushort)packet.Id);
            packet.Write(writer);
            return writer.ToArray();
        }

        public static IPacket Decode(byte[] data)
        {
            return Decode(data, 0, data == null ? 0 : data.Length);
        }

        /// <summary>Wirft PacketFormatException bei unbekannter ID, kaputten oder überschüssigen Daten.</summary>
        public static IPacket Decode(byte[] data, int offset, int count)
        {
            PacketReader reader = new PacketReader(data, offset, count);
            PacketId id = (PacketId)reader.ReadUInt16();
            IPacket packet = PacketFactory.Create(id);
            if (packet == null)
                throw new PacketFormatException("Unbekannte Paket-ID: " + (ushort)id);

            packet.Read(reader);
            if (reader.Remaining != 0)
                throw new PacketFormatException("Überschüssige Bytes im Paket");
            return packet;
        }
    }
}
