using Lumiis.Shared.Net;
using Xunit;

namespace Lumiis.Tests
{
    public class AppearanceTests
    {
        [Fact]
        public void RoundTrip_PreservesAllFields()
        {
            var original = new Appearance
            {
                BodyId = 1,
                HairStyleId = 2,
                HairColorId = 5,
                ArmorId = 3,
                HelmetId = 0,
                WeaponId = 4,
                ShieldId = 0
            };

            var writer = new PacketWriter();
            original.Write(writer);
            var reader = new PacketReader(writer.ToArray());

            var decoded = new Appearance();
            decoded.Read(reader);

            Assert.Equal(0, reader.Remaining);
            Assert.Equal(original, decoded);
        }

        [Fact]
        public void Equals_SameValues_AreEqual()
        {
            var a = new Appearance { BodyId = 1, ArmorId = 2 };
            var b = new Appearance { BodyId = 1, ArmorId = 2 };

            Assert.Equal(a, b);
            Assert.True(a == b);
            Assert.False(a != b);
            Assert.Equal(a.GetHashCode(), b.GetHashCode());
        }

        [Theory]
        [InlineData(2, 1, 0, 0, 0, 0, 0)]
        [InlineData(1, 2, 0, 0, 0, 0, 0)]
        [InlineData(1, 1, 1, 0, 0, 0, 0)]
        [InlineData(1, 1, 0, 1, 0, 0, 0)]
        [InlineData(1, 1, 0, 0, 1, 0, 0)]
        [InlineData(1, 1, 0, 0, 0, 1, 0)]
        [InlineData(1, 1, 0, 0, 0, 0, 1)]
        public void Equals_DifferentValues_AreNotEqual(int bodyId, int hairStyleId, int hairColorId, int armorId, int helmetId, int weaponId, int shieldId)
        {
            var baseline = new Appearance { BodyId = 1, HairStyleId = 1 };
            var other = new Appearance
            {
                BodyId = bodyId,
                HairStyleId = hairStyleId,
                HairColorId = hairColorId,
                ArmorId = armorId,
                HelmetId = helmetId,
                WeaponId = weaponId,
                ShieldId = shieldId
            };

            Assert.NotEqual(baseline, other);
        }
    }
}
