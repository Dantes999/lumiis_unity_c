using Lumiis.Data.Mobs;
using Xunit;

namespace Lumiis.Tests.Data.Mobs
{
    public class MobCatalogTests
    {
        private const string ValidJson = @"{
  ""mobs"": [
    { ""id"": 1, ""name"": ""Wildhund"", ""speedWire"": 700, ""wanderRadiusWire"": 500, ""wanderIntervalMinMs"": 2000, ""wanderIntervalMaxMs"": 5000, ""maxHp"": 30, ""attack"": 5, ""defense"": 0, ""yangMin"": 5, ""yangMax"": 15, ""meleeRangeWire"": 130, ""attackCooldownMs"": 700, ""aggressive"": true, ""aggroRangeWire"": 400, ""leashRangeWire"": 700, ""level"": 3, ""baseXp"": 30 },
    { ""id"": 2, ""name"": ""Waldschrat"", ""speedWire"": 200, ""wanderRadiusWire"": 300, ""wanderIntervalMinMs"": 15000, ""wanderIntervalMaxMs"": 30000, ""maxHp"": 60, ""attack"": 8, ""defense"": 3, ""yangMin"": 10, ""yangMax"": 25, ""meleeRangeWire"": 150, ""attackCooldownMs"": 1000, ""aggressive"": false, ""aggroRangeWire"": 0, ""leashRangeWire"": 600, ""level"": 6, ""baseXp"": 60 }
  ]
}";

        [Fact]
        public void LoadFromJson_ValidData_ExposesMobs()
        {
            MobCatalog catalog = MobCatalog.LoadFromJson(ValidJson);

            Assert.True(catalog.TryGet(1, out MobDefinition wildhund));
            Assert.Equal("Wildhund", wildhund.Name);
            Assert.Equal(700, wildhund.SpeedWire);
            Assert.Equal(500, wildhund.WanderRadiusWire);
            Assert.Equal(2000, wildhund.WanderIntervalMinMs);
            Assert.Equal(5000, wildhund.WanderIntervalMaxMs);

            Assert.True(catalog.TryGet(2, out MobDefinition waldschrat));
            Assert.Equal("Waldschrat", waldschrat.Name);

            Assert.Equal(3, wildhund.Level);
            Assert.Equal(30, wildhund.BaseXp);
            Assert.Equal(6, waldschrat.Level);
            Assert.Equal(60, waldschrat.BaseXp);
        }

