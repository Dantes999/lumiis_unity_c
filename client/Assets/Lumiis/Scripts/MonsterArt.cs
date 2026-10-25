using System.Collections.Generic;
using UnityEngine;

namespace Lumiis.Client
{
    /// <summary>Lädt die Referenz-Monster-Sheets (40x32-Zellen, 5 Frames, Reihenfolge unten/links/rechts/oben)
    /// und schneidet daraus Lauf-/Idle-Sprites. Reine Anzeige, keine Spielwahrheit.</summary>
    public static class MonsterArt
    {
        public const int CellWidth = 40;
        public const int CellHeight = 32;
        public const int FramesPerRow = 5;
        public const int RowCount = 4;

        private static readonly Dictionary<string, Sprite[]> Cache = new Dictionary<string, Sprite[]>();

        public static Sprite[] LoadFrames(string resourceName)
        {
            if (string.IsNullOrEmpty(resourceName)) return null;
            if (Cache.TryGetValue(resourceName, out Sprite[] cached)) return cached;

            Texture2D texture = Resources.Load<Texture2D>("Sprites/Monsters/" + resourceName);
            if (texture == null) return null;

            var frames = new Sprite[FramesPerRow * RowCount];
            for (int row = 0; row < RowCount; row++)
            {
                for (int col = 0; col < FramesPerRow; col++)
                {
                    // PNG-Zeilen sind von oben nach unten gezeichnet; Unity-Texturen haben den Ursprung
                    // unten links. Deshalb die Zeile spiegeln.
                    float y = texture.height - (row + 1) * CellHeight;
                    var rect = new Rect(col * CellWidth, y, CellWidth, CellHeight);
                    frames[row * FramesPerRow + col] = Sprite.Create(texture, rect, new Vector2(0.5f, 0.09f), 32f);
                }
            }

            Cache[resourceName] = frames;
            return frames;
        }

        public static string ResourceForMob(int mobId)
        {
            switch (mobId)
            {
                case 1: return "alpha_wolf";
                case 2: return "boar";
                case 5: return "alpha_wolf"; // Hungriger Wildhund: gleiches Sheet, rote Tönung
                case 6: return "dummy";
                // Map1 (Yongan, Originaldaten aus lumiis-2d, siehe tools/import-map1-spawns.py)
                case 20: return "dog";        // Wilder Hund
                case 21: return "wolf";       // Wolf
                case 22: return "alpha_wolf"; // Alphawolf
                case 23: return "boar";       // Wildschwein
                default: return null;
            }
        }

        // Lumiis-Stein-Sheet (200x104): intakter Stein bei (120,0,40,52), Bruchstücke bei (160,0,40,52); die
        // übrigen Zellen sind Riss-Überlagerungen (siehe tools/monster-sheets/metin_sorrow.json).
        private const int StoneCellX = 120;
        private const int StoneCellWidth = 40;
        private const int StoneCellHeight = 52;
        private static readonly Dictionary<string, Sprite> StoneCache = new Dictionary<string, Sprite>();

        /// <summary>Lumiis-Steine haben kein Lauf-Sheet, sondern ein einzelnes Bild.</summary>
        public static string StoneResourceForMob(int mobId)
        {
            switch (mobId)
            {
                case 3:  // Lumiis des Kummers (alte Testkarten)
                case 18: // Lumiis der Schwärze (Jayang, dunkel getönt)
                case 24: // Lumiis des Kummers (Map1)
                    return "metin_sorrow";
                default: return null;
            }
        }

        public static Sprite LoadStone(string resourceName)
        {
            if (string.IsNullOrEmpty(resourceName)) return null;
            if (StoneCache.TryGetValue(resourceName, out Sprite cached)) return cached;

            Texture2D texture = Resources.Load<Texture2D>("Sprites/Monsters/" + resourceName);
            if (texture == null) return null;

            var rect = new Rect(StoneCellX, texture.height - StoneCellHeight, StoneCellWidth, StoneCellHeight);
            Sprite sprite = Sprite.Create(texture, rect, new Vector2(0.5f, 0.08f), 32f);
            StoneCache[resourceName] = sprite;
            return sprite;
        }
    }
}
