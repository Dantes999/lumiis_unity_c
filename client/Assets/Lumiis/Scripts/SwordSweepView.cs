using System.Collections.Generic;
using UnityEngine;

namespace Lumiis.Client
{
    /// <summary>Schwertschweif wie in 3D-Actionspielen: ein Band zwischen Klingenansatz und Klingenspitze, das die
    /// letzten Positionen der Klinge verbindet und nach hinten ausblendet. CharacterAnimator liefert pro Frame
    /// eine Probe, solange die Klinge durch den Bogen fährt. Rein kosmetisch.</summary>
    public sealed class SwordSweepView : MonoBehaviour
    {
        private const float SampleLifetime = 0.13f;
        private const int MaxSamples = 24;

        private struct Sample
        {
            public Vector3 Base;
            public Vector3 Tip;
            public float Time;
        }

        private readonly List<Sample> _samples = new List<Sample>();
        private Mesh _mesh;
        private MeshRenderer _renderer;
        private Material _material;
        private readonly List<Vector3> _vertices = new List<Vector3>();
        private readonly List<Color> _colors = new List<Color>();
        private readonly List<int> _triangles = new List<int>();

        public Color CoreColor = new Color(1f, 1f, 1f, 0.9f);
        public Color EdgeColor = new Color(0.62f, 0.82f, 1f, 0.55f);

        public void Initialize(int sortingOrder)
        {
            _mesh = new Mesh { name = "SwordSweep" };
            _mesh.MarkDynamic();
            gameObject.AddComponent<MeshFilter>().sharedMesh = _mesh;
            _renderer = gameObject.AddComponent<MeshRenderer>();
            Shader shader = Shader.Find("Sprites/Default");
            _material = shader != null ? new Material(shader) : null;
            _renderer.sharedMaterial = _material;
            _renderer.sortingOrder = sortingOrder;
        }

        public void AddSample(Vector3 bladeBase, Vector3 bladeTip)
        {
            _samples.Add(new Sample { Base = bladeBase, Tip = bladeTip, Time = Time.time });
            if (_samples.Count > MaxSamples) _samples.RemoveAt(0);
        }

        private void LateUpdate()
        {
            float now = Time.time;
            while (_samples.Count > 0 && now - _samples[0].Time > SampleLifetime) _samples.RemoveAt(0);

            _mesh.Clear();
            if (_samples.Count < 2) return;

            _vertices.Clear();
            _colors.Clear();
            _triangles.Clear();
            int last = _samples.Count - 1;
            for (int i = 0; i <= last; i++)
            {
                Sample sample = _samples[i];
                float age = Mathf.Clamp01((now - sample.Time) / SampleLifetime);
                float along = (float)i / last; // 0 = ältester, 1 = neuester Punkt
                float fade = (1f - age) * along;
                // Innen (Klingenansatz) schwächer, außen (Spitze) hell - das Band wirkt wie ein Lichtbogen.
                Vector3 inner = Vector3.Lerp(sample.Base, sample.Tip, 0.35f);
                _vertices.Add(inner);
                _vertices.Add(sample.Tip);
                Color edge = EdgeColor;
                edge.a *= fade * 0.35f;
                Color core = Color.Lerp(EdgeColor, CoreColor, along);
                core.a *= fade;
                _colors.Add(edge);
                _colors.Add(core);
                if (i == last) continue;
                int v = i * 2;
                _triangles.Add(v); _triangles.Add(v + 1); _triangles.Add(v + 3);
                _triangles.Add(v); _triangles.Add(v + 3); _triangles.Add(v + 2);
                // Rückseite, damit das Band unabhängig von der Schlagrichtung sichtbar ist.
                _triangles.Add(v); _triangles.Add(v + 3); _triangles.Add(v + 1);
                _triangles.Add(v); _triangles.Add(v + 2); _triangles.Add(v + 3);
            }
            _mesh.SetVertices(_vertices);
            _mesh.SetColors(_colors);
            _mesh.SetTriangles(_triangles, 0);
            _mesh.RecalculateBounds();
        }

        private void OnDestroy()
        {
            if (_mesh != null) Destroy(_mesh);
            if (_material != null) Destroy(_material);
        }
    }
}
