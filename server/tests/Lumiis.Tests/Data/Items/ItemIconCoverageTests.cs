using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Lumiis.Data.Items;
using Lumiis.Data.Looks;
using Xunit;

namespace Lumiis.Tests.Data.Items
{
    /// <summary>Stellt sicher, dass jeder iconKey aus data/items.json im importierten
    /// Lumiis-Icon-Manifest existiert. Der Test schützt vor Tippfehlern und vor dem erneuten
    /// Auseinanderlaufen von Item-Katalog und Asset-Import (siehe docs/icon-integration-plan.md).</summary>
    public class ItemIconCoverageTests
    {
        [Fact]
        public void RealItemCatalog_AllIconKeysExistInManifest()
        {
            string root = ResolveRepoRoot();
            string dataDir = Path.Combine(root, "data");
            LookCatalog looks = LookCatalog.LoadFromFile(Path.Combine(dataDir, "looks.json"));
            ItemCatalog items = ItemCatalog.LoadFromFile(Path.Combine(dataDir, "items.json"), looks);

            string manifestPath = Path.Combine(root, "client", "Assets", "Resources", "Icons", "icon-manifest.json");
            using JsonDocument manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (JsonElement icon in manifest.RootElement.GetProperty("icons").EnumerateArray())
                keys.Add(icon.GetProperty("key").GetString() ?? "");

            foreach (ItemDefinition item in items.All)
            {
                Assert.False(string.IsNullOrWhiteSpace(item.IconKey), $"Item {item.Id} ({item.Name}) hat keinen iconKey.");
                Assert.True(keys.Contains(item.IconKey), $"Item {item.Id} ({item.Name}): iconKey '{item.IconKey}' fehlt im Manifest.");
            }
        }

        private static string ResolveRepoRoot()
        {
            DirectoryInfo? dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "data", "items.json"))) return dir.FullName;
                dir = dir.Parent;
            }
            throw new FileNotFoundException("Repo-Root mit data/items.json nicht gefunden (ausgehend von " + AppContext.BaseDirectory + ").");
        }
    }
}
