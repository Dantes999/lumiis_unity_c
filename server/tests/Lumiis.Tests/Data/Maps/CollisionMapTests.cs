using System;
using System.Text;
using Lumiis.Data.Maps;
using Xunit;

namespace Lumiis.Tests.Data.Maps
{
    /// <summary>Testet den CollisionMap-Loader mit direkt im Test konstruierten Bytes (kein Python/Bake-
    /// Skript nötig, siehe docs/tasks/M3a-collision.md) - Rundtrip, Grenzprüfung, kaputte Header.</summary>
    public class CollisionMapTests
    {
        /// <summary>Baut die Bytes für eine width x height CollisionMap von Hand (gleiches Format wie
        /// tools/bake-collision.py: Magic "LCM1", Breite/Höhe als int32 LE, dann 1-Bit-pro-Zelle,
        /// Little-Endian, zeilenweise).</summary>
        private static byte[] BuildBytes(int width, int height, params (int X, int Y)[] blockedCells)
        {
            int cellCount = width * height;
            byte[] packed = new byte[(cellCount + 7) / 8];
            foreach ((int x, int y) in blockedCells)
            {
                int index = y * width + x;
                packed[index / 8] |= (byte)(1 << (index % 8));
            }

            byte[] header = new byte[12];
            Encoding.ASCII.GetBytes("LCM1").CopyTo(header, 0);
            BitConverter.GetBytes(width).CopyTo(header, 4);
            BitConverter.GetBytes(height).CopyTo(header, 8);

            byte[] bytes = new byte[header.Length + packed.Length];
            header.CopyTo(bytes, 0);
            packed.CopyTo(bytes, header.Length);
            return bytes;
        }

        [Fact]
        public void LoadFromBytes_RoundTrip_MatchesBlockedCells()
        {
            byte[] bytes = BuildBytes(4, 3, (1, 0), (3, 2));

            CollisionMap map = CollisionMap.LoadFromBytes(bytes, expectedWidth: 4, expectedHeight: 3);

            Assert.Equal(4, map.Width);
            Assert.Equal(3, map.Height);
            Assert.True(map.IsBlocked(1, 0));
            Assert.True(map.IsBlocked(3, 2));
            Assert.False(map.IsBlocked(0, 0));
            Assert.False(map.IsBlocked(2, 1));
        }

        [Fact]
        public void LoadFromBytes_AllOpen_NothingBlocked()
        {
            byte[] bytes = BuildBytes(5, 5);

            CollisionMap map = CollisionMap.LoadFromBytes(bytes, expectedWidth: 5, expectedHeight: 5);

            for (int y = 0; y < 5; y++)
            for (int x = 0; x < 5; x++)
                Assert.False(map.IsBlocked(x, y));
        }

        [Fact]
        public void IsBlocked_OutsideMap_CountsAsBlocked()
        {
            byte[] bytes = BuildBytes(4, 4);
            CollisionMap map = CollisionMap.LoadFromBytes(bytes, expectedWidth: 4, expectedHeight: 4);

            Assert.True(map.IsBlocked(-1, 0));
            Assert.True(map.IsBlocked(0, -1));
            Assert.True(map.IsBlocked(4, 0));
            Assert.True(map.IsBlocked(0, 4));
        }

        [Fact]
        public void IsBlockedAtWire_ConvertsWireCoordinatesToCells()
        {
            byte[] bytes = BuildBytes(4, 4, (2, 1));
            CollisionMap map = CollisionMap.LoadFromBytes(bytes, expectedWidth: 4, expectedHeight: 4);

            Assert.True(map.IsBlockedAtWire(250, 150)); // Zelle (2,1)
            Assert.False(map.IsBlockedAtWire(0, 0));    // Zelle (0,0)
        }

        [Fact]
        public void LoadFromBytes_WrongMagic_ThrowsWithClearMessage()
        {
            byte[] bytes = BuildBytes(2, 2);
            bytes[0] = (byte)'X'; // Magic kaputt

            var ex = Assert.Throws<CollisionMapException>(() => CollisionMap.LoadFromBytes(bytes, 2, 2));
            Assert.Contains("Magic", ex.Message);
        }

        [Fact]
        public void LoadFromBytes_TooShortForHeader_ThrowsWithClearMessage()
        {
            byte[] bytes = new byte[] { 1, 2, 3 };

            var ex = Assert.Throws<CollisionMapException>(() => CollisionMap.LoadFromBytes(bytes, 2, 2));
            Assert.Contains("Header", ex.Message);
        }

        [Fact]
        public void LoadFromBytes_TooShortForCells_ThrowsWithClearMessage()
        {
            byte[] header = new byte[12];
            Encoding.ASCII.GetBytes("LCM1").CopyTo(header, 0);
            BitConverter.GetBytes(10).CopyTo(header, 4);
            BitConverter.GetBytes(10).CopyTo(header, 8);
            // 10x10 = 100 Zellen -> 13 Byte Bitmaske nötig, hier nur 1 Byte angehängt.
            byte[] bytes = new byte[header.Length + 1];
            header.CopyTo(bytes, 0);

            var ex = Assert.Throws<CollisionMapException>(() => CollisionMap.LoadFromBytes(bytes, 10, 10));
            Assert.Contains("zu kurz", ex.Message);
        }

        [Fact]
        public void LoadFromBytes_DimensionMismatch_ThrowsWithClearMessage()
        {
            byte[] bytes = BuildBytes(4, 4);

            var ex = Assert.Throws<CollisionMapException>(() => CollisionMap.LoadFromBytes(bytes, expectedWidth: 8, expectedHeight: 8));
            Assert.Contains("stimmen nicht", ex.Message);
        }
    }
}
