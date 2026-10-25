using UnityEngine;

namespace Lumiis.Client
{
    /// <summary>Trefferfunke am Kontaktpunkt: ein heller Schnittstrich entlang der Klingenrichtung und einige
    /// kurze Funken, die nach außen fliegen und verglühen. Rein kosmetisch, löscht sich selbst.</summary>
    public sealed class ImpactSparkView : MonoBehaviour
    {
        private const int SparkCount = 7;
        private const float Duration = 0.16f;

        private LineRenderer _slash;
        private LineRenderer[] _sparks;
        private Vector3[] _sparkDirections;
        private float[] _sparkSpeeds;
        private Vector3 _origin;
        private Material _material;
        private float _elapsed;
        private Color _color;

        public void Initialize(Vector3 origin, Vector2 slashDirection, Color color, int sortingOrder, int seed)
        {
            _origin = origin;
            _color = color;
            Shader shader = Shader.Find("Sprites/Default");
            _material = shader != null ? new Material(shader) : null;

            if (slashDirection.sqrMagnitude < 0.0001f) slashDirection = Vector2.right;
            slashDirection.Normalize();
            Vector3 slash = new Vector3(slashDirection.x, slashDirection.y, 0f);
            _slash = CreateLine("Slash", 0.09f, sortingOrder + 1);
            _slash.SetPosition(0, origin - slash * 0.45f);
            _slash.SetPosition(1, origin + slash * 0.45f);

            var random = new System.Random(seed);
            _sparks = new LineRenderer[SparkCount];
            _sparkDirections = new Vector3[SparkCount];
            _sparkSpeeds = new float[SparkCount];
            float baseAngle = Mathf.Atan2(slashDirection.y, slashDirection.x);
            for (int i = 0; i < SparkCount; i++)
            {
                // Funken fliegen bevorzugt quer zum Schnitt (wie vom Schwert weggeschlagen).
                float angle = baseAngle + Mathf.PI * 0.5f * (i % 2 == 0 ? 1f : -1f) + (float)(random.NextDouble() - 0.5) * 1.6f;
                _sparkDirections[i] = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f);
                _sparkSpeeds[i] = 3.5f + (float)random.NextDouble() * 3f;
                _sparks[i] = CreateLine("Spark", 0.045f, sortingOrder);
            }
            Apply(0f);
        }

        private LineRenderer CreateLine(string name, float width, int sortingOrder)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var line = go.AddComponent<LineRenderer>();
            line.sharedMaterial = _material;
            line.useWorldSpace = true;
            line.positionCount = 2;
            line.startWidth = width;
            line.endWidth = width * 0.3f;
            line.numCapVertices = 2;
            line.sortingOrder = sortingOrder;
            line.alignment = LineAlignment.View;
            return line;
        }

        private void Update()
        {
            _elapsed += Time.deltaTime;
            if (_elapsed >= Duration)
            {
                Destroy(gameObject);
                return;
            }
            Apply(_elapsed / Duration);
        }

        private void Apply(float t)
        {
            float alpha = 1f - t * t;
            Color core = Color.Lerp(Color.white, _color, t);
            core.a = alpha;
            Color tail = _color;
            tail.a = 0f;

            _slash.startColor = core;
            _slash.endColor = core;
            _slash.widthMultiplier = 1f - t;

            for (int i = 0; i < SparkCount; i++)
            {
                float distance = _sparkSpeeds[i] * Duration * (1f - (1f - t) * (1f - t));
                Vector3 head = _origin + _sparkDirections[i] * (0.1f + distance);
                Vector3 tailPos = _origin + _sparkDirections[i] * (0.1f + distance * 0.55f);
                _sparks[i].SetPosition(0, head);
                _sparks[i].SetPosition(1, tailPos);
                _sparks[i].startColor = core;
                _sparks[i].endColor = tail;
            }
        }

        private void OnDestroy()
        {
            if (_material != null) Destroy(_material);
        }
    }
}
