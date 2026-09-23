using Lumiis.Data.Mobs;
using Xunit;

namespace Lumiis.Tests.Data.Mobs
{
    public class MobCatalogTests
    {
        private const string ValidJson = @"{
  ""mobs"": [
    { ""id"": 1, ""name"": ""Wildhund"", ""speedWire"": 700, ""wanderRadiusWire"": 500, ""wanderIntervalMinMs"": 2000, ""wanderIntervalMaxMs"": 5000 },
    { ""id"": 2, ""name"": ""Waldschrat"", ""speedWire"": 200, ""wanderRadiusWire"": 300, ""wanderIntervalMinMs"": 15000, ""wanderIntervalMaxMs"": 30000 }
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
    { ""id"": 1, ""name"": ""A"", ""speedWire"": 100, ""wanderRadiusWire"": 100, ""wanderIntervalMinMs"": 1000, ""wanderIntervalMaxMs"": 2000 },
    { ""id"": 1, ""name"": ""B"", ""speedWire"": 100, ""wanderRadiusWire"": 100, ""wanderIntervalMinMs"": 1000, ""wanderIntervalMaxMs"": 2000 }
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
    }
}
