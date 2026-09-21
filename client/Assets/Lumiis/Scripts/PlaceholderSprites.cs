using UnityEngine;

namespace Lumiis.Client
{
    /// <summary>Erzeugt zur Laufzeit einfache Platzhalter-Sprites (keine Kunst-Assets nötig).</summary>
    internal static class PlaceholderSprites
    {
        /// <summary>Ein einfarbiges Quadrat, exakt 1 Welteinheit groß (wird per SpriteRenderer.color eingefärbt).</summary>
        public static Sprite CreateSquare(int pixelSize = 8)
        {
            var texture = new Texture2D(pixelSize, pixelSize, TextureFormat.RGBA32, false);
            texture.filterMode = FilterMode.Point;

            Color32 white = new Color32(255, 255, 255, 255);
            Color32[] pixels = new Color32[pixelSize * pixelSize];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = white;
            texture.SetPixels32(pixels);
            texture.Apply();

            return Sprite.Create(texture, new Rect(0, 0, pixelSize, pixelSize), new Vector2(0.5f, 0.5f), pixelSize);
        }

        /// <summary>Eine 2x2-Schachbrett-Kachel; im SpriteRenderer.drawMode Tiled ergibt sie ein Raster mit
        /// cellUnits großen Zellen (Default 10 Welteinheiten).</summary>
        public static Sprite CreateCheckerTile(int cellPixels = 40, int pixelsPerUnit = 4)
        {
            int size = cellPixels * 2;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            texture.filterMode = FilterMode.Point;
            texture.wrapMode = TextureWrapMode.Repeat;

            Color32 colorA = new Color32(58, 58, 64, 255);
            Color32 colorB = new Color32(46, 46, 51, 255);
            Color32[] pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                bool cellY = y < cellPixels;
                for (int x = 0; x < size; x++)
                {
                    bool cellX = x < cellPixels;
                    pixels[y * size + x] = (cellX == cellY) ? colorA : colorB;
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply();

            return Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), pixelsPerUnit);
        }
    }
}
