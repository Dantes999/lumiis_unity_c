using System.Collections.Generic;
using UnityEngine;

namespace Lumiis.Client
{
    /// <summary>Lädt die Monster-Sheets aus Resources/Sprites/Monsters (5 Frames je Zeile, Zeilen unten/links/rechts/
    /// oben; normale Monster 40x32-Zellen, Bosse 56x48 - die Zellgröße folgt aus der Bildgröße) und schneidet daraus
    /// Lauf-/Idle-Sprites. Welche MobId welches Sheet nutzt, steht in data/monster-looks.json
    /// (MonsterLookCatalogClient). Reine Anzeige, keine Spielwahrheit.</summary>
    public sealed class MonsterArt
    {
        public const int FramesPerRow = 5;
        public const int RowCount = 4;
        private const float PixelsPerUnit = 32f;

        private readonly Dictionary<string, Sprite[]> _frameCache = new Dictionary<string, Sprite[]>();
        private readonly Dictionary<string, Sprite> _stoneCache = new Dictionary<string, Sprite>();
        private readonly List<Texture2D> _ownedTextures = new List<Texture2D>();

        public Sprite[] LoadFrames(string resourceName, bool desaturate = false)
        {
            if (string.IsNullOrEmpty(resourceName)) return null;
            string key = desaturate ? resourceName + "#grau" : resourceName;
            if (_frameCache.TryGetValue(key, out Sprite[] cached)) return cached;

            Texture2D texture = LoadTexture(resourceName, desaturate);
            if (texture == null) return null;

            int cellWidth = texture.width / FramesPerRow;
            int cellHeight = texture.height / RowCount;
            var frames = new Sprite[FramesPerRow * RowCount];
            for (int row = 0; row < RowCount; row++)
            {
                for (int col = 0; col < FramesPerRow; col++)
                {
                    // PNG-Zeilen sind von oben nach unten gezeichnet; Unity-Texturen haben den Ursprung
                    // unten links. Deshalb die Zeile spiegeln.
                    float y = texture.height - (row + 1) * cellHeight;
                    var rect = new Rect(col * cellWidth, y, cellWidth, cellHeight);
                    // Fußpunkt bei 3/32 der Zellhöhe (wie bisher bei den 40x32-Sheets)
                    frames[row * FramesPerRow + col] = Sprite.Create(texture, rect, new Vector2(0.5f, 0.09f), PixelsPerUnit);
                }
            }

            _frameCache[key] = frames;
            return frames;
        }

        // Lumiis-Stein-Sheet (200x104): intakter Stein bei (120,0,40,52), Bruchstücke bei (160,0,40,52); die
        // übrigen Zellen sind Riss-Überlagerungen.
        private const int StoneCellX = 120;
        private const int StoneCellWidth = 40;
        private const int StoneCellHeight = 52;

        /// <summary>Lumiis-Steine haben kein Lauf-Sheet, sondern ein einzelnes Bild.</summary>
        public Sprite LoadStone(string resourceName)
        {
            if (string.IsNullOrEmpty(resourceName)) return null;
            if (_stoneCache.TryGetValue(resourceName, out Sprite cached)) return cached;

            Texture2D texture = LoadTexture(resourceName, false);
            if (texture == null) return null;

            var rect = new Rect(StoneCellX, texture.height - StoneCellHeight, StoneCellWidth, StoneCellHeight);
            Sprite sprite = Sprite.Create(texture, rect, new Vector2(0.5f, 0.08f), PixelsPerUnit);
            _stoneCache[resourceName] = sprite;
            return sprite;
        }

        private Texture2D LoadTexture(string resourceName, bool desaturate)
        {
            Texture2D source = Resources.Load<Texture2D>("Sprites/Monsters/" + resourceName);
            if (source == null || !desaturate) return source;
            if (!source.isReadable) return source;

            // Weiße Tiger: Tiger-Sheet entsättigt und aufgehellt (eigene Kopie, Original bleibt unverändert)
            Color32[] pixels = source.GetPixels32();
            for (int i = 0; i < pixels.Length; i++)
            {
                Color32 p = pixels[i];
                int lum = (p.r * 30 + p.g * 59 + p.b * 11) / 100;
                byte v = (byte)Mathf.Min(255, 40 + lum * 95 / 100);
                pixels[i] = new Color32(v, v, (byte)Mathf.Min(255, v + 8), p.a);
            }
            var copy = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                name = source.name + "_grau"
            };
            copy.SetPixels32(pixels);
            copy.Apply(false, false);
            _ownedTextures.Add(copy);
            return copy;
        }

        public void Dispose()
        {
            foreach (Sprite[] frames in _frameCache.Values)
                foreach (Sprite s in frames)
                    if (s != null) Object.Destroy(s);
            foreach (Sprite s in _stoneCache.Values)
                if (s != null) Object.Destroy(s);
            foreach (Texture2D t in _ownedTextures)
                if (t != null) Object.Destroy(t);
            _frameCache.Clear();
            _stoneCache.Clear();
            _ownedTextures.Clear();
        }
    }
}