        [Fact]
        public void LoadFromJson_LevelZero_Throws()
        {
            const string json = @"{
  ""mobs"": [
    { ""id"": 1, ""name"": ""A"", ""speedWire"": 100, ""wanderRadiusWire"": 100, ""wanderIntervalMinMs"": 1000, ""wanderIntervalMaxMs"": 2000, ""maxHp"": 10, ""attack"": 1, ""defense"": 0, ""yangMin"": 1, ""yangMax"": 1, ""meleeRangeWire"": 100, ""attackCooldownMs"": 500, ""aggressive"": false, ""aggroRangeWire"": 0, ""leashRangeWire"": 300, ""level"": 0, ""baseXp"": 10 }
  ]
}";
            var ex = Assert.Throws<MobCatalogException>(() => MobCatalog.LoadFromJson(json));
            Assert.Contains("level <= 0", ex.Message);
        }

        [Fact]
        public void LoadFromJson_NegativeBaseXp_Throws()
        {
            const string json = @"{
  ""mobs"": [
    { ""id"": 1, ""name"": ""A"", ""speedWire"": 100, ""wanderRadiusWire"": 100, ""wanderIntervalMinMs"": 1000, ""wanderIntervalMaxMs"": 2000, ""maxHp"": 10, ""attack"": 1, ""defense"": 0, ""yangMin"": 1, ""yangMax"": 1, ""meleeRangeWire"": 100, ""attackCooldownMs"": 500, ""aggressive"": false, ""aggroRangeWire"": 0, ""leashRangeWire"": 300, ""level"": 1, ""baseXp"": -1 }
  ]
}";
            var ex = Assert.Throws<MobCatalogException>(() => MobCatalog.LoadFromJson(json));
            Assert.Contains("baseXp < 0", ex.Message);
        }

        [Fact]
        public void TryGet_UnknownId_ReturnsFalse()
        {
            MobCatalog catalog = MobCatalog.LoadFromJson(ValidJson);
            Assert.False(catalog.TryGet(999, out _));
        }

        [Fact]
        public void LoadFromJson_DuplicateId_Throws()
        {
            const string json = @"{
  ""mobs"": [
    { ""id"": 1, ""name"": ""A"", ""speedWire"": 100, ""wanderRadiusWire"": 100, ""wanderIntervalMinMs"": 1000, ""wanderIntervalMaxMs"": 2000, ""maxHp"": 10, ""attack"": 1, ""defense"": 0, ""yangMin"": 1, ""yangMax"": 1, ""meleeRangeWire"": 100, ""attackCooldownMs"": 500, ""aggressive"": false, ""aggroRangeWire"": 0, ""leashRangeWire"": 300, ""level"": 1, ""baseXp"": 10 },
    { ""id"": 1, ""name"": ""B"", ""speedWire"": 100, ""wanderRadiusWire"": 100, ""wanderIntervalMinMs"": 1000, ""wanderIntervalMaxMs"": 2000, ""maxHp"": 10, ""attack"": 1, ""defense"": 0, ""yangMin"": 1, ""yangMax"": 1, ""meleeRangeWire"": 100, ""attackCooldownMs"": 500, ""aggressive"": false, ""aggroRangeWire"": 0, ""leashRangeWire"": 300, ""level"": 1, ""baseXp"": 10 }
  ]
}";
            var ex = Assert.Throws<MobCatalogException>(() => MobCatalog.LoadFromJson(json));
            Assert.Contains("doppelte Id", ex.Message);
        }

        [Fact]
        public void LoadFromJson_InvalidWanderInterval_Throws()
        {
            const string json = @"{
  ""mobs"": [
    { ""id"": 1, ""name"": ""A"", ""speedWire"": 100, ""wanderRadiusWire"": 100, ""wanderIntervalMinMs"": 5000, ""wanderIntervalMaxMs"": 2000 }
  ]
}";
            var ex = Assert.Throws<MobCatalogException>(() => MobCatalog.LoadFromJson(json));
            Assert.Contains("Wander-Intervall", ex.Message);
        }

        [Fact]
        public void LoadFromJson_EmptyMobList_Throws()
        {
            const string json = @"{ ""mobs"": [] }";
            Assert.Throws<MobCatalogException>(() => MobCatalog.LoadFromJson(json));
        }

        [Fact]
        public void LoadFromJson_WithDrops_ExposesDrops()
        {
            const string json = @"{
  ""mobs"": [
    { ""id"": 1, ""name"": ""A"", ""speedWire"": 100, ""wanderRadiusWire"": 100, ""wanderIntervalMinMs"": 1000, ""wanderIntervalMaxMs"": 2000, ""maxHp"": 10, ""attack"": 1, ""defense"": 0, ""yangMin"": 1, ""yangMax"": 1, ""meleeRangeWire"": 100, ""attackCooldownMs"": 500, ""aggressive"": false, ""aggroRangeWire"": 0, ""leashRangeWire"": 300, ""level"": 1, ""baseXp"": 10,
      ""drops"": [ { ""itemId"": 8, ""chancePercent"": 30 }, { ""itemId"": 6, ""chancePercent"": 10 } ] }
  ]
}";
            MobCatalog catalog = MobCatalog.LoadFromJson(json);
            Assert.True(catalog.TryGet(1, out MobDefinition mob));
            Assert.Equal(2, mob.Drops.Count);
            Assert.Equal(8, mob.Drops[0].ItemId);
            Assert.Equal(30, mob.Drops[0].ChancePercent);
        }

        [Fact]
        public void LoadFromJson_NoDrops_ExposesEmptyDropsList()
        {
            MobCatalog catalog = MobCatalog.LoadFromJson(ValidJson);
            Assert.True(catalog.TryGet(1, out MobDefinition mob));
            Assert.Empty(mob.Drops);
        }

        [Fact]
        public void LoadFromJson_DropWithInvalidItemId_Throws()
        {
            const string json = @"{
  ""mobs"": [
    { ""id"": 1, ""name"": ""A"", ""speedWire"": 100, ""wanderRadiusWire"": 100, ""wanderIntervalMinMs"": 1000, ""wanderIntervalMaxMs"": 2000, ""maxHp"": 10, ""attack"": 1, ""defense"": 0, ""yangMin"": 1, ""yangMax"": 1, ""meleeRangeWire"": 100, ""attackCooldownMs"": 500, ""aggressive"": false, ""aggroRangeWire"": 0, ""leashRangeWire"": 300, ""level"": 1, ""baseXp"": 10,
      ""drops"": [ { ""itemId"": 0, ""chancePercent"": 30 } ] }
  ]
}";
            var ex = Assert.Throws<MobCatalogException>(() => MobCatalog.LoadFromJson(json));
            Assert.Contains("itemId <= 0", ex.Message);
        }

        [Fact]
        public void LoadFromJson_DropWithChancePercentOutOfRange_Throws()
        {
            const string json = @"{
  ""mobs"": [
    { ""id"": 1, ""name"": ""A"", ""speedWire"": 100, ""wanderRadiusWire"": 100, ""wanderIntervalMinMs"": 1000, ""wanderIntervalMaxMs"": 2000, ""maxHp"": 10, ""attack"": 1, ""defense"": 0, ""yangMin"": 1, ""yangMax"": 1, ""meleeRangeWire"": 100, ""attackCooldownMs"": 500, ""aggressive"": false, ""aggroRangeWire"": 0, ""leashRangeWire"": 300, ""level"": 1, ""baseXp"": 10,
      ""drops"": [ { ""itemId"": 8, ""chancePercent"": 101 } ] }
  ]
}";
            var ex = Assert.Throws<MobCatalogException>(() => MobCatalog.LoadFromJson(json));
            Assert.Contains("chancePercent", ex.Message);
        }
    }
}
