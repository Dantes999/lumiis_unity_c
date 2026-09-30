using UnityEngine;

namespace Lumiis.Client
{
    /// <summary>combat-visuals Abschnitt 4: kurzzeitige Linie zwischen Angreifer- und Zielposition bei
    /// jedem EntityDamaged/EntityDodged - verblasst linear, entfernt sich danach selbst. Mehrere gleichzeitig
    /// aktive Instanzen sind unabhängig voneinander (je ein eigenes GameObject, kein globaler Singleton-Zustand -
    /// siehe GameClient.SpawnHitLine, das pro Paket eine neue Instanz erzeugt).</summary>
    public sealed class HitLineView : MonoBehaviour
    {
        private const int SortingOrder = 50; // über allen Figuren (CharacterView-SortingGroup liegt immer bei 0).
        private const float LineWidth = 0.06f;

        private LineRenderer _line;
        private Color _color;
        private float _elapsed;
        private float _durationSeconds;

        public void Initialize(Vector3 from, Vector3 to, Color color, float durationSeconds)
        {
            _line = gameObject.AddComponent<LineRenderer>();
            _line.material = new Material(Shader.Find("Sprites/Default"));
            _line.useWorldSpace = true;
            _line.positionCount = 2;
            _line.SetPosition(0, from);
            _line.SetPosition(1, to);
            _line.startWidth = LineWidth;
            _line.endWidth = LineWidth;
            _line.sortingOrder = SortingOrder;

            _color = color;
            _line.startColor = color;
            _line.endColor = color;
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

            float alphaFactor = 1f - _elapsed / _durationSeconds;
            Color c = _color;
            c.a = _color.a * alphaFactor;
            _line.startColor = c;
            _line.endColor = c;
        }
    }
}
