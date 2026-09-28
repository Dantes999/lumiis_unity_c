using System;
using System.Text;
using Lumiis.Data.Items;
using Lumiis.Data.Maps;
using Lumiis.Data.Npcs;
using Lumiis.Tests;
using Xunit;

namespace Lumiis.Tests.Data.Npcs
{
    public class NpcSpawnCatalogTests
    {
        private const string ValidItemsJson = @"{
  ""items"": [
    { ""id"": 1, ""name"": ""Testschwert"", ""itemType"": ""Weapon"", ""lookId"": 1, ""attackBonus"": 5, ""defenseBonus"": 0, ""buyPrice"": 200, ""sellPrice"": 80 }
  ]
}";

        private const string ValidNpcsJson = @"{
  ""npcs"": [
    { ""id"": 1, ""name"": ""Dorfhaendler"", ""appearance"": { ""bodyId"": 1, ""hairStyleId"": 1, ""hairColorId"": 1 }, ""shopItemIds"": [1] }
  ]
}";

        private static NpcCatalog CreateNpcCatalog()
        {
            ItemCatalog itemCatalog = ItemCatalog.LoadFromJson(ValidItemsJson, TestLookCatalogs.CreateMinimal());
            return NpcCatalog.LoadFromJson(ValidNpcsJson, itemCatalog, TestLookCatalogs.CreateMinimal());
        }

        /// <summary>Synthetische 10x10-Zellen-CollisionMap direkt aus Bytes, Zelle (5,5) blockiert - analog zu
        /// SpawnCatalogTests.</summary>
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
        public void LoadFromJson_ValidData_ExposesPlacements()
        {
            NpcCatalog npcCatalog = CreateNpcCatalog();
            CollisionMap collisionMap = BuildCollisionMapWithBlockedCell(5, 5);
            const string json = @"{ ""placements"": [ { ""id"": 1, ""npcId"": 1, ""positionX"": 100, ""positionY"": 100 } ] }";

            NpcSpawnCatalog catalog = NpcSpawnCatalog.LoadFromJson(json, npcCatalog, collisionMap);

            Assert.Single(catalog.Placements);
            NpcPlacement placement = catalog.Placements[0];
            Assert.Equal(1, placement.Id);
            Assert.Equal(1, placement.NpcId);
            Assert.Equal(100, placement.PositionX);
            Assert.Equal(100, placement.PositionY);
        }

        [Fact]
        public void LoadFromJson_UnknownNpcId_ThrowsWithPlacementIdInMessage()
        {
            NpcCatalog npcCatalog = CreateNpcCatalog();
            CollisionMap collisionMap = BuildCollisionMapWithBlockedCell(5, 5);
            const string json = @"{ ""placements"": [ { ""id"": 3, ""npcId"": 999, ""positionX"": 100, ""positionY"": 100 } ] }";

            var ex = Assert.Throws<NpcSpawnCatalogException>(() => NpcSpawnCatalog.LoadFromJson(json, npcCatalog, collisionMap));
            Assert.Contains("Platzierung 3", ex.Message);
            Assert.Contains("999", ex.Message);
        }

        [Fact]
        public void LoadFromJson_PositionOnBlockedCell_ThrowsWithPlacementIdInMessage()
        {
            NpcCatalog npcCatalog = CreateNpcCatalog();
            CollisionMap collisionMap = BuildCollisionMapWithBlockedCell(5, 5);
            const string json = @"{ ""placements"": [ { ""id"": 2, ""npcId"": 1, ""positionX"": 550, ""positionY"": 550 } ] }";

            var ex = Assert.Throws<NpcSpawnCatalogException>(() => NpcSpawnCatalog.LoadFromJson(json, npcCatalog, collisionMap));
            Assert.Contains("Platzierung 2", ex.Message);
            Assert.Contains("blockierte", ex.Message);
        }

        [Fact]
        public void LoadFromJson_DuplicateId_Throws()
        {
            NpcCatalog npcCatalog = CreateNpcCatalog();
            CollisionMap collisionMap = BuildCollisionMapWithBlockedCell(5, 5);
            const string json = @"{
  ""placements"": [
    { ""id"": 1, ""npcId"": 1, ""positionX"": 100, ""positionY"": 100 },
    { ""id"": 1, ""npcId"": 1, ""positionX"": 200, ""positionY"": 200 }
  ]
}";
            Assert.Throws<NpcSpawnCatalogException>(() => NpcSpawnCatalog.LoadFromJson(json, npcCatalog, collisionMap));
        }
    }
}
