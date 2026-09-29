using System;
using System.IO;
using System.Text;
using Lumiis.Data.Maps;
using Lumiis.Data.Mobs;
using Lumiis.Shared;
using Xunit;

namespace Lumiis.Tests.Data.Mobs
{
    public class SpawnCatalogTests
    {
        private const string ValidMobsJson = @"{
  ""mobs"": [
    { ""id"": 1, ""name"": ""Wildhund"", ""speedWire"": 700, ""wanderRadiusWire"": 500, ""wanderIntervalMinMs"": 2000, ""wanderIntervalMaxMs"": 5000, ""maxHp"": 30, ""attack"": 5, ""defense"": 0, ""yangMin"": 5, ""yangMax"": 15, ""meleeRangeWire"": 130, ""attackCooldownMs"": 700, ""aggressive"": true, ""aggroRangeWire"": 400, ""leashRangeWire"": 700, ""level"": 3, ""baseXp"": 30 }
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

        // ---- Dev-Tools: erweiterte, echte spawns.json (siehe docs/tasks/dev-tools-debug-spawn.md) ----

        /// <summary>Sucht data/ ausgehend vom tatsächlichen Testassembly-Verzeichnis (AppContext.BaseDirectory) -
        /// läuft die Verzeichnisse bis zum Repo-Root hoch, analog zu GameServerIntegrationTests.ResolveVillageCollisionMapPath.</summary>
        private static string ResolveDataDir()
        {
            DirectoryInfo? dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                string candidate = Path.Combine(dir.FullName, "data");
                if (File.Exists(Path.Combine(candidate, "mobs.json"))) return candidate;
                dir = dir.Parent;
            }
            throw new FileNotFoundException("data/mobs.json nicht gefunden (ausgehend von " + AppContext.BaseDirectory + ").");
        }

        [Fact]
        public void LoadFromFile_RealVillageSpawns_LoadsWithoutErrorIncludingDevToolsSpawnPoints()
        {
            string dataDir = ResolveDataDir();
            CollisionMap collisionMap = CollisionMap.LoadFromFile(
                Path.Combine(dataDir, "maps", "village", "collision.bin"), GameConstants.MapWidth / 100, GameConstants.MapHeight / 100);
            MobCatalog mobCatalog = MobCatalog.LoadFromFile(Path.Combine(dataDir, "mobs.json"));

            SpawnCatalog catalog = SpawnCatalog.LoadFromFile(
                Path.Combine(dataDir, "maps", "village", "spawns.json"), mobCatalog, collisionMap);

            Assert.True(catalog.Spawns.Count >= 9); // 5 urspruengliche + 4 neue (Dev-Tools Abschnitt 1)
            Assert.Contains(catalog.Spawns, s => s.Id == 6);
            Assert.Contains(catalog.Spawns, s => s.Id == 9);
        }
    }
}
