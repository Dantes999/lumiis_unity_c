using System.Collections.Generic;
using Lumiis.Shared;
using UnityEngine;

namespace Lumiis.Client
{
    /// <summary>Cacht Sprites nach (Slot, LookId, Facing, State, Frame). Als Instanz an einen GameClient
    /// gebunden (keine statische Variable) - DestroyAll räumt beim Zerstören des GameClient alle gecachten
    /// Sprites UND ihre zugrunde liegenden Texturen auf (beide werden sonst nicht automatisch freigegeben,
    /// siehe Review-Befund zum fehlenden Caching/Cleanup).</summary>
    public sealed class CachingSpriteProvider : ISpriteProvider
    {
        private readonly ISpriteProvider _inner;

        private readonly Dictionary<(CharacterSlot Slot, int LookId, FacingDirection Direction, string State, int Frame), Sprite> _cache
            = new Dictionary<(CharacterSlot, int, FacingDirection, string, int), Sprite>();

        /// <summary>Getrennt vom Slot-Cache: Waffen-PNGs werden nach spriteKey (nicht nach LookId) gecacht.
        /// Geladene Assets stammen aus Resources und werden von Unity selbst verwaltet - DestroyAll rührt sie
        /// deshalb bewusst nicht an (nur die zur Laufzeit erzeugten Platzhalter-Texturen).</summary>
        private readonly Dictionary<string, Sprite> _weaponCache = new Dictionary<string, Sprite>(System.StringComparer.Ordinal);

        private const string WeaponResourcePath = "Sprites/Items/";

        public CachingSpriteProvider(ISpriteProvider inner)
        {
            _inner = inner;
        }

        public int Count => _cache.Count + _weaponCache.Count;

        public Sprite GetSprite(CharacterSlot slot, int lookId, FacingDirection direction, string state, int frame, Vector2 pivotNormalized)
        {
            var key = (slot, lookId, direction, state, frame);
            if (_cache.TryGetValue(key, out Sprite cached) && cached != null)
                return cached;

            Sprite sprite = _inner.GetSprite(slot, lookId, direction, state, frame, pivotNormalized);
            _cache[key] = sprite;
            return sprite;
        }

        /// <summary>Lädt das statische Waffen-PNG gecacht aus Resources/Sprites/Items/. Ein leerer Schlüssel
        /// oder ein fehlendes Asset ergibt null (CharacterView nutzt dann den Platzhalter); auch dieses
        /// Ergebnis wird gecacht, damit bei jedem Ausrüsten kein erneuter Resources-Lookup nötig ist.</summary>
        public Sprite LoadWeaponSprite(string spriteKey)
        {
            if (string.IsNullOrWhiteSpace(spriteKey)) return null;
            if (_weaponCache.TryGetValue(spriteKey, out Sprite cached)) return cached;

            Sprite sprite = _inner.LoadWeaponSprite(spriteKey);
            if (sprite == null)
                sprite = Resources.Load<Sprite>(WeaponResourcePath + spriteKey);

            _weaponCache[spriteKey] = sprite;
            return sprite;
        }

        public void DestroyAll()
        {
            foreach (Sprite sprite in _cache.Values)
            {
                if (sprite == null) continue;
                Texture2D texture = sprite.texture;
                Object.Destroy(sprite);
                // Nur die vom Placeholder-Provider erzeugten Texturen gehören uns. Lumiis-Charakter-Sheets
                // sind Resources-Assets und werden von Unity verwaltet.
                if (texture != null && texture.name == "__runtime") Object.Destroy(texture);
            }
            _cache.Clear();

            // Weapon sprites are Resources assets (or null cache entries) - Unity owns their lifetime.
            _weaponCache.Clear();
        }
    }
}
