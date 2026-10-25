using System;
using System.IO;
using UnityEngine;

namespace Lumiis.Client
{
    // JsonUtility gleicht Feldnamen exakt (case-sensitive) ab - lowerCamelCase wie in upgrade.json.
    [Serializable]
    public class UpgradeCatalogJson
    {
        public int maxUpgradeLevel = 9;
        public int[] successChancePercent = Array.Empty<int>();
        public int whetstoneItemId;
        public int protectionScrollItemId;
    }

    /// <summary>Lädt upgrade.json aus StreamingAssets - nur für die Anzeige im Schmied-Fenster (Erfolgschance,
    /// benötigte Materialien). Ob eine Aufwertung gelingt, entscheidet ausschließlich der Server.</summary>
    public sealed class UpgradeCatalogClient
    {
        private readonly UpgradeCatalogJson _data;

        public UpgradeCatalogClient(string json)
        {
            _data = JsonUtility.FromJson<UpgradeCatalogJson>(json) ?? new UpgradeCatalogJson();
        }

        /// <summary>Nur Desktop/Editor (File.ReadAllText auf StreamingAssets), analog zu ClassCatalogClient.</summary>
        public static UpgradeCatalogClient LoadFromStreamingAssets(string fileName = "upgrade.json")
        {
            string path = Path.Combine(Application.streamingAssetsPath, "lumiis-data", fileName);
            return new UpgradeCatalogClient(File.ReadAllText(path));
        }

        public int MaxUpgradeLevel => _data.maxUpgradeLevel;
        public int WhetstoneItemId => _data.whetstoneItemId;
        public int ProtectionScrollItemId => _data.protectionScrollItemId;

        /// <summary>Erfolgschance für den Schritt von currentLevel auf currentLevel+1, -1 = unbekannt/Maximum.</summary>
        public int SuccessChancePercent(int currentLevel)
        {
            if (currentLevel < 0 || currentLevel >= _data.successChancePercent.Length) return -1;
            return _data.successChancePercent[currentLevel];
        }
    }
}
