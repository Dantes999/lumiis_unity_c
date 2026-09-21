using System;
using Lumiis.Bot;
using Lumiis.Shared;
using Xunit;

namespace Lumiis.Tests.Bot
{
    public class BotTargetPickerTests
    {
        [Fact]
        public void GetRange_ZeroRadius_IsWholeMap()
        {
            BotTargetPicker.GetRange(50000, 50000, 0, out int minX, out int maxX, out int minY, out int maxY);

            Assert.Equal(0, minX);
            Assert.Equal(GameConstants.MapWidth, maxX);
            Assert.Equal(0, minY);
            Assert.Equal(GameConstants.MapHeight, maxY);
        }

        [Fact]
        public void GetRange_NegativeRadius_IsWholeMap()
        {
            BotTargetPicker.GetRange(50000, 50000, -5, out int minX, out int maxX, out int minY, out int maxY);

            Assert.Equal(0, minX);
            Assert.Equal(GameConstants.MapWidth, maxX);
        }

        [Fact]
        public void GetRange_AwayFromEdges_IsExactSquareAroundSpawn()
        {
            // Spawn in der Kartenmitte, radius 20 Einheiten = 2000 Serverwerte, weit weg von jeder Grenze.
            BotTargetPicker.GetRange(50000, 50000, 20, out int minX, out int maxX, out int minY, out int maxY);

            Assert.Equal(48000, minX);
            Assert.Equal(52000, maxX);
            Assert.Equal(48000, minY);
            Assert.Equal(52000, maxY);
        }

        [Fact]
        public void GetRange_NearMapEdge_IsClampedToMapBounds()
        {
            // Spawn nahe der Kartengrenze (0,0), radius 20 Einheiten würde ins Negative reichen.
            BotTargetPicker.GetRange(500, 500, 20, out int minX, out int maxX, out int minY, out int maxY);

            Assert.Equal(0, minX);
            Assert.Equal(2500, maxX);
            Assert.Equal(0, minY);
            Assert.Equal(2500, maxY);
        }

        [Fact]
        public void GetRange_NearOppositeMapEdge_IsClampedToMapBounds()
        {
            int spawn = GameConstants.MapWidth - 500;
            BotTargetPicker.GetRange(spawn, spawn, 20, out int minX, out int maxX, out int minY, out int maxY);

            Assert.Equal(spawn - 2000, minX);
            Assert.Equal(GameConstants.MapWidth, maxX);
            Assert.Equal(spawn - 2000, minY);
            Assert.Equal(GameConstants.MapHeight, maxY);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(5)]
        [InlineData(20)]
        [InlineData(1000)]
        public void PickTarget_AlwaysWithinComputedRange(int radiusUnits)
        {
            var random = new Random(12345);
            BotTargetPicker.GetRange(50000, 50000, radiusUnits, out int minX, out int maxX, out int minY, out int maxY);

            for (int i = 0; i < 200; i++)
            {
                BotTargetPicker.PickTarget(50000, 50000, radiusUnits, random, out int x, out int y);
                Assert.InRange(x, minX, maxX);
                Assert.InRange(y, minY, maxY);
            }
        }

        [Fact]
        public void PickTarget_AlwaysWithinMapBounds()
        {
            var random = new Random(999);
            for (int i = 0; i < 200; i++)
            {
                BotTargetPicker.PickTarget(100, 100, 50, random, out int x, out int y);
                Assert.InRange(x, 0, GameConstants.MapWidth);
                Assert.InRange(y, 0, GameConstants.MapHeight);
            }
        }
    }
}
