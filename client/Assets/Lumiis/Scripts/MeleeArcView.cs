using UnityEngine;

namespace Lumiis.Client
{
    /// <summary>Kurz sichtbarer Schlagbogen in Blickrichtung. Zeigt Reichweite und Richtung des normalen
    /// Angriffs, damit man ohne Debug-Overlay sieht, wo der Schlag trifft. Rein kosmetisch; die Trefferlogik
    /// bleibt im Server (AttackRequest/TryAttack).</summary>
    public sealed class MeleeArcView : MonoBehaviour
    {
        private const int SortingOrder = 60;
        private const int Segments = 18;
        private const float HalfAngleDegrees = 52f;
        private const float LineWidth = 0.11f;

        private LineRenderer _line;
        private Color _color;
        private float _elapsed;
        private float _duration;

        public void Initialize(Vector3 origin, Vector2 direction, float radius, Color color, float durationSeconds)
        {
            if (direction.sqrMagnitude < 0.0001f) direction = Vector2.down;
            direction.Normalize();

            _line = gameObject.AddComponent<LineRenderer>();
            _line.material = new Material(Shader.Find("Sprites/Default"));
            _line.useWorldSpace = true;
            _line.positionCount = Segments + 1;
            _line.startWidth = LineWidth;
            _line.endWidth = LineWidth;
            _line.numCornerVertices = 2;
            _line.numCapVertices = 2;
            _line.sortingOrder = SortingOrder;
            _line.textureMode = LineTextureMode.Stretch;
            _line.alignment = LineAlignment.View;

            float baseAngle = Mathf.Atan2(direction.y, direction.x);
            float half = HalfAngleDegrees * Mathf.Deg2Rad;
            for (int i = 0; i <= Segments; i++)
            {
                float angle = baseAngle - half + (2f * half) * i / Segments;
                Vector3 point = origin + new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f) * radius;
                _line.SetPosition(i, point);
            }

            _color = color;
            _duration = Mathf.Max(0.05f, durationSeconds);
            ApplyAlpha(1f);
        }

        private void Update()
        {
            _elapsed += Time.deltaTime;
            if (_elapsed >= _duration)
            {
                Destroy(gameObject);
                return;
            }
            ApplyAlpha(1f - _elapsed / _duration);
        }

        private void ApplyAlpha(float factor)
        {
            if (_line == null) return;
            Color c = _color;
            c.a *= Mathf.Clamp01(factor);
            _line.startColor = c;
            _line.endColor = c;
        }

        private void OnDestroy()
        {
            if (_line != null && _line.material != null) Destroy(_line.material);
        }
    }
}
