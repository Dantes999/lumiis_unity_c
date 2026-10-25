using System.Collections.Generic;
using Lumiis.Shared;
using UnityEngine;

namespace Lumiis.Client
{
    /// <summary>Lädt die echten Lumiis-Charakter-Sheets (48×48-Zellen, 9 Spalten) aus Resources/Characters.
    /// Spalten: 0 Stand, 1–2 Schritte, 3 Atmen, 4–6 Angriff, 7 Treffer, 8 Tod. Zeilen: unten/rechts/oben.
    /// Für Ebenen ohne echtes Sheet (Helm/Schild) fällt der Provider auf den Platzhalter zurück.</summary>
    public sealed class LumiisSpriteProvider : ISpriteProvider
    {
        private const int CellPx = 48;
        private const float PixelsPerUnit = 32f;
        private const int FootPx = 4;

        private readonly PlaceholderSpriteProvider _fallback;
        private readonly Dictionary<string, Sprite> _cache = new Dictionary<string, Sprite>();

        public LumiisSpriteProvider(int canvasPx, int ppu)
        {
            _fallback = new PlaceholderSpriteProvider(canvasPx, ppu);
        }

        public Sprite GetSprite(CharacterSlot slot, int lookId, FacingDirection direction, string state, int frame, Vector2 pivotNormalized)
        {
            string resource = ResourceFor(slot, lookId);
            if (resource == null) return _fallback.GetSprite(slot, lookId, direction, state, frame, pivotNormalized);

            Texture2D texture = Resources.Load<Texture2D>(resource);
            if (texture == null) return _fallback.GetSprite(slot, lookId, direction, state, frame, pivotNormalized);

            int row = RowFor(direction);
            int column = ColumnFor(state, frame);
            string key = resource + "|" + row + "|" + column;
            if (_cache.TryGetValue(key, out Sprite cached) && cached != null) return cached;

            float y = texture.height - (row + 1) * CellPx;
            var rect = new Rect(column * CellPx, y, CellPx, CellPx);
            var pivot = new Vector2(0.5f, (float)FootPx / CellPx);
            Sprite sprite = Sprite.Create(texture, rect, pivot, PixelsPerUnit, 0, SpriteMeshType.FullRect);
            _cache[key] = sprite;
            return sprite;
        }

        public Sprite LoadWeaponSprite(string spriteKey)
        {
            return null; // statische Item-Waffen laufen über CachingSpriteProvider/Resources/Sprites/Items.
        }

        /// <summary>Resources-Pfad des Sheets für eine Ebene (null = kein echtes Sheet) - auch für die
        /// UI-Vorschau in der Charakterauswahl (UiSkin.BuildCharacterPortrait).</summary>
        public static string SheetResourceFor(CharacterSlot slot, int lookId) => ResourceFor(slot, lookId);

        private static string ResourceFor(CharacterSlot slot, int lookId)
        {
            switch (slot)
            {
                case CharacterSlot.Body:
                    return "Characters/body/" + BodySheet(lookId);
                case CharacterSlot.Head:
                    return "Characters/head/" + BodySheet(lookId);
                case CharacterSlot.Hair:
                    return "Characters/hair/" + HairSheet(lookId);
                case CharacterSlot.Armor:
                    return ArmorSheet(lookId);
                default:
                    return null; // Helm/Schild/Waffe: Platzhalter, bis echte Sheets importiert sind.
            }
        }

        private static string BodySheet(int lookId)
        {
            switch (lookId)
            {
                case 2: return "warrior_f";
                case 3: return "ninja_m";
                case 4: return "ninja_f";
                case 5: return "sura_m";
                case 6: return "sura_f";
                case 7: return "shaman_m";
                case 8: return "shaman_f";
                default: return "warrior_m";
            }
        }

        private static string HairSheet(int lookId)
        {
            switch (lookId)
            {
                case 2: return "warrior_m_2";
                case 3: return "warrior_f_1";
                default: return "warrior_m_1";
            }
        }

        private static string ArmorSheet(int lookId)
        {
            switch (lookId)
            {
                case 1: return "Characters/armor/06_stoffrobe";
                case 2: return "Characters/armor/05_lederruestung";
                case 3: return "Characters/armor/02_eisenplattenpanzer";
                case 4: return "Characters/armor/01_moenchsplattenpanzer";
                default: return null;
            }
        }

        private static int RowFor(FacingDirection direction)
        {
            switch (direction)
            {
                case FacingDirection.Up: return 2;
                case FacingDirection.Side: return 1;
                default: return 0;
            }
        }

        private static int ColumnFor(string state, int frame)
        {
            switch (state)
            {
                case "walk": return 1 + Mathf.Abs(frame) % 2;
                case "attack": return 4 + Mathf.Clamp(frame, 0, 2);
                case "dead": return 8;
                default: return 0;
            }
        }
    }
}
