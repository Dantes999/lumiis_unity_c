using UnityEngine;

namespace Lumiis.Client
{
    /// <summary>combat-visuals Abschnitt 3: kurzzeitiger, groesserer Ring (RadiusWire) beim Ausloesen von
    /// Kreisschlag - blendet ein und wieder aus (Deckkraft-Dreieck ueber die Lebenszeit), entfernt sich
    /// danach selbst. Optimistisch beim Senden erzeugt (siehe GameClient.UseSkillHotkey-Kommentar), keine
    /// Positionsverfolgung noetig (so kurzlebig, dass eine Bewegung waehrenddessen vernachlaessigbar ist).</summary>
    public sealed class RadiusFlashView : MonoBehaviour
    {
        private const int SortingOrder = -35;

        private SpriteRenderer _renderer;
        private float _elapsed;
        private float _durationSeconds;
        private float _maxAlpha;

        public void Initialize(Sprite unitRingSprite, Vector3 worldPosition, float diameterWorldUnits, Color color, float durationSeconds)
        {
            transform.position = worldPosition;
            transform.localScale = new Vector3(diameterWorldUnits, diameterWorldUnits, 1f);

            _renderer = gameObject.AddComponent<SpriteRenderer>();
            _renderer.sprite = unitRingSprite;
            _renderer.sortingOrder = SortingOrder;
            _maxAlpha = color.a;
            _renderer.color = color;
            _durationSeconds = durationSeconds;
        }

        private void Update()
        {
            _elapsed += Time.deltaTime;
            if (_elapsed >= _durationSeconds)
            {
                Destroy(gameObject);
                return;
            }

            float t = _elapsed / _durationSeconds;
            // Erste Haelfte einblenden, zweite Haelfte ausblenden.
            float alphaFactor = t < 0.5f ? t / 0.5f : 1f - (t - 0.5f) / 0.5f;
            Color c = _renderer.color;
            c.a = _maxAlpha * alphaFactor;
            _renderer.color = c;
        }
    }
}
