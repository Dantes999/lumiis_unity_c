using Lumiis.Data.Progression;
using Xunit;

namespace Lumiis.Tests.Data.Progression
{
    public class ProgressionCatalogTests
    {
        private const string ValidJson = @"{
  ""maxLevel"": 5,
  ""xpToNext"": [ 10, 20, 30, 40 ],
  ""levelDiffPercent"": [
    { ""maxDiff"": -1, ""percent"": 120 },
    { ""maxDiff"": 0, ""percent"": 100 },
    { ""maxDiff"": 1, ""percent"": 80 },
    { ""maxDiff"": 999999, ""percent"": 10 }
  ]
}";

        [Fact]
        public void LoadFromJson_ValidData_ExposesMaxLevelAndXpToNext()
        {
            ProgressionCatalog catalog = ProgressionCatalog.LoadFromJson(ValidJson);

            Assert.Equal(5, catalog.MaxLevel);
            Assert.Equal(10, catalog.XpToNextLevel(1));
            Assert.Equal(20, catalog.XpToNextLevel(2));
            Assert.Equal(30, catalog.XpToNextLevel(3));
            Assert.Equal(40, catalog.XpToNextLevel(4));
            Assert.Equal(0, catalog.XpToNextLevel(5)); // MaxLevel: kein weiterer Levelaufstieg.
        }

        [Fact]
        public void XpToNextLevel_OutOfRange_Throws()
        {
            ProgressionCatalog catalog = ProgressionCatalog.LoadFromJson(ValidJson);
            Assert.Throws<System.ArgumentOutOfRangeException>(() => catalog.XpToNextLevel(0));
            Assert.Throws<System.ArgumentOutOfRangeException>(() => catalog.XpToNextLevel(6));
        }

        [Theory]
        [InlineData(-100, 120)]
        [InlineData(-1, 120)]
        [InlineData(0, 100)]
        [InlineData(1, 80)]
        [InlineData(2, 10)]
        [InlineData(100, 10)]
        public void PercentForLevelDiff_LooksUpCorrectTier(int diff, int expectedPercent)
        {
            ProgressionCatalog catalog = ProgressionCatalog.LoadFromJson(ValidJson);
            Assert.Equal(expectedPercent, catalog.PercentForLevelDiff(diff));
        }

        [Fact]
        public void LoadFromJson_MaxLevelBelow2_Throws()
        {
            const string json = @"{ ""maxLevel"": 1, ""xpToNext"": [], ""levelDiffPercent"": [ { ""maxDiff"": 0, ""percent"": 100 } ] }";
            var ex = Assert.Throws<ProgressionCatalogException>(() => ProgressionCatalog.LoadFromJson(json));
            Assert.Contains("maxLevel", ex.Message);
        }

        [Fact]
        public void LoadFromJson_XpToNextWrongLength_Throws()
        {
            const string json = @"{ ""maxLevel"": 5, ""xpToNext"": [ 10, 20 ], ""levelDiffPercent"": [ { ""maxDiff"": 0, ""percent"": 100 } ] }";
            var ex = Assert.Throws<ProgressionCatalogException>(() => ProgressionCatalog.LoadFromJson(json));
            Assert.Contains("xpToNext", ex.Message);
        }

        [Fact]
        public void LoadFromJson_XpToNextEntryZeroOrNegative_Throws()
        {
            const string json = @"{ ""maxLevel"": 3, ""xpToNext"": [ 10, 0 ], ""levelDiffPercent"": [ { ""maxDiff"": 0, ""percent"": 100 } ] }";
            var ex = Assert.Throws<ProgressionCatalogException>(() => ProgressionCatalog.LoadFromJson(json));
            Assert.Contains("xpToNext", ex.Message);
        }

        [Fact]
        public void LoadFromJson_EmptyLevelDiffPercent_Throws()
        {
            const string json = @"{ ""maxLevel"": 2, ""xpToNext"": [ 10 ], ""levelDiffPercent"": [] }";
            var ex = Assert.Throws<ProgressionCatalogException>(() => ProgressionCatalog.LoadFromJson(json));
            Assert.Contains("levelDiffPercent", ex.Message);
        }

        [Fact]
        public void LoadFromJson_LevelDiffPercentNotStrictlyAscending_Throws()
        {
            const string json = @"{ ""maxLevel"": 2, ""xpToNext"": [ 10 ], ""levelDiffPercent"": [
                { ""maxDiff"": 0, ""percent"": 100 }, { ""maxDiff"": 0, ""percent"": 90 } ] }";
            var ex = Assert.Throws<ProgressionCatalogException>(() => ProgressionCatalog.LoadFromJson(json));
            Assert.Contains("aufsteigend", ex.Message);
        }

        [Fact]
        public void LoadFromJson_LevelDiffPercentDescending_Throws()
        {
            const string json = @"{ ""maxLevel"": 2, ""xpToNext"": [ 10 ], ""levelDiffPercent"": [
                { ""maxDiff"": 5, ""percent"": 100 }, { ""maxDiff"": 1, ""percent"": 90 } ] }";
            var ex = Assert.Throws<ProgressionCatalogException>(() => ProgressionCatalog.LoadFromJson(json));
            Assert.Contains("aufsteigend", ex.Message);
        }

        [Fact]
        public void LoadFromJson_NegativePercent_Throws()
        {
            const string json = @"{ ""maxLevel"": 2, ""xpToNext"": [ 10 ], ""levelDiffPercent"": [ { ""maxDiff"": 0, ""percent"": -1 } ] }";
            var ex = Assert.Throws<ProgressionCatalogException>(() => ProgressionCatalog.LoadFromJson(json));
            Assert.Contains("percent", ex.Message);
        }

        [Fact]
        public void LoadFromJson_RealDataFile_LoadsSuccessfully()
        {
            // Regressionstest gegen die echte data/progression.json (24 XpToNext-Eintraege fuer MaxLevel 25,
            // 15 Levelunterschieds-Zeilen) - findet Tippfehler in der echten Datei, ohne den Dateiinhalt hier zu duplizieren.
            string repoRoot = FindRepoRoot();
            string path = System.IO.Path.Combine(repoRoot, "data", "progression.json");
            ProgressionCatalog catalog = ProgressionCatalog.LoadFromFile(path);

            Assert.Equal(25, catalog.MaxLevel);
            Assert.Equal(90, catalog.XpToNextLevel(1)); // (8+1)*10*1
            Assert.Equal(7680, catalog.XpToNextLevel(24)); // (8+24)*10*24
            Assert.Equal(0, catalog.XpToNextLevel(25));
            Assert.Equal(100, catalog.PercentForLevelDiff(0));
            Assert.Equal(10, catalog.PercentForLevelDiff(999));
        }

        private static string FindRepoRoot()
        {
            System.IO.DirectoryInfo? dir = new System.IO.DirectoryInfo(System.AppContext.BaseDirectory);
            while (dir != null)
            {
                string candidate = System.IO.Path.Combine(dir.FullName, "data", "progression.json");
                if (System.IO.File.Exists(candidate)) return dir.FullName;
                dir = dir.Parent;
            }
            throw new System.IO.FileNotFoundException("Repo-Root mit data/progression.json nicht gefunden.");
        }
    }
}
