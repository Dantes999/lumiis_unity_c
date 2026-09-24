using Lumiis.Data.Items;
using Lumiis.Tests;
using Xunit;

namespace Lumiis.Tests.Data.Items
{
    public class ItemCatalogTests
    {
        private const string ValidJson = @"{
  ""items"": [
    { ""id"": 1, ""name"": ""Holzschwert"", ""itemType"": ""Weapon"", ""lookId"": 1, ""attackBonus"": 2, ""defenseBonus"": 0 },
    { ""id"": 2, ""name"": ""Lederruestung"", ""itemType"": ""Armor"", ""lookId"": 1, ""attackBonus"": 0, ""defenseBonus"": 3 },
    { ""id"": 3, ""name"": ""Heiltrank"", ""itemType"": ""Consumable"", ""healAmount"": 30 }
  ]
}";

        [Fact]
        public void LoadFromJson_ValidData_ExposesItems()
        {
            ItemCatalog catalog = ItemCatalog.LoadFromJson(ValidJson, TestLookCatalogs.CreateMinimal());

            Assert.True(catalog.TryGet(1, out ItemDefinition weapon));
            Assert.Equal("Holzschwert", weapon.Name);
            Assert.Equal(ItemType.Weapon, weapon.ItemType);
            Assert.Equal(1, weapon.LookId);
            Assert.Equal(2, weapon.AttackBonus);
            Assert.Equal(0, weapon.DefenseBonus);
            Assert.True(weapon.IsEquipment);

            Assert.True(catalog.TryGet(3, out ItemDefinition potion));
            Assert.Equal(ItemType.Consumable, potion.ItemType);
            Assert.Equal(30, potion.HealAmount);
            Assert.False(potion.IsEquipment);
        }

        [Fact]
        public void TryGet_UnknownId_ReturnsFalse()
        {
            ItemCatalog catalog = ItemCatalog.LoadFromJson(ValidJson, TestLookCatalogs.CreateMinimal());
            Assert.False(catalog.TryGet(999, out _));
        }

        [Fact]
        public void LoadFromJson_DuplicateId_Throws()
        {
            const string json = @"{
  ""items"": [
    { ""id"": 1, ""name"": ""Holzschwert"", ""itemType"": ""Weapon"", ""lookId"": 1, ""attackBonus"": 2, ""defenseBonus"": 0 },
    { ""id"": 1, ""name"": ""Eisenschwert"", ""itemType"": ""Weapon"", ""lookId"": 1, ""attackBonus"": 6, ""defenseBonus"": 0 }
  ]
}";
            var ex = Assert.Throws<ItemCatalogException>(() => ItemCatalog.LoadFromJson(json, TestLookCatalogs.CreateMinimal()));
            Assert.Contains("doppelte Id", ex.Message);
        }

        [Fact]
        public void LoadFromJson_MissingName_Throws()
        {
            const string json = @"{ ""items"": [ { ""id"": 1, ""itemType"": ""Weapon"", ""lookId"": 1 } ] }";
            var ex = Assert.Throws<ItemCatalogException>(() => ItemCatalog.LoadFromJson(json, TestLookCatalogs.CreateMinimal()));
            Assert.Contains("ohne Namen", ex.Message);
        }

        [Fact]
        public void LoadFromJson_InvalidItemType_Throws()
        {
            const string json = @"{ ""items"": [ { ""id"": 1, ""name"": ""Kaputt"", ""itemType"": ""Ring"" } ] }";
            var ex = Assert.Throws<ItemCatalogException>(() => ItemCatalog.LoadFromJson(json, TestLookCatalogs.CreateMinimal()));
            Assert.Contains("ungültigen itemType", ex.Message);
        }

        [Fact]
        public void LoadFromJson_UnknownLookId_Throws()
        {
            const string json = @"{ ""items"": [ { ""id"": 1, ""name"": ""Geisterschwert"", ""itemType"": ""Weapon"", ""lookId"": 999 } ] }";
            var ex = Assert.Throws<ItemCatalogException>(() => ItemCatalog.LoadFromJson(json, TestLookCatalogs.CreateMinimal()));
            Assert.Contains("unbekannte LookId", ex.Message);
        }

        [Fact]
        public void LoadFromJson_ConsumableWithoutHealAmount_Throws()
        {
            const string json = @"{ ""items"": [ { ""id"": 1, ""name"": ""Leertrank"", ""itemType"": ""Consumable"", ""healAmount"": 0 } ] }";
            var ex = Assert.Throws<ItemCatalogException>(() => ItemCatalog.LoadFromJson(json, TestLookCatalogs.CreateMinimal()));
            Assert.Contains("healAmount", ex.Message);
        }

        [Fact]
        public void LoadFromJson_EmptyItemList_Throws()
        {
            const string json = @"{ ""items"": [] }";
            Assert.Throws<ItemCatalogException>(() => ItemCatalog.LoadFromJson(json, TestLookCatalogs.CreateMinimal()));
        }
    }
}
