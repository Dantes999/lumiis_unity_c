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

        public void Initialize(Sprite sprite, Color color)
        {
            _renderer = gameObject.AddComponent<SpriteRenderer>();
            _renderer.sprite = sprite;
            _renderer.color = color;
        }

        public void SetWorldPosition(double worldX, double worldY)
        {
            transform.position = new Vector3((float)worldX, (float)worldY, 0f);
        }
    }
}
