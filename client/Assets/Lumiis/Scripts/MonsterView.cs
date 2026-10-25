using Lumiis.Shared;
using UnityEngine;

namespace Lumiis.Client
{
    /// <summary>Monster-Ansicht: animiertes Referenz-Sprite (Wolf/Boar/Dummy) oder ein Platzhalter-Sprite,
    /// plus kleiner Lebensbalken. Blickrichtung/Frames werden aus dem ClientWorld-Zustand abgeleitet; die
    /// eigentliche Bewegung interpoliert weiterhin ClientWorld (GameClient ruft SetWorldPosition).</summary>
    public sealed class MonsterView : MonoBehaviour
    {
        private SpriteRenderer _renderer;
        private Sprite[] _frames;
        private float _uniformScale = 1f;

        private SpriteRenderer _hpBackground;
        private SpriteRenderer _hpFill;
        private const float HpBarWidth = 0.9f;
        private const float HpBarY = 1.18f;

        public void Initialize(Sprite[] frames, Color color, float uniformScale = 1f)
        {
            _frames = frames;
            _uniformScale = uniformScale;
            _renderer = gameObject.AddComponent<SpriteRenderer>();
            _renderer.color = color;
            _renderer.sortingOrder = 0;
            // Sortierung am Fußpunkt (wie Bäume/Häuser im TerrainRenderer), damit Monster hinter Bäumen verschwinden
            _renderer.spriteSortPoint = SpriteSortPoint.Pivot;
            transform.localScale = new Vector3(uniformScale, uniformScale, 1f);

            if (frames != null && frames.Length > 0) _renderer.sprite = frames[0];
            CreateHealthBar();
        }

        /// <summary>Fallback für Sonderfälle (Stein/Boss/unbekannte MobId) - gleiche Optik wie vorher.</summary>
        public void Initialize(Sprite sprite, Color color, float uniformScale = 1f)
        {
            Initialize(frames: null, color, uniformScale);
            _renderer.sprite = sprite;
        }

        private void CreateHealthBar()
        {
            _hpBackground = CreateBar("MonsterHpBackground", new Color(0.08f, 0.07f, 0.07f, 0.85f), 30);
            _hpFill = CreateBar("MonsterHpFill", new Color(0.38f, 0.78f, 0.32f, 1f), 31);
            _hpBackground.enabled = false;
            _hpFill.enabled = false;
        }

        private SpriteRenderer CreateBar(string name, Color color, int sortingOrder)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = PlaceholderSprites.CreateSquare();
            renderer.color = color;
            renderer.sortingOrder = sortingOrder;
            return renderer;
        }

        public void SetWorldPosition(double worldX, double worldY)
        {
            transform.position = new Vector3((float)worldX, (float)worldY, 0f);
        }

        /// <summary>Setzt Blickrichtung und Lauf-Frame. Die vier Sheet-Zeilen sind unten/links/rechts/oben;
        /// bei Side+FlipX (links) nehmen wir die linke Zeile, sonst die rechte.</summary>
        public void SetPose(Facing facing, bool moving, float time)
        {
            if (_frames == null || _frames.Length < 20) return;

            int row;
            if (facing.Direction == FacingDirection.Down) row = 0;
            else if (facing.Direction == FacingDirection.Up) row = 3;
            else row = facing.FlipX ? 1 : 2;

            int frame = moving ? Mathf.Abs(Mathf.FloorToInt(time * 8f)) % 4 : 0;
            _renderer.sprite = _frames[row * 5 + frame];
        }

        public void SetHealth(int current, int max)
        {
            if (_hpBackground == null || _hpFill == null) return;

            bool show = max > 0 && current > 0 && current < max;
            _hpBackground.enabled = show;
            _hpFill.enabled = show;
            if (!show) return;

            float fraction = Mathf.Clamp01((float)current / max);
            float y = HpBarY / Mathf.Max(0.01f, _uniformScale);
            _hpBackground.transform.localPosition = new Vector3(0f, y, 0f);
            _hpBackground.transform.localScale = new Vector3(HpBarWidth + 0.06f, 0.09f, 1f);

            _hpFill.transform.localPosition = new Vector3(-(HpBarWidth * (1f - fraction)) / 2f, y, 0f);
            _hpFill.transform.localScale = new Vector3(HpBarWidth * fraction, 0.055f, 1f);
        }

        /// <summary>Bounds-Check statt echtem Collider-System (M4a, Rechtsklick-Angriff - siehe
        /// GameClient.HandleClickInput) - reicht für Sprite-Figuren.</summary>
        /// <summary>Oberkante der Monstergrafik in Weltkoordinaten (für Namensschild/Schadenszahlen).</summary>
        public float TopWorldY => _renderer != null ? Mathf.Max(_renderer.bounds.max.y, transform.position.y + HpBarY + 0.1f) : transform.position.y + HpBarY + 0.1f;

        public bool ContainsWorldPoint(Vector3 worldPos)
        {
            return _renderer != null && _renderer.bounds.Contains(worldPos);
        }
    }
}
