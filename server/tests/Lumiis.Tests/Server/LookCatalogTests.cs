using Lumiis.Server.Looks;
using Lumiis.Shared.Net;
using Xunit;

namespace Lumiis.Tests.Server
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
        public void CreateAppearanceForName_IsDeterministic()
        {
            LookCatalog catalog = TestLookCatalogs.CreateMinimal();
            Appearance first = catalog.CreateAppearanceForName("Alice");
            Appearance second = catalog.CreateAppearanceForName("Alice");
            Assert.Equal(first, second);
        }

        [Fact]
        public void CreateAppearanceForName_DifferentNames_ProduceVariety()
        {
            // Der Testkatalog hat nur 1 Option je Slot (0 oder 1), zwei einzelne Namen können also leicht
            // kollidieren. Über eine größere Stichprobe muss aber Varianz auftreten, sonst flösse der Name
            // gar nicht in die Berechnung ein.
            LookCatalog catalog = TestLookCatalogs.CreateMinimal();
            var seen = new System.Collections.Generic.HashSet<Appearance>();
            for (int i = 0; i < 30; i++)
                seen.Add(catalog.CreateAppearanceForName("Bot" + i));

            Assert.True(seen.Count > 1, "Erwartet mehrere unterschiedliche Appearances über 30 verschiedene Namen.");
        }

        [Fact]
        public void CreateAppearanceForName_AlwaysAssignsValidBody()
        {
            LookCatalog catalog = TestLookCatalogs.CreateMinimal();
            Appearance appearance = catalog.CreateAppearanceForName("Bob");
            Assert.Equal(1, appearance.BodyId); // einziger Body-Look im Testkatalog
        }

        [Theory]
        [InlineData("Alice")]
        [InlineData("Bob")]
        [InlineData("Zzzzzzzzzzzzzzz")]
        [InlineData("Umlaut_Äöü")]
        public void CreateAppearanceForName_OnlyReturnsValidIds(string name)
        {
            LookCatalog catalog = TestLookCatalogs.CreateMinimal();
            Appearance appearance = catalog.CreateAppearanceForName(name);

            // Testkatalog hat je optionalem Slot genau Id 1 (oder 0 = keine Ebene) und 2 Haarfarben (1-basiert).
            Assert.Equal(1, appearance.BodyId);
            Assert.True(appearance.HairStyleId == 0 || appearance.HairStyleId == 1);
            Assert.True(appearance.ArmorId == 0 || appearance.ArmorId == 1);
            Assert.True(appearance.HelmetId == 0 || appearance.HelmetId == 1);
            Assert.True(appearance.WeaponId == 0 || appearance.WeaponId == 1);
            Assert.True(appearance.ShieldId == 0 || appearance.ShieldId == 1);
            Assert.True(appearance.HairColorId >= 0 && appearance.HairColorId <= 2);
        }
    }
}
