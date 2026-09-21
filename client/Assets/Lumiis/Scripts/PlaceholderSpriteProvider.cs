using Lumiis.Shared;
using UnityEngine;

namespace Lumiis.Client
{
    /// <summary>Erzeugt zur Laufzeit farbige Platzhalter-Formen für jede Ebene (keine Kunst-Assets nötig).
    /// Alle Sprites haben dieselbe Canvas-Größe, damit die Ebenen deckungsgleich übereinanderliegen
    /// (siehe docs/sprite-spec.md). Formen unterscheiden sich leicht je Richtung.</summary>
    public sealed class PlaceholderSpriteProvider : ISpriteProvider
    {
        private readonly int _canvasPx;
        private readonly int _ppu;

        public PlaceholderSpriteProvider(int canvasPx, int ppu)
        {
            _canvasPx = canvasPx > 0 ? canvasPx : 128;
            _ppu = ppu > 0 ? ppu : 64;
        }

        private int CenterX => _canvasPx / 2;

        public Sprite GetSprite(CharacterSlot slot, int lookId, FacingDirection direction, string state, int frame, Vector2 pivotNormalized)
        {
            Color32[] pixels = new Color32[_canvasPx * _canvasPx];
            Color32 clear = new Color32(0, 0, 0, 0);
            for (int i = 0; i < pixels.Length; i++) pixels[i] = clear;

            switch (slot)
            {
                case CharacterSlot.Body: DrawBody(pixels, direction); break;
                case CharacterSlot.Armor: DrawArmor(pixels, direction, lookId); break;
                case CharacterSlot.Shield: DrawShield(pixels, direction); break;
                case CharacterSlot.Weapon: DrawWeapon(pixels, direction, lookId); break;
                case CharacterSlot.Hair: DrawHair(pixels, direction); break;
                case CharacterSlot.Helmet: DrawHelmet(pixels, direction); break;
            }

            var texture = new Texture2D(_canvasPx, _canvasPx, TextureFormat.RGBA32, false);
            texture.filterMode = FilterMode.Point;
            texture.SetPixels32(pixels);
            texture.Apply();

            return Sprite.Create(texture, new Rect(0, 0, _canvasPx, _canvasPx), pivotNormalized, _ppu);
        }

        private void FillRect(Color32[] pixels, int x0, int y0, int width, int height, Color32 color)
        {
            for (int y = y0; y < y0 + height; y++)
            {
                if (y < 0 || y >= _canvasPx) continue;
                int row = y * _canvasPx;
                for (int x = x0; x < x0 + width; x++)
                {
                    if (x < 0 || x >= _canvasPx) continue;
                    pixels[row + x] = color;
                }
            }
        }

        private void FillCircle(Color32[] pixels, int cx, int cy, int radius, Color32 color)
        {
            for (int y = -radius; y <= radius; y++)
            {
                int py = cy + y;
                if (py < 0 || py >= _canvasPx) continue;
                int row = py * _canvasPx;
                for (int x = -radius; x <= radius; x++)
                {
                    if (x * x + y * y > radius * radius) continue;
                    int px = cx + x;
                    if (px < 0 || px >= _canvasPx) continue;
                    pixels[row + px] = color;
                }
            }
        }

        private void DrawBody(Color32[] pixels, FacingDirection direction)
        {
            Color32 skin = new Color32(210, 170, 130, 255);
            int torsoWidth = direction == FacingDirection.Side ? _canvasPx / 6 : _canvasPx / 4;
            FillRect(pixels, CenterX - torsoWidth / 2, _canvasPx / 5, torsoWidth, _canvasPx * 2 / 5, skin);
            FillCircle(pixels, CenterX, _canvasPx * 4 / 5, _canvasPx / 8, skin);
        }

        private void DrawArmor(Color32[] pixels, FacingDirection direction, int lookId)
        {
            Color32 armorColor = ArmorColorForId(lookId);
            int width = direction == FacingDirection.Side ? _canvasPx / 5 : _canvasPx / 3;
            FillRect(pixels, CenterX - width / 2, _canvasPx / 5, width, _canvasPx * 2 / 5, armorColor);
        }

        private static Color32 ArmorColorForId(int lookId)
        {
            switch (lookId)
            {
                case 1: return new Color32(170, 140, 100, 255); // Stoff
                case 2: return new Color32(120, 90, 60, 255);   // Leder
                case 3: return new Color32(140, 140, 150, 255); // Kette
                case 4: return new Color32(180, 180, 190, 255); // Platte
                default: return new Color32(150, 150, 150, 255);
            }
        }

        private void DrawHair(Color32[] pixels, FacingDirection direction)
        {
            // Weiß, wird von CharacterView per SpriteRenderer.color aus der Palette eingefärbt.
            Color32 hairBase = new Color32(255, 255, 255, 255);
            FillCircle(pixels, CenterX, _canvasPx * 7 / 8, _canvasPx / 9, hairBase);
        }

        private void DrawHelmet(Color32[] pixels, FacingDirection direction)
        {
            Color32 metal = new Color32(120, 120, 130, 255);
            FillCircle(pixels, CenterX, _canvasPx * 7 / 8, _canvasPx / 8, metal);
        }

        private void DrawWeapon(Color32[] pixels, FacingDirection direction, int lookId)
        {
            Color32 blade = new Color32(200, 200, 210, 255);
            int handX = CenterX + (direction == FacingDirection.Side ? _canvasPx / 10 : _canvasPx / 6);
            int handY = _canvasPx * 2 / 5;
            int length = _canvasPx * 2 / 5;
            FillRect(pixels, handX - 2, handY, 4, length, blade);

            if (lookId == 3) // Axt: zusätzlicher Klingenkopf oben
                FillRect(pixels, handX - 8, handY + length - 10, 16, 12, blade);
        }

        private void DrawShield(Color32[] pixels, FacingDirection direction)
        {
            Color32 wood = new Color32(110, 80, 50, 255);
            int offsetX = direction == FacingDirection.Side ? _canvasPx / 3 : _canvasPx / 4;
            FillCircle(pixels, CenterX - offsetX, _canvasPx * 2 / 5, _canvasPx / 8, wood);
        }
    }
}
