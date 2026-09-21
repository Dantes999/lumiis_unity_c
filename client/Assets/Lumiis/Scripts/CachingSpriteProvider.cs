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

        public CachingSpriteProvider(ISpriteProvider inner)
        {
            _inner = inner;
        }

        public int Count => _cache.Count;

        public Sprite GetSprite(CharacterSlot slot, int lookId, FacingDirection direction, string state, int frame, Vector2 pivotNormalized)
        {
            var key = (slot, lookId, direction, state, frame);
            if (_cache.TryGetValue(key, out Sprite cached) && cached != null)
                return cached;

            Sprite sprite = _inner.GetSprite(slot, lookId, direction, state, frame, pivotNormalized);
            _cache[key] = sprite;
            return sprite;
        }

        public void DestroyAll()
        {
            foreach (Sprite sprite in _cache.Values)
            {
                if (sprite == null) continue;
                Texture2D texture = sprite.texture;
                Object.Destroy(sprite);
                if (texture != null) Object.Destroy(texture);
            }
            _cache.Clear();
        }
    }
}
