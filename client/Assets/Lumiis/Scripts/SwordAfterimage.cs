using UnityEngine;

namespace Lumiis.Client
{
    /// <summary>Kurz sichtbares, verblassendes Nachbild der Waffe während eines Hiebs (Bewegungsunschärfe).
    /// Rein kosmetisch.</summary>
    public sealed class SwordAfterimage : MonoBehaviour
    {
        private SpriteRenderer _renderer;
        private Color _color;
        private float _duration;
        private float _elapsed;

        public void Initialize(SpriteRenderer renderer, Color color, float durationSeconds)
        {
            _renderer = renderer;
            _color = color;
            _duration = Mathf.Max(0.02f, durationSeconds);
            _renderer.color = color;
        }

        private void Update()
        {
            _elapsed += Time.deltaTime;
            if (_elapsed >= _duration)
            {
                Destroy(gameObject);
                return;
            }
            Color c = _color;
            c.a *= 1f - _elapsed / _duration;
            _renderer.color = c;
        }
    }
}
