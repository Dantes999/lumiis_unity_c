using System;
using System.IO;
using UnityEngine;

namespace Lumiis.Client
{
    // JsonUtility gleicht Feldnamen exakt (case-sensitive) mit den JSON-Keys ab - deshalb lowerCamelCase
    // statt der sonst üblichen PascalCase-Konvention, analog zu LookCatalogClient/ItemCatalogClient/
    // SkillCatalogClient.

    [Serializable]
    public class ClassEntryJson
    {
        public string name = "";

        // combat-visuals: nur MeleeRangeWire wird clientseitig gebraucht (Reichweitenring, siehe
        // GameClient/RangeRingView) - die restlichen Formelfelder aus classes.json bleiben
        // serverautoritativ (RecomputeDerivedStats) und werden hier bewusst NICHT geladen.
        public int meleeRangeWire;
    }

    [Serializable]
    public class ClassCatalogJson
    {
        public ClassEntryJson[] classes = Array.Empty<ClassEntryJson>();
    }

    /// <summary>Lädt classes.json aus StreamingAssets (JsonUtility, analog zu SkillCatalogClient) - der
    /// Client braucht nur MeleeRangeWire für den Reichweitenring (combat-visuals). Alpha-Umfang kennt nur
    /// eine Klasse ("Krieger") - der Client wählt deshalb bewusst nicht nach Name aus (der Server schickt
    /// die Klasse des eigenen Charakters nie mit), sondern nimmt den ersten Katalog-Eintrag, analog zur
    /// serverseitigen Vereinfachung über GameServer.DefaultClassName. Sobald es mehrere Klassen gibt, muss
    /// das genauso wie serverseitig durch eine echte Klassen-Zuordnung ersetzt werden.</summary>
    public sealed class ClassCatalogClient
    {
        private readonly int _meleeRangeWire;

        public ClassCatalogClient(string json)
        {
            ClassCatalogJson data = JsonUtility.FromJson<ClassCatalogJson>(json) ?? new ClassCatalogJson();
            _meleeRangeWire = data.classes.Length > 0 ? data.classes[0].meleeRangeWire : 0;
        }

        /// <summary>Nur Desktop/Editor (File.ReadAllText auf StreamingAssets), analog zu
        /// SkillCatalogClient.LoadFromStreamingAssets.</summary>
        public static ClassCatalogClient LoadFromStreamingAssets(string fileName = "classes.json")
        {
            string path = Path.Combine(Application.streamingAssetsPath, "lumiis-data", fileName);
            string json = File.ReadAllText(path);
            return new ClassCatalogClient(json);
        }

        /// <summary>0, falls classes.json leer war (sollte bei korrekt validierten Daten nie vorkommen).</summary>
        public int MeleeRangeWire => _meleeRangeWire;
    }
}
