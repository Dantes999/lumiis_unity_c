using Lumiis.Data.Looks;
using Lumiis.Shared.Net;
using Xunit;

namespace Lumiis.Tests.Data
{
    public class LookCatalogTests
    {
        [Fact]
        public void LoadFromJson_ValidData_ExposesSettings()
        {
            LookCatalog catalog = TestLookCatalogs.CreateMinimal();
            Assert.Equal(128, catalog.CanvasPx);
            Assert.Equal(64, catalog.Ppu);
            Assert.Equal(2, catalog.HairColors.Count);
        }

        [Fact]
        public void LoadFromJson_DuplicateIdInSameSlot_Throws()
        {
            const string json = @"{
  ""settings"": { ""canvasPx"": 128, ""ppu"": 64 },
  ""hairColors"": [""#111111""],
  ""looks"": [
    { ""id"": 1, ""slot"": ""Body"", ""name"": ""Basis"" },
    { ""id"": 1, ""slot"": ""Armor"", ""name"": ""A"" },
    { ""id"": 1, ""slot"": ""Armor"", ""name"": ""B"" }
  ]
}";
            Assert.Throws<LookCatalogException>(() => LookCatalog.LoadFromJson(json));
        }

        [Fact]
        public void LoadFromJson_SameIdDifferentSlots_IsAllowed()
        {
            // Ids sind je Slot eindeutig, nicht global - id=1 darf in mehreren Slots vorkommen (siehe Testkatalog).
            LookCatalog catalog = TestLookCatalogs.CreateMinimal();
            Assert.NotNull(catalog);
        }

        [Fact]
        public void LoadFromJson_HidesHairOnNonHelmet_Throws()
        {
            const string json = @"{
  ""settings"": { ""canvasPx"": 128, ""ppu"": 64 },
  ""hairColors"": [""#111111""],
  ""looks"": [
    { ""id"": 1, ""slot"": ""Body"", ""name"": ""Basis"" },
    { ""id"": 1, ""slot"": ""Armor"", ""name"": ""A"", ""hidesHair"": true }
  ]
}";
            Assert.Throws<LookCatalogException>(() => LookCatalog.LoadFromJson(json));
        }

        [Fact]
        public void LoadFromJson_HidesHairOnHelmet_IsAllowed()
        {
            const string json = @"{
  ""settings"": { ""canvasPx"": 128, ""ppu"": 64 },
  ""hairColors"": [""#111111""],
  ""looks"": [
    { ""id"": 1, ""slot"": ""Body"", ""name"": ""Basis"" },
    { ""id"": 1, ""slot"": ""Helmet"", ""name"": ""Vollhelm"", ""hidesHair"": true }
  ]
}";
            LookCatalog catalog = LookCatalog.LoadFromJson(json);
            Assert.NotNull(catalog);
        }

        [Fact]
        public void LoadFromJson_EmptyHairColors_Throws()
        {
            const string json = @"{
  ""settings"": { ""canvasPx"": 128, ""ppu"": 64 },
  ""hairColors"": [],
  ""looks"": [
    { ""id"": 1, ""slot"": ""Body"", ""name"": ""Basis"" }
  ]
}";
            Assert.Throws<LookCatalogException>(() => LookCatalog.LoadFromJson(json));
        }

        [Fact]
        public void LoadFromJson_NoBodyLook_Throws()
        {
            const string json = @"{
  ""settings"": { ""canvasPx"": 128, ""ppu"": 64 },
  ""hairColors"": [""#111111""],
  ""looks"": [
    { ""id"": 1, ""slot"": ""Armor"", ""name"": ""A"" }
  ]
}";
            Assert.Throws<LookCatalogException>(() => LookCatalog.LoadFromJson(json));
        }

        [Fact]
        public void LoadFromJson_InvalidJson_Throws()
        {
            Assert.Throws<LookCatalogException>(() => LookCatalog.LoadFromJson("{ nicht valides json"));
        }

        [Fact]
        public void CreateDefaultAppearance_PicksFirstEntryPerSlot()
        {
            const string json = @"{
  ""settings"": { ""canvasPx"": 128, ""ppu"": 64 },
  ""hairColors"": [""#111111"", ""#222222""],
  ""looks"": [
    { ""id"": 1, ""slot"": ""Body"", ""name"": ""Basis"" },
    { ""id"": 5, ""slot"": ""Armor"", ""name"": ""Erste"" },
    { ""id"": 9, ""slot"": ""Armor"", ""name"": ""Zweite"" }
  ]
}";
            LookCatalog catalog = LookCatalog.LoadFromJson(json);
            Appearance appearance = catalog.CreateDefaultAppearance();

            Assert.Equal(1, appearance.BodyId);
            Assert.Equal(5, appearance.ArmorId); // erster Eintrag im Katalog (Id 5), nicht der kleinste (Id 9 kleiner wäre falsch anzunehmen)
            Assert.Equal(0, appearance.HairStyleId); // kein Hair-Eintrag im Katalog -> 0
            Assert.Equal(1, appearance.HairColorId); // erste Haarfarbe
        }

        [Fact]
        public void CreateDefaultAppearance_IsDeterministic()
        {
            LookCatalog catalog = TestLookCatalogs.CreateMinimal();
            Appearance first = catalog.CreateDefaultAppearance();
            Appearance second = catalog.CreateDefaultAppearance();
            Assert.Equal(first, second);
        }
    }
}
