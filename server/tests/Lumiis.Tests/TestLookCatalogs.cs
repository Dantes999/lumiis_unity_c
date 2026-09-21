using Lumiis.Server.Looks;

namespace Lumiis.Tests
{
    /// <summary>Kleiner, in sich geschlossener Look-Katalog für Tests - unabhängig vom echten data/looks.json
    /// und dem Arbeitsverzeichnis des Testrunners.</summary>
    internal static class TestLookCatalogs
    {
        public const string MinimalValidJson = @"{
  ""settings"": { ""canvasPx"": 128, ""ppu"": 64 },
  ""hairColors"": [""#111111"", ""#222222""],
  ""looks"": [
    { ""id"": 1, ""slot"": ""Body"", ""name"": ""Basis"" },
    { ""id"": 1, ""slot"": ""Hair"", ""name"": ""Kurz"" },
    { ""id"": 1, ""slot"": ""Armor"", ""name"": ""Stoff"" },
    { ""id"": 1, ""slot"": ""Helmet"", ""name"": ""Kappe"" },
    { ""id"": 1, ""slot"": ""Weapon"", ""name"": ""Dolch"" },
    { ""id"": 1, ""slot"": ""Shield"", ""name"": ""Rundschild"" }
  ]
}";

        public static LookCatalog CreateMinimal()
        {
            return LookCatalog.LoadFromJson(MinimalValidJson);
        }
    }
}
