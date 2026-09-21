using System;

namespace Lumiis.Shared.Net
{
    /// <summary>Ebenen-Zusammensetzung eines Charakters (Paper Doll). 0 = keine Ebene, außer BodyId.
    /// Eine einzige Write/Read-Implementierung, die sowohl JoinAcceptedPacket (eigene Figur) als auch
    /// EntitySpawnPacket (andere Figuren) verwenden - keine doppelte Serialisierung.</summary>
    public struct Appearance : IEquatable<Appearance>
    {
        public int BodyId;
        public int HairStyleId;
        public int HairColorId;
        public int ArmorId;
        public int HelmetId;
        public int WeaponId;
        public int ShieldId;

        public void Write(PacketWriter writer)
        {
            writer.WriteInt32(BodyId);
            writer.WriteInt32(HairStyleId);
            writer.WriteInt32(HairColorId);
            writer.WriteInt32(ArmorId);
            writer.WriteInt32(HelmetId);
            writer.WriteInt32(WeaponId);
            writer.WriteInt32(ShieldId);
        }

        public void Read(PacketReader reader)
        {
            BodyId = reader.ReadInt32();
            HairStyleId = reader.ReadInt32();
            HairColorId = reader.ReadInt32();
            ArmorId = reader.ReadInt32();
            HelmetId = reader.ReadInt32();
            WeaponId = reader.ReadInt32();
            ShieldId = reader.ReadInt32();
        }

        public bool Equals(Appearance other)
        {
            return BodyId == other.BodyId
                && HairStyleId == other.HairStyleId
                && HairColorId == other.HairColorId
                && ArmorId == other.ArmorId
                && HelmetId == other.HelmetId
                && WeaponId == other.WeaponId
                && ShieldId == other.ShieldId;
        }

        public override bool Equals(object obj)
        {
            return obj is Appearance other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + BodyId;
                hash = hash * 31 + HairStyleId;
                hash = hash * 31 + HairColorId;
                hash = hash * 31 + ArmorId;
                hash = hash * 31 + HelmetId;
                hash = hash * 31 + WeaponId;
                hash = hash * 31 + ShieldId;
                return hash;
            }
        }

        public static bool operator ==(Appearance left, Appearance right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(Appearance left, Appearance right)
        {
            return !left.Equals(right);
        }
    }
}
