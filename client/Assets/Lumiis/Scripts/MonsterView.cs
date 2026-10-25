using UnityEngine;

namespace Lumiis.Client
{
    /// <summary>Minimaler Platzhalter für Monster (seit M3c) - anders als CharacterView kein Paper-Doll, keine
    /// Blickrichtung, keine Animation: nur ein SpriteRenderer mit einer je MobId unterschiedlichen Form/Farbe
    /// (siehe GameClient.MonsterShapeFor/MonsterColorFor). Bewegungsinterpolation läuft trotzdem über denselben
    /// ClientWorld-Mechanismus wie bei Spielern (GameClient.Update() ruft SetWorldPosition), keine eigene
    /// Fallunterscheidung dort nötig.</summary>
    public sealed class MonsterView : MonoBehaviour
    {
        private SpriteRenderer _renderer;

        public void Initialize(Sprite sprite, Color color, float uniformScale = 1f)
        {
            _renderer = gameObject.AddComponent<SpriteRenderer>();
            _renderer.sprite = sprite;
            _renderer.color = color;
            transform.localScale = new Vector3(uniformScale, uniformScale, 1f);
        }

        public void SetWorldPosition(double worldX, double worldY)
        {
            transform.position = new Vector3((float)worldX, (float)worldY, 0f);
        }

        /// <summary>Bounds-Check statt echtem Collider-System (M4a, Rechtsklick-Angriff - siehe
        /// GameClient.HandleClickInput) - reicht für Platzhalter-Formen.</summary>
        public bool ContainsWorldPoint(Vector3 worldPos)
        {
            return _renderer != null && _renderer.bounds.Contains(worldPos);
        }
    }
}
