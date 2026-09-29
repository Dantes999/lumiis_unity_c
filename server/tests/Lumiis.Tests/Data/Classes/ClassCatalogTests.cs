using Lumiis.Data.Classes;
using Xunit;

namespace Lumiis.Tests.Data.Classes
{
    public class ClassCatalogTests
    {
        private const string ValidJson = @"{
  ""classes"": [
    { ""name"": ""Krieger"", ""meleeRangeWire"": 150, ""attackCooldownMs"": 800,
      ""baseHp"": 68, ""baseSp"": 12, ""baseAttack"": 3, ""baseDefense"": 3,
      ""baseSt"": 6, ""baseHt"": 4, ""baseDx"": 3, ""baseIq"": 3,
      ""hpPerHt"": 8, ""hpPerLevel"": 10, ""spPerIq"": 6, ""spPerLevel"": 4, ""attackPerSt"": 2, ""htPerDefense"": 2,
      ""dodgeBpPerDx"": 50, ""dodgeCapBp"": 4000, ""statPointsPerLevel"": 3, ""skillPointsPerLevel"": 1,
      ""regenDelayMs"": 5000, ""regenIntervalMs"": 1000, ""hpRegenPercent"": 1, ""spRegenPercent"": 2 }
  ]
}";

        [Fact]
        public void LoadFromJson_ValidData_ExposesClasses()
        {
            ClassCatalog catalog = ClassCatalog.LoadFromJson(ValidJson);

            Assert.True(catalog.TryGet("Krieger", out ClassDefinition krieger));
            Assert.Equal("Krieger", krieger.Name);
            Assert.Equal(150, krieger.MeleeRangeWire);
            Assert.Equal(800, krieger.AttackCooldownMs);
            Assert.Equal(68, krieger.BaseHp);
            Assert.Equal(12, krieger.BaseSp);
            Assert.Equal(3, krieger.BaseAttack);
            Assert.Equal(3, krieger.BaseDefense);
            Assert.Equal(6, krieger.BaseSt);
            Assert.Equal(4, krieger.BaseHt);
            Assert.Equal(3, krieger.BaseDx);
            Assert.Equal(3, krieger.BaseIq);
            Assert.Equal(8, krieger.HpPerHt);
            Assert.Equal(10, krieger.HpPerLevel);
            Assert.Equal(6, krieger.SpPerIq);
            Assert.Equal(4, krieger.SpPerLevel);
            Assert.Equal(2, krieger.AttackPerSt);
            Assert.Equal(2, krieger.HtPerDefense);
            Assert.Equal(50, krieger.DodgeBpPerDx);
            Assert.Equal(4000, krieger.DodgeCapBp);
            Assert.Equal(3, krieger.StatPointsPerLevel);
            Assert.Equal(1, krieger.SkillPointsPerLevel);
            Assert.Equal(5000, krieger.RegenDelayMs);
            Assert.Equal(1000, krieger.RegenIntervalMs);
            Assert.Equal(1, krieger.HpRegenPercent);
            Assert.Equal(2, krieger.SpRegenPercent);
        }

        [Fact]
        public void TryGet_IsCaseInsensitive()
        {
            ClassCatalog catalog = ClassCatalog.LoadFromJson(ValidJson);
            Assert.True(catalog.TryGet("krieger", out _));
            Assert.True(catalog.TryGet("KRIEGER", out _));
        }

        [Fact]
        public void TryGet_UnknownName_ReturnsFalse()
        {
            ClassCatalog catalog = ClassCatalog.LoadFromJson(ValidJson);
            Assert.False(catalog.TryGet("Magier", out _));
        }

        [Fact]
        public void LoadFromJson_DuplicateName_Throws()
        {
            string json = @"{ ""classes"": [" + ValidEntry("Krieger") + ", " + ValidEntry("Krieger") + "] }";
            var ex = Assert.Throws<ClassCatalogException>(() => ClassCatalog.LoadFromJson(json));
            Assert.Contains("doppelter Klassenname", ex.Message);
        }

