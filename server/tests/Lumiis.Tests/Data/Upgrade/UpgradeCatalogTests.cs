using System;
using System.IO;
using Lumiis.Data.Items;
using Lumiis.Data.Upgrade;
using Xunit;

namespace Lumiis.Tests.Data.Upgrade
{
    public class UpgradeCatalogTests
    {
        private const string ItemsJson = @"{
  ""items"": [
    { ""id"": 1, ""name"": ""Schwert"", ""itemType"": ""Weapon"", ""lookId"": 1, ""attackBonus"": 5, ""defenseBonus"": 0, ""upgradeBonusPerLevel"": 2 },
    { ""id"": 9, ""name"": ""Schleifstein"", ""itemType"": ""UpgradeMaterial"" },
    { ""id"": 10, ""name"": ""Bewahrungsrolle"", ""itemType"": ""ProtectionScroll"" }
  ]
}";

        private const string ValidJson = @"{
  ""maxUpgradeLevel"": 9,
  ""successChancePercent"": [ 90, 80, 70, 60, 50, 40, 30, 20, 10 ],
  ""whetstoneItemId"": 9,
  ""protectionScrollItemId"": 10
}";

        private static UpgradeCatalog Load(string json)
        {
            ItemCatalog items = ItemCatalog.LoadFromJson(ItemsJson, TestLookCatalogs.CreateMinimal());
            return UpgradeCatalog.LoadFromJson(json, items);
        }

        [Fact]
        public void LoadFromJson_ValidData_ExposesChancesAndItemIds()
        {
            UpgradeCatalog catalog = Load(ValidJson);

            Assert.Equal(9, catalog.MaxUpgradeLevel);
            Assert.Equal(9, catalog.WhetstoneItemId);
            Assert.Equal(10, catalog.ProtectionScrollItemId);
            Assert.Equal(90, catalog.GetSuccessChancePercent(0));
            Assert.Equal(50, catalog.GetSuccessChancePercent(4));
            Assert.Equal(10, catalog.GetSuccessChancePercent(8));
        }

        [Theory]
        [InlineData(-1)]
        [InlineData(9)]
        public void GetSuccessChancePercent_OutOfRange_Throws(int currentLevel)
        {
            UpgradeCatalog catalog = Load(ValidJson);
            Assert.Throws<ArgumentOutOfRangeException>(() => catalog.GetSuccessChancePercent(currentLevel));
        }

        [Fact]
        public void LoadFromJson_WrongMaxUpgradeLevel_Throws()
        {
            const string json = @"{ ""maxUpgradeLevel"": 10, ""successChancePercent"": [ 90, 80, 70, 60, 50, 40, 30, 20, 10, 5 ], ""whetstoneItemId"": 9, ""protectionScrollItemId"": 10 }";
            var ex = Assert.Throws<UpgradeCatalogException>(() => Load(json));
            Assert.Contains("maxUpgradeLevel", ex.Message);
        }

        [Fact]
        public void LoadFromJson_WrongChanceTableLength_Throws()
        {
            const string json = @"{ ""maxUpgradeLevel"": 9, ""successChancePercent"": [ 90, 80 ], ""whetstoneItemId"": 9, ""protectionScrollItemId"": 10 }";
            var ex = Assert.Throws<UpgradeCatalogException>(() => Load(json));
            Assert.Contains("successChancePercent", ex.Message);
        }

        [Fact]
        public void LoadFromJson_ChanceOutOfRange_Throws()
        {
            const string json = @"{ ""maxUpgradeLevel"": 9, ""successChancePercent"": [ 90, 80, 70, 60, 50, 40, 30, 20, 101 ], ""whetstoneItemId"": 9, ""protectionScrollItemId"": 10 }";
            var ex = Assert.Throws<UpgradeCatalogException>(() => Load(json));
            Assert.Contains("successChancePercent", ex.Message);
        }

        [Fact]
        public void LoadFromJson_UnknownWhetstone_Throws()
        {
            const string json = @"{ ""maxUpgradeLevel"": 9, ""successChancePercent"": [ 90, 80, 70, 60, 50, 40, 30, 20, 10 ], ""whetstoneItemId"": 999, ""protectionScrollItemId"": 10 }";
            var ex = Assert.Throws<UpgradeCatalogException>(() => Load(json));
            Assert.Contains("whetstoneItemId", ex.Message);
        }

        [Fact]
        public void LoadFromJson_WrongItemType_Throws()
        {
            const string json = @"{ ""maxUpgradeLevel"": 9, ""successChancePercent"": [ 90, 80, 70, 60, 50, 40, 30, 20, 10 ], ""whetstoneItemId"": 1, ""protectionScrollItemId"": 10 }";
            var ex = Assert.Throws<UpgradeCatalogException>(() => Load(json));
            Assert.Contains("UpgradeMaterial", ex.Message);
        }

        [Fact]
        public void LoadFromJson_SameItemForBothIds_Throws()
        {
            const string json = @"{ ""maxUpgradeLevel"": 9, ""successChancePercent"": [ 90, 80, 70, 60, 50, 40, 30, 20, 10 ], ""whetstoneItemId"": 9, ""protectionScrollItemId"": 9 }";
            var ex = Assert.Throws<UpgradeCatalogException>(() => Load(json));
            Assert.Contains("verschieden", ex.Message);
        }

        [Fact]
        public void LoadFromJson_InvalidJson_Throws()
        {
            Assert.Throws<UpgradeCatalogException>(() => Load("{ kaputt"));
        }

        [Fact]
        public void LoadFromFile_RealDataFile_LoadsSuccessfully()
        {
            string repoRoot = FindRepoRoot();
            string itemsPath = Path.Combine(repoRoot, "data", "items.json");
            string upgradePath = Path.Combine(repoRoot, "data", "upgrade.json");

            ItemCatalog items = ItemCatalog.LoadFromFile(itemsPath, Lumiis.Data.Looks.LookCatalog.LoadFromFile(
                Path.Combine(repoRoot, "data", "looks.json")));
            UpgradeCatalog catalog = UpgradeCatalog.LoadFromFile(upgradePath, items);

            Assert.Equal(9, catalog.MaxUpgradeLevel);
            Assert.Equal(90, catalog.GetSuccessChancePercent(0));
            Assert.Equal(10, catalog.GetSuccessChancePercent(8));
            Assert.True(items.TryGet(catalog.WhetstoneItemId, out ItemDefinition whetstone));
            Assert.Equal(ItemType.UpgradeMaterial, whetstone.ItemType);
            Assert.True(items.TryGet(catalog.ProtectionScrollItemId, out ItemDefinition scroll));
            Assert.Equal(ItemType.ProtectionScroll, scroll.ItemType);
        }

        private static string FindRepoRoot()
        {
            DirectoryInfo? dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                string candidate = Path.Combine(dir.FullName, "data", "upgrade.json");
                if (File.Exists(candidate)) return dir.FullName;
                dir = dir.Parent;
            }
            throw new FileNotFoundException("Repo-Root mit data/upgrade.json nicht gefunden.");
        }
    }
}
