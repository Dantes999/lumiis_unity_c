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
                default: return null;
            }
        }
    }
}
