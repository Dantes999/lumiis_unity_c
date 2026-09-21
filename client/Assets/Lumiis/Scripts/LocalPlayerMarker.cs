using UnityEngine;

namespace Lumiis.Client
{
    /// <summary>Markiert den lokalen Spieler zusätzlich zur gelben Namensfarbe: ein Ring am Fußpunkt, immer
    /// knapp hinter den eigenen Charakter-Ebenen sortiert. Rein kosmetisch, keine Protokolländerung, keine
    /// Ebenen-Tönung.</summary>
    public sealed class LocalPlayerMarker : MonoBehaviour
    {
        private static readonly Color RingColor = new Color(1f, 0.85f, 0.2f, 0.9f);

        private SpriteRenderer _renderer;

        public void Initialize(Sprite ringSprite)
        {
            _renderer = gameObject.AddComponent<SpriteRenderer>();
            _renderer.sprite = ringSprite;
            _renderer.color = RingColor;
        }

        private void LateUpdate()
        {
            // Dieselbe Basis-Formel wie CharacterView.ApplySortingOrder, Rang -1: immer knapp hinter der
            // eigenen Figur (Rang 0-5), unabhängig von der aktuellen Facing-Zeichenreihenfolge.
            _renderer.sortingOrder = -Mathf.RoundToInt(transform.position.y * 100f) * 10 - 1;
        }
    }
}