        [Theory]
        [InlineData("meleeRangeWire", "0", "meleeRangeWire")]
        [InlineData("attackCooldownMs", "0", "attackCooldownMs")]
        [InlineData("baseHp", "0", "baseHp")]
        [InlineData("baseSp", "-1", "baseSp")]
        [InlineData("baseAttack", "-1", "baseAttack")]
        [InlineData("baseDefense", "-1", "baseDefense")]
        [InlineData("baseSt", "0", "baseSt")]
        [InlineData("baseHt", "0", "baseSt")]
        [InlineData("baseDx", "0", "baseSt")]
        [InlineData("baseIq", "0", "baseSt")]
        [InlineData("hpPerHt", "-1", "Formel-Faktor")]
        [InlineData("htPerDefense", "0", "htPerDefense")]
        [InlineData("dodgeCapBp", "-1", "dodgeCapBp")]
        [InlineData("statPointsPerLevel", "-1", "Punkte pro Level")]
        [InlineData("regenDelayMs", "-1", "regenDelayMs")]
        [InlineData("regenIntervalMs", "0", "regenIntervalMs")]
        [InlineData("hpRegenPercent", "-1", "Regenerationsprozente")]
        public void LoadFromJson_InvalidValue_Throws(string field, string invalidValue, string expectedFieldInMessage)
        {
            string json = @"{ ""classes"": [" + ValidEntry("Krieger", field, invalidValue) + "] }";
            var ex = Assert.Throws<ClassCatalogException>(() => ClassCatalog.LoadFromJson(json));
            Assert.Contains(expectedFieldInMessage, ex.Message);
        }

        [Fact]
        public void LoadFromJson_EmptyClassList_Throws()
        {
            const string json = @"{ ""classes"": [] }";
            Assert.Throws<ClassCatalogException>(() => ClassCatalog.LoadFromJson(json));
        }

        [Fact]
        public void LoadFromJson_MissingName_Throws()
        {
            string json = @"{ ""classes"": [" + ValidEntry(null) + "] }";
            var ex = Assert.Throws<ClassCatalogException>(() => ClassCatalog.LoadFromJson(json));
            Assert.Contains("ohne Namen", ex.Message);
        }

        /// <summary>Baut einen gültigen classes.json-Eintrag, optional mit einem überschriebenen Feld
        /// (Rohtext, damit auch ungültige Zahlenwerte wie "-1" eingesetzt werden können).</summary>
        private static string ValidEntry(string? name, string? overrideField = null, string? overrideValue = null)
        {
            var fields = new System.Collections.Generic.Dictionary<string, string>
            {
                ["meleeRangeWire"] = "150",
                ["attackCooldownMs"] = "800",
                ["baseHp"] = "68",
                ["baseSp"] = "12",
                ["baseAttack"] = "3",
                ["baseDefense"] = "3",
                ["baseSt"] = "6",
                ["baseHt"] = "4",
                ["baseDx"] = "3",
                ["baseIq"] = "3",
                ["hpPerHt"] = "8",
                ["hpPerLevel"] = "10",
                ["spPerIq"] = "6",
                ["spPerLevel"] = "4",
                ["attackPerSt"] = "2",
                ["htPerDefense"] = "2",
                ["dodgeBpPerDx"] = "50",
                ["dodgeCapBp"] = "4000",
                ["statPointsPerLevel"] = "3",
                ["skillPointsPerLevel"] = "1",
                ["regenDelayMs"] = "5000",
                ["regenIntervalMs"] = "1000",
                ["hpRegenPercent"] = "1",
                ["spRegenPercent"] = "2"
            };

            if (overrideField != null)
                fields[overrideField] = overrideValue!;

            var sb = new System.Text.StringBuilder("{ ");
            if (name != null)
                sb.Append($@"""name"": ""{name}"", ");
            foreach (var kvp in fields)
                sb.Append($@"""{kvp.Key}"": {kvp.Value}, ");
            sb.Length -= 2; // letztes ", " entfernen
            sb.Append(" }");
            return sb.ToString();
        }
    }
}
