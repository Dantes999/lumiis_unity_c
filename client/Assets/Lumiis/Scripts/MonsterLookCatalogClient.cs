using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Lumiis.Client
{
    // JsonUtility gleicht Feldnamen exakt (case-sensitive) ab - deshalb lowerCamelCase wie in data/monster-looks.json.
    [Serializable]
    public class MonsterLookJson
    {
        public int mobId;
        public string sheet = "";
        public float scale;
        public string tint = "";
        public bool desaturate;
        public bool stone;
    }

    [Serializable]
    public class MonsterLooksJson
    {
        public MonsterLookJson[] looks = Array.Empty<MonsterLookJson>();
    }

    /// <summary>Aussehen je MobId (Sheet, Größe, Tönung) aus StreamingAssets/lumiis-data/monster-looks.json - reine
    /// Anzeige, erzeugt von tools/build-wiki-content.py. Der Client kennt mobs.json weiterhin nicht.</summary>
    public sealed class MonsterLookCatalogClient
    {
        private readonly Dictionary<int, MonsterLookJson> _looks = new Dictionary<int, MonsterLookJson>();

        public static MonsterLookCatalogClient LoadFromStreamingAssets()
        {
            string path = Path.Combine(Application.streamingAssetsPath, "lumiis-data", "monster-looks.json");
            return new MonsterLookCatalogClient(File.ReadAllText(path));
        }

        public MonsterLookCatalogClient(string json)
        {
            MonsterLooksJson data = JsonUtility.FromJson<MonsterLooksJson>(json) ?? new MonsterLooksJson();
            foreach (MonsterLookJson look in data.looks)
            {
                if (look == null || string.IsNullOrEmpty(look.sheet)) continue;
                _looks[look.mobId] = look;
            }
        }

        public bool TryGet(int mobId, out MonsterLookJson look)
        {
            return _looks.TryGetValue(mobId, out look);
        }

        public static float ScaleOf(MonsterLookJson look)
        {
            return look != null && look.scale > 0f ? look.scale : 1f;
        }

        public static Color TintOf(MonsterLookJson look)
        {
            if (look != null && !string.IsNullOrEmpty(look.tint) && ColorUtility.TryParseHtmlString(look.tint, out Color c))
                return c;
            return Color.white;
        }
    }
}
