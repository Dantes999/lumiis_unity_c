using UnityEngine;

namespace Lumiis.Client
{
    /// <summary>Technische Boden-Drop-Darstellung: Icon + kleiner Name, anklickbar fürs Aufheben. Die
    /// eigentliche UI (Loot-Fenster/Tooltip) kann diese View später ersetzen/erweitern.</summary>
    public sealed class GroundItemView : MonoBehaviour
    {
        private SpriteRenderer _renderer;

        public void Initialize(Sprite icon, string itemName)
        {
            _renderer = gameObject.AddComponent<SpriteRenderer>();
            _renderer.sprite = icon;
            _renderer.sortingOrder = 5;
            transform.localScale = new Vector3(0.55f, 0.55f, 1f);

            var shadowGo = new GameObject("Shadow");
            shadowGo.transform.SetParent(transform, false);
            shadowGo.transform.localPosition = new Vector3(0f, -0.15f, 0f);
            var shadow = shadowGo.AddComponent<SpriteRenderer>();
            shadow.sprite = PlaceholderSprites.CreateSquare();
            shadow.color = new Color(0f, 0f, 0f, 0.35f);
            shadow.sortingOrder = 4;
            shadow.transform.localScale = new Vector3(1.4f, 0.35f, 1f);

            if (!string.IsNullOrEmpty(itemName))
            {
                var labelGo = new GameObject("Label");
                labelGo.transform.SetParent(transform, false);
                labelGo.transform.localPosition = new Vector3(0f, 2.2f, 0f);
                var label = labelGo.AddComponent<TextMesh>();
                label.text = itemName;
                label.characterSize = 0.08f;
                label.fontSize = 32;
                label.anchor = TextAnchor.MiddleCenter;
                label.color = new Color(0.95f, 0.88f, 0.65f);
                MeshRenderer labelRenderer = labelGo.GetComponent<MeshRenderer>();
                if (labelRenderer != null) labelRenderer.sortingOrder = 60;
            }
        }

        public void SetWorldPosition(double x, double y)
        {
            transform.position = new Vector3((float)x, (float)y, 0f);
        }

        public bool ContainsWorldPoint(Vector3 worldPos)
        {
            return _renderer != null && _renderer.bounds.Contains(worldPos);
        }
    }
}
