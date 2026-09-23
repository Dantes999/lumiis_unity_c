using Lumiis.Data.Classes;
using Xunit;

namespace Lumiis.Tests.Data.Classes
{
    public class ClassCatalogTests
    {
        private const string ValidJson = @"{
  ""classes"": [
    { ""name"": ""Krieger"", ""maxHp"": 100, ""attack"": 15, ""defense"": 5, ""meleeRangeWire"": 150, ""attackCooldownMs"": 800 }
  ]
}";

        [Fact]
        public void LoadFromJson_ValidData_ExposesClasses()
        {
            ClassCatalog catalog = ClassCatalog.LoadFromJson(ValidJson);

            Assert.True(catalog.TryGet("Krieger", out ClassDefinition krieger));
            Assert.Equal("Krieger", krieger.Name);
            Assert.Equal(100, krieger.MaxHp);
            Assert.Equal(15, krieger.Attack);
            Assert.Equal(5, krieger.Defense);
            Assert.Equal(150, krieger.MeleeRangeWire);
            Assert.Equal(800, krieger.AttackCooldownMs);
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
            const string json = @"{
  ""classes"": [
    { ""name"": ""Krieger"", ""maxHp"": 100, ""attack"": 15, ""defense"": 5, ""meleeRangeWire"": 150, ""attackCooldownMs"": 800 },
    { ""name"": ""Krieger"", ""maxHp"": 90, ""attack"": 10, ""defense"": 5, ""meleeRangeWire"": 150, ""attackCooldownMs"": 800 }
  ]
}";
            var ex = Assert.Throws<ClassCatalogException>(() => ClassCatalog.LoadFromJson(json));
            Assert.Contains("doppelter Klassenname", ex.Message);
        }

        [Theory]
        [InlineData(@"{ ""classes"": [ { ""name"": ""Krieger"", ""maxHp"": 0, ""attack"": 15, ""defense"": 5, ""meleeRangeWire"": 150, ""attackCooldownMs"": 800 } ] }", "maxHp")]
        [InlineData(@"{ ""classes"": [ { ""name"": ""Krieger"", ""maxHp"": 100, ""attack"": -1, ""defense"": 5, ""meleeRangeWire"": 150, ""attackCooldownMs"": 800 } ] }", "attack")]
        [InlineData(@"{ ""classes"": [ { ""name"": ""Krieger"", ""maxHp"": 100, ""attack"": 15, ""defense"": -1, ""meleeRangeWire"": 150, ""attackCooldownMs"": 800 } ] }", "defense")]
        [InlineData(@"{ ""classes"": [ { ""name"": ""Krieger"", ""maxHp"": 100, ""attack"": 15, ""defense"": 5, ""meleeRangeWire"": 0, ""attackCooldownMs"": 800 } ] }", "meleeRangeWire")]
        [InlineData(@"{ ""classes"": [ { ""name"": ""Krieger"", ""maxHp"": 100, ""attack"": 15, ""defense"": 5, ""meleeRangeWire"": 150, ""attackCooldownMs"": 0 } ] }", "attackCooldownMs")]
        public void LoadFromJson_InvalidValue_Throws(string json, string expectedFieldInMessage)
        {
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
            const string json = @"{ ""classes"": [ { ""maxHp"": 100, ""attack"": 15, ""defense"": 5, ""meleeRangeWire"": 150, ""attackCooldownMs"": 800 } ] }";
            var ex = Assert.Throws<ClassCatalogException>(() => ClassCatalog.LoadFromJson(json));
            Assert.Contains("ohne Namen", ex.Message);
        }
    }
}
