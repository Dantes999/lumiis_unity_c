using UnityEngine;

namespace Lumiis.Client
{
    /// <summary>Visualisiert eine ClientEntity als farbiges Quadrat.</summary>
    public sealed class EntityView : MonoBehaviour
    {
        public int EntityId { get; private set; }
        public string EntityName { get; private set; }

        private SpriteRenderer _renderer;

        public void Initialize(int entityId, string entityName, Color color, Sprite squareSprite)
        {
            EntityId = entityId;
            EntityName = entityName;

            _renderer = gameObject.AddComponent<SpriteRenderer>();
            _renderer.sprite = squareSprite;
            _renderer.color = color;
            _renderer.sortingOrder = 0;

            transform.localScale = Vector3.one * 0.8f; // etwas kleiner als 1 Einheit, sichtbare Lücke zum Raster
        }

        public void SetWorldPosition(double worldX, double worldY)
        {
            transform.position = new Vector3((float)worldX, (float)worldY, 0f);
        }
    }
}
