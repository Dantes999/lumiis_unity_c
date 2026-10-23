using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Lumiis.Client
{
    // JsonUtility gleicht Feldnamen exakt (case-sensitive) mit den JSON-Keys ab - deshalb lowerCamelCase
    // statt der sonst üblichen PascalCase-Konvention. Keine Dictionaries möglich, nur feste Felder/Arrays.

    [Serializable]
    public class LookCatalogSettingsJson
    {
        public int canvasPx;
        public int ppu;
    }

    [Serializable]
    public class LookAnchorJson
    {
        public int[] hand = Array.Empty<int>();
        public int[] head = Array.Empty<int>();
    }

    [Serializable]
    public class LookAnchorsJson
    {
        public LookAnchorJson down = new LookAnchorJson();
        public LookAnchorJson up = new LookAnchorJson();
        public LookAnchorJson side = new LookAnchorJson();
    }

    [Serializable]
    public class LookColorsJson
    {
        public string primaryColor = "";
        public string secondaryColor = "";
        public string accentColor = "";

        public bool HasAny => !string.IsNullOrEmpty(primaryColor)
            || !string.IsNullOrEmpty(secondaryColor)
            || !string.IsNullOrEmpty(accentColor);
    }

    [Serializable]
    public class LookEntryJson
    {
        public int id;
        public string slot = "";
        public string name = "";
        public bool hidesHair;
        public LookAnchorsJson anchors = new LookAnchorsJson();

        /// <summary>Palette-Swapping für Rüstungs-Looks (Hex-Strings) - wird von CharacterView per
        /// MaterialPropertyBlock an den Rüstungs-Renderer übergeben (siehe ApplyArmorColors).</summary>
        public LookColorsJson colors = new LookColorsJson();
    }

    [Serializable]
    public class LookCatalogJson
    {
        public LookCatalogSettingsJson settings = new LookCatalogSettingsJson();
        public string[] hairColors = Array.Empty<string>();
        public LookEntryJson[] looks = Array.Empty<LookEntryJson>();
    }

    /// <summary>Lädt looks.json aus StreamingAssets (JsonUtility). Indiziert die Einträge für schnellen Zugriff
    /// nach Slot bzw. Slot+Id - das ist ein reiner Laufzeit-Index, keine Deserialisierung in ein Dictionary.</summary>
    public sealed class LookCatalogClient
    {
        private readonly LookCatalogJson _data;
        private readonly Dictionary<string, List<LookEntryJson>> _bySlot = new Dictionary<string, List<LookEntryJson>>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<(string Slot, int Id), LookEntryJson> _byKey = new Dictionary<(string, int), LookEntryJson>();

        public int CanvasPx => _data.settings.canvasPx;
        public int Ppu => _data.settings.ppu;
        public IReadOnlyList<string> HairColors => _data.hairColors;

        public LookCatalogClient(string json)
        {
            _data = JsonUtility.FromJson<LookCatalogJson>(json) ?? new LookCatalogJson();

            foreach (LookEntryJson entry in _data.looks)
            {
                if (!_bySlot.TryGetValue(entry.slot, out List<LookEntryJson> list))
                {
                    list = new List<LookEntryJson>();
                    _bySlot[entry.slot] = list;
                }
                list.Add(entry);
                _byKey[(entry.slot, entry.id)] = entry;
            }
        }

        /// <summary>Nur Desktop/Editor (File.ReadAllText auf StreamingAssets) - Android/WebGL bräuchten
        /// UnityWebRequest, nicht Teil von M1c.</summary>
        public static LookCatalogClient LoadFromStreamingAssets(string fileName = "looks.json")
        {
            string path = Path.Combine(Application.streamingAssetsPath, "lumiis-data", fileName);
            string json = File.ReadAllText(path);
            return new LookCatalogClient(json);
        }

        public bool TryGetEntry(string slot, int id, out LookEntryJson entry)
        {
            return _byKey.TryGetValue((slot, id), out entry);
        }

        /// <summary>Farbpalette eines Rüstungs-Looks - false, wenn der Look fehlt oder keine Farben
        /// hinterlegt sind (CharacterView fällt dann auf Weiß/den Platzhalter-Farbton zurück).</summary>
        public bool TryGetArmorColors(int armorId, out LookColorsJson colors)
        {
            colors = null;
            if (armorId <= 0) return false;
            if (!_byKey.TryGetValue(("Armor", armorId), out LookEntryJson entry)) return false;
            if (entry.colors == null || !entry.colors.HasAny) return false;
            colors = entry.colors;
            return true;
        }

        public IReadOnlyList<int> GetIdsForSlot(string slot)
        {
            if (!_bySlot.TryGetValue(slot, out List<LookEntryJson> list)) return Array.Empty<int>();

            var ids = new List<int>(list.Count);
            foreach (LookEntryJson entry in list) ids.Add(entry.id);
            return ids;
        }
    }
}
