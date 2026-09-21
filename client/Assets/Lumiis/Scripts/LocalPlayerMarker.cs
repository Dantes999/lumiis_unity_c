using UnityEngine;

namespace Lumiis.Client
{
    /// <summary>Markiert den lokalen Spieler zusätzlich zur gelben Namensfarbe: ein Ring am Fußpunkt, immer
    /// knapp hinter den eigenen Charakter-Ebenen sortiert (fester Rang -1, innerhalb der SortingGroup der
    /// CharacterView - siehe dort). Rein kosmetisch, keine Protokolländerung, keine Ebenen-Tönung.</summary>
    public sealed class LocalPlayerMarker : MonoBehaviour
    {
        private const int Rank = -1;
        private static readonly Color RingColor = new Color(1f, 0.85f, 0.2f, 0.9f);

        public void Initialize(Sprite ringSprite)
        {
            SpriteRenderer renderer = gameObject.AddComponent<SpriteRenderer>();
            renderer.sprite = ringSprite;
            renderer.color = RingColor;
            renderer.sortingOrder = Rank;
        }
    }
}
