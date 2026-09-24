using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Lumiis.Client
{
    // JsonUtility gleicht Feldnamen exakt (case-sensitive) mit den JSON-Keys ab - deshalb lowerCamelCase
    // statt der sonst üblichen PascalCase-Konvention, analog zu LookCatalogClient.

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
    }

    [Serializable]
    public class ItemCatalogJson
    {
        public ItemEntryJson[] items = Array.Empty<ItemEntryJson>();
    }

    /// <summary>Lädt items.json aus StreamingAssets (JsonUtility, analog zu LookCatalogClient) - der Client
    /// braucht das für das Debug-Panel (M5a): Anzeigename und ItemType je ItemId, um zwischen "Anlegen" und
    /// "Benutzen" zu entscheiden. LookId/AttackBonus/DefenseBonus/HealAmount werden hier bewusst NICHT
    /// gebraucht (keine echte Inventar-Grafik, kein clientseitiger Schaden-/Heilungs-Vorschau-Text, siehe
    /// CLAUDE.md Abschnitt "Items und Inventar") - trotzdem mitgeladen, um dieselbe Struktur wie
    /// data/items.json 1:1 abzubilden.</summary>
    public sealed class ItemCatalogClient
    {
        private readonly Dictionary<int, ItemEntryJson> _byId = new Dictionary<int, ItemEntryJson>();

        public ItemCatalogClient(string json)
        {
            ItemCatalogJson data = JsonUtility.FromJson<ItemCatalogJson>(json) ?? new ItemCatalogJson();
            foreach (ItemEntryJson entry in data.items)
                _byId[entry.id] = entry;
        }

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

        /// <summary>Anzeigename für einen belegten Slot - "#<id>" als Fallback, falls der Katalog (sollte bei
        /// korrekt validierten Daten nie vorkommen) die ItemId nicht kennt.</summary>
        public string DisplayName(int itemId)
        {
            return TryGet(itemId, out ItemEntryJson entry) ? entry.name : "#" + itemId;
        }
    }
}
