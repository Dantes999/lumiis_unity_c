using Lumiis.Data.Items;
using Lumiis.Data.Npcs;
using Lumiis.Shared.Net;
using Lumiis.Tests;
using Xunit;

namespace Lumiis.Tests.Data.Npcs
{
    public class NpcCatalogTests
    {
        // id1: Weapon, buyPrice 200 (kaufbar). id2: Weapon, buyPrice 0 (nicht kaufbar - für den
        // BuyPrice-0-im-Sortiment-Test).
        private const string ValidItemsJson = @"{
  ""items"": [
    { ""id"": 1, ""name"": ""Testschwert"", ""itemType"": ""Weapon"", ""lookId"": 1, ""attackBonus"": 5, ""defenseBonus"": 0, ""buyPrice"": 200, ""sellPrice"": 80 },
    { ""id"": 2, ""name"": ""Startwaffe"", ""itemType"": ""Weapon"", ""lookId"": 1, ""attackBonus"": 2, ""defenseBonus"": 0, ""buyPrice"": 0, ""sellPrice"": 5 }
  ]
}";

        private const string ValidNpcsJson = @"{
  ""npcs"": [
    {
      ""id"": 1,
      ""name"": ""Dorfhaendler"",
      ""appearance"": { ""bodyId"": 1, ""hairStyleId"": 1, ""hairColorId"": 1, ""armorId"": 1, ""helmetId"": 0, ""weaponId"": 0, ""shieldId"": 0 },
      ""shopItemIds"": [1]
    }
  ]
}";

        private static ItemCatalog CreateItemCatalog(string json = ValidItemsJson) =>
            ItemCatalog.LoadFromJson(json, TestLookCatalogs.CreateMinimal());

        [Fact]
        public void LoadFromJson_ValidData_ExposesNpcs()
        {
            NpcCatalog catalog = NpcCatalog.LoadFromJson(ValidNpcsJson, CreateItemCatalog(), TestLookCatalogs.CreateMinimal());

            Assert.True(catalog.TryGet(1, out NpcDefinition npc));
            Assert.Equal("Dorfhaendler", npc.Name);
            Assert.Equal(1, npc.Appearance.BodyId);
            Assert.Equal(1, npc.Appearance.ArmorId);
            Assert.Equal(0, npc.Appearance.HelmetId);
            Assert.Single(npc.ShopItemIds);
            Assert.Equal(1, npc.ShopItemIds[0]);
        }

        [Fact]
        public void TryGet_UnknownId_ReturnsFalse()
        {
            NpcCatalog catalog = NpcCatalog.LoadFromJson(ValidNpcsJson, CreateItemCatalog(), TestLookCatalogs.CreateMinimal());
            Assert.False(catalog.TryGet(999, out _));
        }

        [Fact]
        public void LoadFromJson_DuplicateId_Throws()
        {
            const string json = @"{
  ""npcs"": [
    { ""id"": 1, ""name"": ""A"", ""appearance"": { ""bodyId"": 1, ""hairStyleId"": 1, ""hairColorId"": 1 }, ""shopItemIds"": [1] },
    { ""id"": 1, ""name"": ""B"", ""appearance"": { ""bodyId"": 1, ""hairStyleId"": 1, ""hairColorId"": 1 }, ""shopItemIds"": [1] }
  ]
}";
            var ex = Assert.Throws<NpcCatalogException>(() => NpcCatalog.LoadFromJson(json, CreateItemCatalog(), TestLookCatalogs.CreateMinimal()));
            Assert.Contains("doppelte Id", ex.Message);
        }

        [Fact]
        public void LoadFromJson_MissingName_Throws()
        {
            const string json = @"{ ""npcs"": [ { ""id"": 1, ""appearance"": { ""bodyId"": 1, ""hairStyleId"": 1, ""hairColorId"": 1 }, ""shopItemIds"": [1] } ] }";
            var ex = Assert.Throws<NpcCatalogException>(() => NpcCatalog.LoadFromJson(json, CreateItemCatalog(), TestLookCatalogs.CreateMinimal()));
            Assert.Contains("ohne Namen", ex.Message);
        }

        [Fact]
        public void LoadFromJson_UnknownShopItemId_Throws()
        {
            const string json = @"{
  ""npcs"": [
    { ""id"": 1, ""name"": ""Haendler"", ""appearance"": { ""bodyId"": 1, ""hairStyleId"": 1, ""hairColorId"": 1 }, ""shopItemIds"": [999] }
  ]
}";
            var ex = Assert.Throws<NpcCatalogException>(() => NpcCatalog.LoadFromJson(json, CreateItemCatalog(), TestLookCatalogs.CreateMinimal()));
            Assert.Contains("unbekannte ItemId", ex.Message);
        }

        [Fact]
        public void LoadFromJson_ShopItemWithoutBuyPrice_Throws()
        {
            // Item 2 hat buyPrice 0 (siehe ValidItemsJson) - darf nicht im Sortiment stehen.
            const string json = @"{
  ""npcs"": [
    { ""id"": 1, ""name"": ""Haendler"", ""appearance"": { ""bodyId"": 1, ""hairStyleId"": 1, ""hairColorId"": 1 }, ""shopItemIds"": [2] }
  ]
}";
            var ex = Assert.Throws<NpcCatalogException>(() => NpcCatalog.LoadFromJson(json, CreateItemCatalog(), TestLookCatalogs.CreateMinimal()));
            Assert.Contains("buyPrice <= 0", ex.Message);
        }

        [Fact]
        public void LoadFromJson_EmptyShopItemIds_ForMerchant_Throws()
        {
            const string json = @"{
  ""npcs"": [
    { ""id"": 1, ""name"": ""Haendler"", ""service"": ""Merchant"", ""appearance"": { ""bodyId"": 1, ""hairStyleId"": 1, ""hairColorId"": 1 }, ""shopItemIds"": [] }
  ]
}";
            var ex = Assert.Throws<NpcCatalogException>(() => NpcCatalog.LoadFromJson(json, CreateItemCatalog(), TestLookCatalogs.CreateMinimal()));
            Assert.Contains("leeres Sortiment", ex.Message);
        }

        [Fact]
        public void LoadFromJson_EmptyShopItemIds_WithoutService_IsAllowed()
        {
            const string json = @"{
  ""npcs"": [
    { ""id"": 1, ""name"": ""Waechter"", ""appearance"": { ""bodyId"": 1, ""hairStyleId"": 1, ""hairColorId"": 1 }, ""shopItemIds"": [] }
  ]
}";
            NpcCatalog catalog = NpcCatalog.LoadFromJson(json, CreateItemCatalog(), TestLookCatalogs.CreateMinimal());
            Assert.True(catalog.TryGet(1, out NpcDefinition waechter));
            Assert.Empty(waechter.ShopItemIds);
            Assert.Equal(NpcService.None, waechter.Service);
        }

        [Fact]
        public void LoadFromJson_UnknownBodyId_Throws()
        {
            const string json = @"{
  ""npcs"": [
    { ""id"": 1, ""name"": ""Haendler"", ""appearance"": { ""bodyId"": 999, ""hairStyleId"": 1, ""hairColorId"": 1 }, ""shopItemIds"": [1] }
  ]
}";
            var ex = Assert.Throws<NpcCatalogException>(() => NpcCatalog.LoadFromJson(json, CreateItemCatalog(), TestLookCatalogs.CreateMinimal()));
            Assert.Contains("unbekannte BodyId", ex.Message);
        }

        [Fact]
        public void LoadFromJson_UnknownArmorId_Throws()
        {
            const string json = @"{
  ""npcs"": [
    { ""id"": 1, ""name"": ""Haendler"", ""appearance"": { ""bodyId"": 1, ""hairStyleId"": 1, ""hairColorId"": 1, ""armorId"": 999 }, ""shopItemIds"": [1] }
  ]
}";
            var ex = Assert.Throws<NpcCatalogException>(() => NpcCatalog.LoadFromJson(json, CreateItemCatalog(), TestLookCatalogs.CreateMinimal()));
            Assert.Contains("unbekannte ArmorId", ex.Message);
        }

        [Fact]
        public void LoadFromJson_InvalidHairColorId_Throws()
        {
            const string json = @"{
  ""npcs"": [
    { ""id"": 1, ""name"": ""Haendler"", ""appearance"": { ""bodyId"": 1, ""hairStyleId"": 1, ""hairColorId"": 999 }, ""shopItemIds"": [1] }
  ]
}";
            var ex = Assert.Throws<NpcCatalogException>(() => NpcCatalog.LoadFromJson(json, CreateItemCatalog(), TestLookCatalogs.CreateMinimal()));
            Assert.Contains("HairColorId", ex.Message);
        }

        [Fact]
        public void LoadFromJson_EmptyNpcList_Throws()
        {
            const string json = @"{ ""npcs"": [] }";
            Assert.Throws<NpcCatalogException>(() => NpcCatalog.LoadFromJson(json, CreateItemCatalog(), TestLookCatalogs.CreateMinimal()));
        }
    }
}
