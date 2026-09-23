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

            // FullRect explizit, da GameClient.CreateFloorAndBoundary drawMode Tiled setzt - das verlangt
            // FullRect statt des sonst impliziten Tight (Warnung sonst zur Laufzeit).
            return Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), pixelsPerUnit,
                0, SpriteMeshType.FullRect);
        }

        /// <summary>Ein Ring (Kreisring), z. B. als Markierung unter den Füßen. Wird per SpriteRenderer.color eingefärbt.</summary>
        public static Sprite CreateRing(int pixelSize = 32, float innerRadiusFraction = 0.65f, int pixelsPerUnit = 48)
        {
            var texture = new Texture2D(pixelSize, pixelSize, TextureFormat.RGBA32, false);
            texture.filterMode = FilterMode.Bilinear;

            Color32 clear = new Color32(0, 0, 0, 0);
            Color32 white = new Color32(255, 255, 255, 255);
            Color32[] pixels = new Color32[pixelSize * pixelSize];

            float center = pixelSize / 2f;
            float outerRadius = pixelSize / 2f;
            float innerRadius = outerRadius * innerRadiusFraction;

            for (int y = 0; y < pixelSize; y++)
            {
                for (int x = 0; x < pixelSize; x++)
                {
                    float dx = x + 0.5f - center;
                    float dy = y + 0.5f - center;
                    float dist = Mathf.Sqrt(dx * dx + dy * dy);
                    pixels[y * pixelSize + x] = (dist <= outerRadius && dist >= innerRadius) ? white : clear;
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply();

            return Sprite.Create(texture, new Rect(0, 0, pixelSize, pixelSize), new Vector2(0.5f, 0.5f), pixelsPerUnit);
        }

        /// <summary>Eine ausgefüllte Kreisscheibe, z. B. als Monster-Platzhalter (seit M3c). Wird per
        /// SpriteRenderer.color eingefärbt.</summary>
        public static Sprite CreateCircle(int pixelSize = 24, int pixelsPerUnit = 24)
        {
            var texture = new Texture2D(pixelSize, pixelSize, TextureFormat.RGBA32, false);
            texture.filterMode = FilterMode.Bilinear;

            Color32 clear = new Color32(0, 0, 0, 0);
            Color32 white = new Color32(255, 255, 255, 255);
            Color32[] pixels = new Color32[pixelSize * pixelSize];

            float center = pixelSize / 2f;
            float radius = pixelSize / 2f;

            for (int y = 0; y < pixelSize; y++)
            {
                for (int x = 0; x < pixelSize; x++)
                {
                    float dx = x + 0.5f - center;
                    float dy = y + 0.5f - center;
                    pixels[y * pixelSize + x] = (Mathf.Sqrt(dx * dx + dy * dy) <= radius) ? white : clear;
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply();

            return Sprite.Create(texture, new Rect(0, 0, pixelSize, pixelSize), new Vector2(0.5f, 0.5f), pixelsPerUnit);
        }

        /// <summary>Ein gleichschenkliges Dreieck (Spitze oben, Basis unten), z. B. als Monster-Platzhalter
        /// (seit M3c). Einfache Halbraumtests pro Zeile statt Barycentric-Koordinaten - für eine grobe
        /// Platzhalterform ausreichend und leichter lesbar. Wird per SpriteRenderer.color eingefärbt.</summary>
        public static Sprite CreateTriangle(int pixelSize = 24, int pixelsPerUnit = 24)
        {
            var texture = new Texture2D(pixelSize, pixelSize, TextureFormat.RGBA32, false);
            texture.filterMode = FilterMode.Bilinear;

            Color32 clear = new Color32(0, 0, 0, 0);
            Color32 white = new Color32(255, 255, 255, 255);
            Color32[] pixels = new Color32[pixelSize * pixelSize];

            float center = pixelSize / 2f;

            for (int y = 0; y < pixelSize; y++)
            {
                float t = y / (float)(pixelSize - 1); // 0 oben (Spitze), 1 unten (Basis)
                float halfWidthAtY = t * (pixelSize / 2f);
                for (int x = 0; x < pixelSize; x++)
                {
                    bool inside = Mathf.Abs(x + 0.5f - center) <= halfWidthAtY;
                    pixels[y * pixelSize + x] = inside ? white : clear;
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply();

            return Sprite.Create(texture, new Rect(0, 0, pixelSize, pixelSize), new Vector2(0.5f, 0.5f), pixelsPerUnit);
        }
    }
}
