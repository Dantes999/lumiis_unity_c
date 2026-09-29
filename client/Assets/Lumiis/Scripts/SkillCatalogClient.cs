using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Lumiis.Client
{
    // JsonUtility gleicht Feldnamen exakt (case-sensitive) mit den JSON-Keys ab - deshalb lowerCamelCase
    // statt der sonst üblichen PascalCase-Konvention, analog zu LookCatalogClient/ItemCatalogClient.

    [Serializable]
    public class SkillLevelEntryJson
    {
        // Nur CooldownMs wird clientseitig gebraucht (lokale Cooldown-Anzeige, siehe
        // GameClient.HandleSkillHotkeys) - SpCost/DamageMultiplierPercent/AttackBonusPercent/
        // CooldownReductionPercent bleiben serverautoritativ und werden hier bewusst NICHT geladen (keine
        // clientseitige Schaden-/Heilungs-Vorschau, analog zu ItemCatalogClient).
        public int cooldownMs;
    }

    [Serializable]
    public class SkillEntryJson
    {
        public int id;
        public string name = "";
        public SkillLevelEntryJson[] levels = Array.Empty<SkillLevelEntryJson>();
    }

    [Serializable]
    public class SkillCatalogJson
    {
        public SkillEntryJson[] skills = Array.Empty<SkillEntryJson>();
    }

    /// <summary>Lädt skills.json aus StreamingAssets (JsonUtility, analog zu ItemCatalogClient) - der Client
    /// braucht das für Anzeigenamen (Overlay/Debug-Panel) und den skill-eigenen CooldownMs je Stufe (lokale
    /// Cooldown-Anzeige nach Tastendruck, siehe GameClient.HandleSkillHotkeys/DrawSkillsLine - rein
    /// optisch/optimistisch, der Server bleibt die einzige Autorität, ein verfrüht gesendetes UseSkillRequest
    /// bekommt ganz normal UseSkillFailedPacket(OnCooldown)).</summary>
    public sealed class SkillCatalogClient
    {
        /// <summary>Client-seitiger Spiegel von Lumiis.Data.Skills.SkillCatalog.MaxSkillLevel (M6b) - der
        /// Client referenziert die Server-Assembly nicht, deshalb eine eigene, kleine Konstante statt eines
        /// Querverweises.</summary>
        public const int MaxLevel = 17;

        private readonly Dictionary<int, SkillEntryJson> _byId = new Dictionary<int, SkillEntryJson>();

        public SkillCatalogClient(string json)
        {
            SkillCatalogJson data = JsonUtility.FromJson<SkillCatalogJson>(json) ?? new SkillCatalogJson();
            foreach (SkillEntryJson entry in data.skills)
                _byId[entry.id] = entry;
        }

        /// <summary>Nur Desktop/Editor (File.ReadAllText auf StreamingAssets), analog zu
        /// ItemCatalogClient.LoadFromStreamingAssets.</summary>
        public static SkillCatalogClient LoadFromStreamingAssets(string fileName = "skills.json")
        {
            string path = Path.Combine(Application.streamingAssetsPath, "lumiis-data", fileName);
            string json = File.ReadAllText(path);
            return new SkillCatalogClient(json);
        }

        public bool TryGet(int skillId, out SkillEntryJson entry)
        {
            return _byId.TryGetValue(skillId, out entry);
        }

        /// <summary>Anzeigename - "#<id>" als Fallback, falls der Katalog (sollte bei korrekt validierten
        /// Daten nie vorkommen) die SkillId nicht kennt.</summary>
        public string DisplayName(int skillId)
        {
            return TryGet(skillId, out SkillEntryJson entry) ? entry.name : "#" + skillId;
        }

        /// <summary>CooldownMs für eine konkrete Stufe (1-17) - 0 bei unbekannter Skill-/Stufen-Kombination
        /// (level &lt;= 0 = nicht gelernt, kein Cooldown zum Anzeigen).</summary>
        public int CooldownMsForLevel(int skillId, int level)
        {
            if (level <= 0 || !TryGet(skillId, out SkillEntryJson entry)) return 0;
            int index = level - 1;
            if (index < 0 || index >= entry.levels.Length) return 0;
            return entry.levels[index].cooldownMs;
        }
    }
}
