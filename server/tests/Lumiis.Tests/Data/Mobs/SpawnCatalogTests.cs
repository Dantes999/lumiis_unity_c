using System;
using System.Text;
using Lumiis.Data.Maps;
using Lumiis.Data.Mobs;
using Xunit;

namespace Lumiis.Tests.Data.Mobs
{
    public class SpawnCatalogTests
    {
        private const string ValidMobsJson = @"{
  ""mobs"": [
    { ""id"": 1, ""name"": ""Wildhund"", ""speedWire"": 700, ""wanderRadiusWire"": 500, ""wanderIntervalMinMs"": 2000, ""wanderIntervalMaxMs"": 5000 }
  ]
}";

        /// <summary>Synthetische 10x10-Zellen-CollisionMap direkt aus Bytes (kein echtes File nötig, analog zu
        /// CollisionMapTests) - Zelle (5,5) blockiert, alles andere begehbar.</summary>
        private static CollisionMap BuildCollisionMapWithBlockedCell(int blockedX, int blockedY)
        {
            const int width = 10;
            const int height = 10;
            byte[] packed = new byte[(width * height + 7) / 8];
            int index = blockedY * width + blockedX;
            packed[index / 8] |= (byte)(1 << (index % 8));

            byte[] header = new byte[12];
            Encoding.ASCII.GetBytes("LCM1").CopyTo(header, 0);
            BitConverter.GetBytes(width).CopyTo(header, 4);
            BitConverter.GetBytes(height).CopyTo(header, 8);

            byte[] bytes = new byte[header.Length + packed.Length];
            header.CopyTo(bytes, 0);
            packed.CopyTo(bytes, header.Length);
            return CollisionMap.LoadFromBytes(bytes, width, height);
        }

        [Fact]
        public void LoadFromJson_ValidData_ExposesSpawns()
        {
            MobCatalog mobCatalog = MobCatalog.LoadFromJson(ValidMobsJson);
            CollisionMap collisionMap = BuildCollisionMapWithBlockedCell(5, 5);
            const string json = @"{
  ""spawns"": [
    { ""id"": 1, ""mobId"": 1, ""positionX"": 100, ""positionY"": 100, ""respawnSeconds"": 30 }
  ]
}";
            SpawnCatalog catalog = SpawnCatalog.LoadFromJson(json, mobCatalog, collisionMap);

            Assert.Single(catalog.Spawns);
            SpawnPoint spawn = catalog.Spawns[0];
            Assert.Equal(1, spawn.Id);
            Assert.Equal(1, spawn.MobId);
            Assert.Equal(100, spawn.PositionX);
            Assert.Equal(100, spawn.PositionY);
            Assert.Equal(30, spawn.RespawnSeconds);
        }

        [Fact]
        public void LoadFromJson_UnknownMobId_ThrowsWithSpawnIdInMessage()
        {
            MobCatalog mobCatalog = MobCatalog.LoadFromJson(ValidMobsJson);
            CollisionMap collisionMap = BuildCollisionMapWithBlockedCell(5, 5);
            const string json = @"{
  ""spawns"": [
    { ""id"": 7, ""mobId"": 999, ""positionX"": 100, ""positionY"": 100, ""respawnSeconds"": 30 }
  ]
}";
            var ex = Assert.Throws<SpawnCatalogException>(() => SpawnCatalog.LoadFromJson(json, mobCatalog, collisionMap));
            Assert.Contains("Spawn-Punkt 7", ex.Message);
            Assert.Contains("999", ex.Message);
        }

        [Fact]
        public void LoadFromJson_PositionOnBlockedCell_ThrowsWithSpawnIdInMessage()
        {
            MobCatalog mobCatalog = MobCatalog.LoadFromJson(ValidMobsJson);
            CollisionMap collisionMap = BuildCollisionMapWithBlockedCell(5, 5);
            const string json = @"{
  ""spawns"": [
    { ""id"": 3, ""mobId"": 1, ""positionX"": 550, ""positionY"": 550, ""respawnSeconds"": 30 }
  ]
}";
            var ex = Assert.Throws<SpawnCatalogException>(() => SpawnCatalog.LoadFromJson(json, mobCatalog, collisionMap));
            Assert.Contains("Spawn-Punkt 3", ex.Message);
            Assert.Contains("blockierte", ex.Message);
        }

        [Fact]
        public void LoadFromJson_DuplicateId_Throws()
        {
            MobCatalog mobCatalog = MobCatalog.LoadFromJson(ValidMobsJson);
            CollisionMap collisionMap = BuildCollisionMapWithBlockedCell(5, 5);
            const string json = @"{
  ""spawns"": [
    { ""id"": 1, ""mobId"": 1, ""positionX"": 100, ""positionY"": 100, ""respawnSeconds"": 30 },
    { ""id"": 1, ""mobId"": 1, ""positionX"": 200, ""positionY"": 200, ""respawnSeconds"": 30 }
  ]
}";
            Assert.Throws<SpawnCatalogException>(() => SpawnCatalog.LoadFromJson(json, mobCatalog, collisionMap));
        }
    }
}
