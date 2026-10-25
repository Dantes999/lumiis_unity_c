using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Lumiis.Client
{
    // JsonUtility gleicht Feldnamen exakt (case-sensitive) mit den JSON-Keys ab - deshalb lowerCamelCase
    // statt der sonst üblichen PascalCase-Konvention, analog zu LookCatalogClient.

    /// <summary>Pixel-Korrektur des Waffen-Pivots relativ zum Griff (x/y, in Canvas-Pixeln).</summary>
    [Serializable]
    public class PivotOffsetJson
    {
        public float x;
        public float y;
    }

    [Serializable]
    public class ItemEntryJson
    {
        public int id;
        public string name = "";
        public string itemType = "";
        public int lookId;
        public int attackBonus;
        public int defenseBonus;
        public int healAmount;

        /// <summary>M6c: Bonus auf Attack/Defense je Aufwertungsstufe (nur Ausrüstung, sonst 0) - reine
        /// Anzeige-/Testinfo, die tatsächlichen Werte kommen weiterhin vom Server (CharacterStats).</summary>
        public int upgradeBonusPerLevel;

        /// <summary>M5b: Verkaufspreis (0 = nicht verkäuflich) - fürs Shop-Panel gebraucht (kein Paket trägt
        /// Verkaufspreise, nur BuyPrice über ShopContentPacket, siehe GameClient.DrawShopPanel). BuyPrice wird
        /// hier bewusst NICHT geladen - die kommt beim Kaufen ohnehin autoritativ vom Server.</summary>
        public long sellPrice;

        /// <summary>Kaufpreis laut items.json - nur für die Debug-Vorschau des Schmied-Fensters; im echten
        /// Spiel kommt der Preis autoritativ per ShopContentPacket.</summary>
        public long buyPrice;

        /// <summary>Paper-Doll-Rendering: Dateiname (ohne Endung) des statischen Waffen-PNGs unter
        /// Resources/Sprites/Items/. Leer = CharacterView nutzt den generierten Platzhalter.</summary>
        public string spriteKey = "";

        /// <summary>Paper-Doll-Rendering: Pixel-Korrektur des Waffen-Pivots relativ zum Griff.</summary>
        public PivotOffsetJson pivotOffset = new PivotOffsetJson();

        /// <summary>Paper-Doll-Rendering: Standard-Aufwertungsstufe der Vorlage (0-9). Die tatsächliche
        /// Instanzstufe kommt später aus dem Inventar; aktuell nutzt der Client diesen Wert als Fallback.</summary>
        public int upgradeLevel;

        /// <summary>Icon-Schlüssel aus dem Lumiis-Asset-Archiv (z. B. "sword_30", "armor_ninja_1").</summary>
        public string iconKey = "";

        public int ClampedUpgradeLevel => Mathf.Clamp(upgradeLevel, MinUpgradeLevel, MaxUpgradeLevel);

        public const int MinUpgradeLevel = 0;
        public const int MaxUpgradeLevel = 9;
    }

    [Serializable]
    public class ItemCatalogJson
    {
        public ItemEntryJson[] items = Array.Empty<ItemEntryJson>();
    }

    /// <summary>Lädt items.json aus StreamingAssets (JsonUtility, analog zu LookCatalogClient) - der Client
    /// braucht das für das Debug-Panel: Anzeigename und ItemType je ItemId (M5a, um zwischen "Anlegen" und
    /// "Benutzen" zu entscheiden), SellPrice (M5b, Shop-Panel) sowie seit dem Paper-Doll-Umbau die
    /// Rendering-Felder der Waffe (spriteKey/pivotOffset/upgradeLevel).</summary>
    public sealed class ItemCatalogClient
    {
        private readonly Dictionary<int, ItemEntryJson> _byId = new Dictionary<int, ItemEntryJson>();
        private readonly List<ItemEntryJson> _entries = new List<ItemEntryJson>();

        public ItemCatalogClient(string json)
        {
            ItemCatalogJson data = JsonUtility.FromJson<ItemCatalogJson>(json) ?? new ItemCatalogJson();
            foreach (ItemEntryJson entry in data.items)
            {
                _byId[entry.id] = entry;
                _entries.Add(entry);
            }
        }

        public IReadOnlyList<ItemEntryJson> Entries => _entries;

        /// <summary>Nur Desktop/Editor (File.ReadAllText auf StreamingAssets), analog zu
        /// LookCatalogClient.LoadFromStreamingAssets.</summary>
        public static ItemCatalogClient LoadFromStreamingAssets(string fileName = "items.json")
        {
            string path = Path.Combine(Application.streamingAssetsPath, "lumiis-data", fileName);
            string json = File.ReadAllText(path);
            return new ItemCatalogClient(json);
        }

        public bool TryGet(int itemId, out ItemEntryJson entry)
        {
            return _byId.TryGetValue(itemId, out entry);
        }

        /// <summary>Erster Eintrag des angegebenen ItemTyps (Reihenfolge wie in items.json) - für das
        /// Waffen-Testfeld, das ohne fest verdrahtete Item-Id auskommt.</summary>
        public ItemEntryJson FirstOfType(string itemType)
        {
            foreach (ItemEntryJson entry in _entries)
            {
                if (string.Equals(entry.itemType, itemType, StringComparison.OrdinalIgnoreCase))
                    return entry;
            }
            return null;
        }

        /// <summary>Anzeigename für einen belegten Slot - "#<id>" als Fallback, falls der Katalog (sollte bei
        /// korrekt validierten Daten nie vorkommen) die ItemId nicht kennt.</summary>
        public string DisplayName(int itemId)
        {
            return TryGet(itemId, out ItemEntryJson entry) ? entry.name : "#" + itemId;
        }

        /// <summary>M6c: erster Eintrag eines ItemTyps (z. B. "ProtectionScroll") - für die rein
        /// clientseitige Vorprüfung "ist überhaupt eine Bewahrungsrolle im Inventar?".</summary>
        public bool HasType(string itemType)
        {
            return FirstOfType(itemType) != null;
        }
    }
}
