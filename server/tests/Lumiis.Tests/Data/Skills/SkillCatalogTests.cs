using System;
using System.Linq;
using Lumiis.Data.Skills;
using Lumiis.Shared.Net;
using Xunit;

namespace Lumiis.Tests.Data.Skills
{
    public class SkillCatalogTests
    {
        /// <summary>17 Stufen-Einträge, EffectValue-Feldname je nach type - Level i hat SpCost=10+i,
        /// CooldownMs=5000-i*100, EffectValue=100+i*10 (aufsteigend, damit Stufe 1 vs. 17 unterscheidbar sind).</summary>
        private static string Levels(string effectField)
        {
            return string.Join(",", Enumerable.Range(1, 17).Select(i =>
                $@"{{ ""spCost"": {10 + i}, ""cooldownMs"": {5000 - i * 100}, ""{effectField}"": {100 + i * 10} }}"));
        }

        private static string SingleSkillJson(int id = 1, string name = "Testskill", string type = "SingleStrike",
            int radiusWire = 0, int durationMs = 0, string? levelsJson = null)
        {
            string effectField = type switch
            {
                "AreaAttack" => "damageMultiplierPercent",
                "AttackBuff" => "attackBonusPercent",
                "AttackSpeedBuff" => "cooldownReductionPercent",
                _ => "damageMultiplierPercent"
            };
            string levels = levelsJson ?? Levels(effectField);
            return $@"{{ ""skills"": [ {{ ""id"": {id}, ""name"": ""{name}"", ""type"": ""{type}"",
                ""radiusWire"": {radiusWire}, ""durationMs"": {durationMs}, ""levels"": [ {levels} ] }} ] }}";
        }

        [Fact]
        public void LoadFromJson_ValidSingleStrike_ExposesLevelData()
        {
            SkillCatalog catalog = SkillCatalog.LoadFromJson(SingleSkillJson());

            Assert.True(catalog.TryGet(1, out SkillDefinition def));
            Assert.Equal("Testskill", def.Name);
            Assert.Equal(SkillType.SingleStrike, def.Type);
            Assert.Equal(17, def.Levels.Count);

            SkillLevelDefinition level1 = def.LevelData(1);
            Assert.Equal(11, level1.SpCost);
            Assert.Equal(4900, level1.CooldownMs);
            Assert.Equal(110, level1.EffectValue);

            SkillLevelDefinition level17 = def.LevelData(17);
            Assert.Equal(27, level17.SpCost);
            Assert.Equal(3300, level17.CooldownMs);
            Assert.Equal(270, level17.EffectValue);
        }

        [Fact]
        public void LoadFromJson_UnknownSkillId_TryGetReturnsFalse()
        {
            SkillCatalog catalog = SkillCatalog.LoadFromJson(SingleSkillJson());
            Assert.False(catalog.TryGet(999, out _));
        }

        [Fact]
        public void LevelData_OutOfRange_Throws()
        {
            SkillCatalog catalog = SkillCatalog.LoadFromJson(SingleSkillJson());
            catalog.TryGet(1, out SkillDefinition def);

            Assert.Throws<ArgumentOutOfRangeException>(() => def.LevelData(0));
            Assert.Throws<ArgumentOutOfRangeException>(() => def.LevelData(18));
        }

        [Fact]
        public void LoadFromJson_DuplicateId_Throws()
        {
            string json = @"{ ""skills"": [
                { ""id"": 1, ""name"": ""A"", ""type"": ""AttackBuff"", ""durationMs"": 1000, ""levels"": [" + Levels("attackBonusPercent") + @"] },
                { ""id"": 1, ""name"": ""B"", ""type"": ""AttackBuff"", ""durationMs"": 1000, ""levels"": [" + Levels("attackBonusPercent") + @"] }
            ] }";
            var ex = Assert.Throws<SkillCatalogException>(() => SkillCatalog.LoadFromJson(json));
            Assert.Contains("doppelte Id", ex.Message);
        }

        [Fact]
        public void LoadFromJson_MissingName_Throws()
        {
            var ex = Assert.Throws<SkillCatalogException>(() => SkillCatalog.LoadFromJson(SingleSkillJson(name: "")));
            Assert.Contains("ohne Namen", ex.Message);
        }

        [Fact]
        public void LoadFromJson_UnknownType_Throws()
        {
            var ex = Assert.Throws<SkillCatalogException>(() => SkillCatalog.LoadFromJson(SingleSkillJson(type: "Teleport")));
            Assert.Contains("type", ex.Message);
        }

        [Fact]
        public void LoadFromJson_AreaAttackWithoutRadius_Throws()
        {
            var ex = Assert.Throws<SkillCatalogException>(() => SkillCatalog.LoadFromJson(SingleSkillJson(type: "AreaAttack", radiusWire: 0)));
            Assert.Contains("radiusWire", ex.Message);
        }

        [Theory]
        [InlineData("AttackBuff")]
        [InlineData("AttackSpeedBuff")]
        public void LoadFromJson_BuffWithoutDuration_Throws(string type)
        {
            var ex = Assert.Throws<SkillCatalogException>(() => SkillCatalog.LoadFromJson(SingleSkillJson(type: type, durationMs: 0)));
            Assert.Contains("durationMs", ex.Message);
        }

        [Fact]
        public void LoadFromJson_WrongLevelCount_Throws()
        {
            string json = SingleSkillJson(levelsJson: @"{ ""spCost"": 1, ""cooldownMs"": 1, ""damageMultiplierPercent"": 1 }");
            var ex = Assert.Throws<SkillCatalogException>(() => SkillCatalog.LoadFromJson(json));
            Assert.Contains("17 Stufen-Einträge", ex.Message);
        }

        /// <summary>17 gültige Stufen-Einträge, Stufe 1 (Index 0) durch badEntryJson ersetzt - testet je einen
        /// Validierungsfehler, ohne die restlichen 16 Einträge von Hand aufschreiben zu müssen.</summary>
        private static string LevelsWithFirstEntryReplaced(string effectField, string badEntryJson)
        {
            string[] entries = Enumerable.Range(1, 17).Select(i =>
                $@"{{ ""spCost"": {10 + i}, ""cooldownMs"": {5000 - i * 100}, ""{effectField}"": {100 + i * 10} }}").ToArray();
            entries[0] = badEntryJson;
            return string.Join(",", entries);
        }

        [Fact]
        public void LoadFromJson_SpCostZero_Throws()
        {
            string badLevels = LevelsWithFirstEntryReplaced("damageMultiplierPercent",
                @"{ ""spCost"": 0, ""cooldownMs"": 100, ""damageMultiplierPercent"": 100 }");
            var ex = Assert.Throws<SkillCatalogException>(() => SkillCatalog.LoadFromJson(SingleSkillJson(levelsJson: badLevels)));
            Assert.Contains("spCost", ex.Message);
        }

        [Fact]
        public void LoadFromJson_CooldownMsZero_Throws()
        {
            string badLevels = LevelsWithFirstEntryReplaced("damageMultiplierPercent",
                @"{ ""spCost"": 10, ""cooldownMs"": 0, ""damageMultiplierPercent"": 100 }");
            var ex = Assert.Throws<SkillCatalogException>(() => SkillCatalog.LoadFromJson(SingleSkillJson(levelsJson: badLevels)));
            Assert.Contains("cooldownMs", ex.Message);
        }

        [Fact]
        public void LoadFromJson_EffectValueZero_Throws()
        {
            string badLevels = LevelsWithFirstEntryReplaced("damageMultiplierPercent",
                @"{ ""spCost"": 10, ""cooldownMs"": 100, ""damageMultiplierPercent"": 0 }");
            var ex = Assert.Throws<SkillCatalogException>(() => SkillCatalog.LoadFromJson(SingleSkillJson(levelsJson: badLevels)));
            Assert.Contains("Effektwert", ex.Message);
        }

        [Fact]
        public void LoadFromJson_NoSkills_Throws()
        {
            var ex = Assert.Throws<SkillCatalogException>(() => SkillCatalog.LoadFromJson(@"{ ""skills"": [] }"));
            Assert.Contains("mindestens ein Skill", ex.Message);
        }

        [Fact]
        public void LoadFromJson_RealDataFile_LoadsSuccessfully()
        {
            // Regressionstest gegen die echte data/skills.json (M6b: Wuchtschlag/Kreisschlag/Kampfrausch/Hast,
            // je 17 Stufen) - findet Tippfehler in der echten Datei, ohne den Inhalt hier zu duplizieren.
            // Insbesondere Hast Stufe 17 muss EXAKT 20 (CooldownReductionPercent) sein, wie vom User vorgegeben
            // (siehe docs/tasks/M6b-skills.md Abschnitt 1).
            string repoRoot = FindRepoRoot();
            string path = System.IO.Path.Combine(repoRoot, "data", "skills.json");
            SkillCatalog catalog = SkillCatalog.LoadFromFile(path);

            Assert.True(catalog.TryGet(1, out SkillDefinition wuchtschlag));
            Assert.Equal("Wuchtschlag", wuchtschlag.Name);
            Assert.Equal(SkillType.SingleStrike, wuchtschlag.Type);

            Assert.True(catalog.TryGet(2, out SkillDefinition kreisschlag));
            Assert.Equal(SkillType.AreaAttack, kreisschlag.Type);
            Assert.True(kreisschlag.RadiusWire > 0);

            Assert.True(catalog.TryGet(3, out SkillDefinition kampfrausch));
            Assert.Equal(SkillType.AttackBuff, kampfrausch.Type);
            Assert.True(kampfrausch.DurationMs > 0);

            Assert.True(catalog.TryGet(4, out SkillDefinition hast));
            Assert.Equal(SkillType.AttackSpeedBuff, hast.Type);
            Assert.Equal(20, hast.LevelData(17).EffectValue);
        }

        private static string FindRepoRoot()
        {
            System.IO.DirectoryInfo? dir = new System.IO.DirectoryInfo(System.AppContext.BaseDirectory);
            while (dir != null)
            {
                string candidate = System.IO.Path.Combine(dir.FullName, "data", "skills.json");
                if (System.IO.File.Exists(candidate)) return dir.FullName;
                dir = dir.Parent;
            }
            throw new System.IO.FileNotFoundException("Repo-Root mit data/skills.json nicht gefunden.");
        }
    }
}
