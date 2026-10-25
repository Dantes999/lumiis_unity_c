using System;
using System.IO;
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
        public void LoadFromJson_SameThresholdTwice_IsOneMixedWave()
        {
            const string json = @"{ ""metins"": [ { ""mobId"": 3, ""waves"": [
                { ""hpPercent"": 75, ""mobId"": 1, ""count"": 2 },
                { ""hpPercent"": 75, ""mobId"": 2, ""count"": 1 } ] } ] }";

            MetinCatalog catalog = MetinCatalog.LoadFromJson(json);

            Assert.True(catalog.TryGet(3, out MetinDefinition metin));
            Assert.Equal(2, metin.Waves.Count);
        }

        [Fact]
        public void LoadFromJson_InvalidPercent_Throws()
        {
            const string json = @"{ ""metins"": [ { ""mobId"": 3, ""waves"": [
                { ""hpPercent"": 100, ""mobId"": 1, ""count"": 1 } ] } ] }";

            Assert.Throws<MetinCatalogException>(() => MetinCatalog.LoadFromJson(json));
        }

        [Fact]
        public void ValidateAgainst_MissingWaveMob_Throws()
        {
            MetinCatalog catalog = MetinCatalog.LoadFromJson(ValidJson);
            MobCatalog mobs = MobCatalog.LoadFromJson(@"{
  ""mobs"": [
    { ""id"": 3, ""name"": ""Stein"", ""speedWire"": 1, ""wanderRadiusWire"": 1, ""wanderIntervalMinMs"": 1000, ""wanderIntervalMaxMs"": 2000, ""maxHp"": 100, ""attack"": 1, ""defense"": 0, ""yangMin"": 0, ""yangMax"": 1, ""meleeRangeWire"": 100, ""attackCooldownMs"": 500, ""aggressive"": false, ""aggroRangeWire"": 0, ""leashRangeWire"": 100, ""level"": 1, ""baseXp"": 1 }
  ]
}");

            Assert.Throws<MetinCatalogException>(() => catalog.ValidateAgainst(mobs));
        }

        [Fact]
        public void LoadFromFile_RealData_ValidatesAgainstMobs()
        {
            string root = ResolveRepoRoot();
            string data = Path.Combine(root, "data");
            MetinCatalog metins = MetinCatalog.LoadFromFile(Path.Combine(data, "metins.json"));
            MobCatalog mobs = MobCatalog.LoadFromFile(Path.Combine(data, "mobs.json"));

            metins.ValidateAgainst(mobs);
            Assert.True(metins.TryGet(3, out MetinDefinition stone));
            Assert.Equal(3, stone.Waves.Count);
        }

        private static string ResolveRepoRoot()
        {
            DirectoryInfo? dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "data", "metins.json"))) return dir.FullName;
                dir = dir.Parent;
            }
            throw new FileNotFoundException("Repo-Root mit data/metins.json nicht gefunden.");
        }

        [Fact]
        public void LoadFromJson_EmptyWaves_Throws()
        {
            const string json = @"{ ""metins"": [ { ""mobId"": 3, ""waves"": [] } ] }";
            Assert.Throws<MetinCatalogException>(() => MetinCatalog.LoadFromJson(json));
        }
    }
}
