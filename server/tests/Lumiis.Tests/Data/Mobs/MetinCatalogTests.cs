using Lumiis.Data.Mobs;
using Xunit;

namespace Lumiis.Tests.Data.Mobs
{
    public class MetinCatalogTests
    {
        private const string ValidJson = @"{
  ""metins"": [
    { ""mobId"": 3, ""waves"": [
      { ""hpPercent"": 75, ""mobId"": 1, ""count"": 2 },
      { ""hpPercent"": 25, ""mobId"": 2, ""count"": 1 }
    ] }
  ]
}";

        [Fact]
        public void LoadFromJson_ValidData_ExposesWaves()
        {
            MetinCatalog catalog = MetinCatalog.LoadFromJson(ValidJson);

            Assert.True(catalog.TryGet(3, out MetinDefinition metin));
            Assert.Equal(2, metin.Waves.Count);
            Assert.Equal(75, metin.Waves[0].HpPercent);
            Assert.Equal(2, metin.Waves[0].Count);
            Assert.Equal(2, metin.Waves[1].MobId);
        }

        [Fact]
        public void LoadFromJson_NonDescendingThresholds_Throws()
        {
            const string json = @"{ ""metins"": [ { ""mobId"": 3, ""waves"": [
                { ""hpPercent"": 50, ""mobId"": 1, ""count"": 1 },
                { ""hpPercent"": 75, ""mobId"": 1, ""count"": 1 } ] } ] }";

            Assert.Throws<MetinCatalogException>(() => MetinCatalog.LoadFromJson(json));
        }

        [Fact]
        public void LoadFromJson_InvalidPercent_Throws()
        {
            const string json = @"{ ""metins"": [ { ""mobId"": 3, ""waves"": [
                { ""hpPercent"": 100, ""mobId"": 1, ""count"": 1 } ] } ] }";

            Assert.Throws<MetinCatalogException>(() => MetinCatalog.LoadFromJson(json));
        }

        [Fact]
        public void LoadFromJson_EmptyWaves_Throws()
        {
            const string json = @"{ ""metins"": [ { ""mobId"": 3, ""waves"": [] } ] }";
            Assert.Throws<MetinCatalogException>(() => MetinCatalog.LoadFromJson(json));
        }
    }
}
