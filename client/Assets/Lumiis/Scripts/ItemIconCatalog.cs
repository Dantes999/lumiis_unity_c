using System;
using System.Collections.Generic;
using UnityEngine;

namespace Lumiis.Client
{
    [Serializable]
    public class IconEntryJson
    {
        public string key = "";
        public string category = "";
        public string file = "";
        public string resource = "";
    }

    [Serializable]
    public class IconManifestJson
    {
        public IconEntryJson[] icons = Array.Empty<IconEntryJson>();
    }

    /// <summary>Lädt das aus dem Lumiis-Asset-Archiv importierte Icon-Manifest und die Texturen lazy aus
    /// Resources. Wird von den Inventar-/Shop-Panels und der Icon-Galerie im Asset-Testlabor genutzt.</summary>
    public sealed class ItemIconCatalog
    {
        private readonly Dictionary<string, IconEntryJson> _byKey = new Dictionary<string, IconEntryJson>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Texture2D> _textures = new Dictionary<string, Texture2D>(StringComparer.OrdinalIgnoreCase);
        private readonly List<IconEntryJson> _all = new List<IconEntryJson>();

        public IReadOnlyList<IconEntryJson> All => _all;

        private ItemIconCatalog(IconManifestJson manifest)
        {
            foreach (IconEntryJson entry in manifest.icons)
            {
                if (entry == null || string.IsNullOrEmpty(entry.key)) continue;
                _byKey[entry.key] = entry;
                _all.Add(entry);
            }
        }

        public static ItemIconCatalog LoadFromResources()
        {
            TextAsset asset = Resources.Load<TextAsset>("Icons/icon-manifest");
            if (asset == null)
            {
                Debug.LogWarning("Icons/icon-manifest.json fehlt unter Assets/Resources/Icons/.");
                return new ItemIconCatalog(new IconManifestJson());
            }

            IconManifestJson manifest = JsonUtility.FromJson<IconManifestJson>(asset.text) ?? new IconManifestJson();
            return new ItemIconCatalog(manifest);
        }

        public bool TryGetEntry(string key, out IconEntryJson entry)
        {
            return _byKey.TryGetValue(key ?? "", out entry);
        }

        public Texture2D GetTexture(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            if (_textures.TryGetValue(key, out Texture2D cached)) return cached;
            if (!_byKey.TryGetValue(key, out IconEntryJson entry)) return null;

            Texture2D texture = Resources.Load<Texture2D>(entry.resource);
            _textures[key] = texture;
            return texture;
        }

        public Texture2D GetTextureForItem(int itemId, ItemCatalogClient items)
        {
            if (items == null || !items.TryGet(itemId, out ItemEntryJson item)) return null;
            return GetTexture(item.iconKey);
        }

        /// <summary>Alle Kategorien in Manifest-Reihenfolge, ohne leere Einträge.</summary>
        public IReadOnlyList<string> Categories()
        {
            var categories = new List<string>();
            foreach (IconEntryJson entry in _all)
            {
                if (!string.IsNullOrEmpty(entry.category) && !categories.Contains(entry.category))
                    categories.Add(entry.category);
            }
            return categories;
        }
    }
}
