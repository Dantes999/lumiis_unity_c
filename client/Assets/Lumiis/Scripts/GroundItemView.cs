using UnityEngine;

namespace Lumiis.Client
{
    /// <summary>Boden-Drop: Icon mit Schatten, darüber ein gut lesbares Namensschild (Name + "+N", Farbe nach
    /// Aufwertungsstufe). Anklickbar fürs Aufheben (GameClient.PickupItem); beim Überfahren mit der Maus wird
    /// das Icon hervorgehoben.</summary>
    public sealed class GroundItemView : MonoBehaviour
    {
        private const float BaseScale = 0.55f;
        private SpriteRenderer _renderer;
        private TextMesh _label;
        private TextMesh _labelShadow;
        private bool _highlighted;

        public void Initialize(Sprite icon, string itemName, int upgradeLevel)
        {
            _renderer = gameObject.AddComponent<SpriteRenderer>();
            _renderer.sprite = icon;
            _renderer.sortingOrder = 5;
            transform.localScale = new Vector3(BaseScale, BaseScale, 1f);

            var shadowGo = new GameObject("Shadow");
            shadowGo.transform.SetParent(transform, false);
            shadowGo.transform.localPosition = new Vector3(0f, -0.15f, 0f);
            var shadow = shadowGo.AddComponent<SpriteRenderer>();
            shadow.sprite = PlaceholderSprites.CreateSquare();
            shadow.color = new Color(0f, 0f, 0f, 0.35f);
            shadow.sortingOrder = 4;
            shadow.transform.localScale = new Vector3(1.4f, 0.35f, 1f);

            if (string.IsNullOrEmpty(itemName)) return;
            string text = upgradeLevel > 0 ? itemName + " +" + upgradeLevel : itemName;
            _labelShadow = CreateLabel("LabelShadow", text, new Color(0f, 0f, 0f, 0.9f), new Vector3(0.05f, 2.15f, 0f), 59);
            _label = CreateLabel("Label", text, ColorForUpgrade(upgradeLevel), new Vector3(0f, 2.2f, 0f), 60);
        }

        private TextMesh CreateLabel(string objectName, string text, Color color, Vector3 localPosition, int sortingOrder)
        {
            var labelGo = new GameObject(objectName);
            labelGo.transform.SetParent(transform, false);
            labelGo.transform.localPosition = localPosition;
            var label = labelGo.AddComponent<TextMesh>();
            label.text = text;
            label.characterSize = 0.09f;
            label.fontSize = 48;
            label.fontStyle = FontStyle.Bold;
            label.anchor = TextAnchor.MiddleCenter;
            label.color = color;
            MeshRenderer labelRenderer = labelGo.GetComponent<MeshRenderer>();
            if (labelRenderer != null) labelRenderer.sortingOrder = sortingOrder;
            return label;
        }

        /// <summary>Farbstufen wie bei klassischen MMOs: normal hell, aufgewertet blau, +7/+8 gold, +9 orange.</summary>
        private static Color ColorForUpgrade(int upgradeLevel)
        {
            if (upgradeLevel >= 9) return new Color(1f, 0.62f, 0.25f);
            if (upgradeLevel >= 7) return new Color(1f, 0.86f, 0.35f);
            if (upgradeLevel >= 1) return new Color(0.55f, 0.85f, 1f);
            return new Color(0.97f, 0.93f, 0.82f);
        }

        public void SetHighlighted(bool highlighted)
        {
            if (_highlighted == highlighted || _renderer == null) return;
            _highlighted = highlighted;
            float scale = highlighted ? BaseScale * 1.2f : BaseScale;
            transform.localScale = new Vector3(scale, scale, 1f);
            _renderer.color = highlighted ? new Color(1f, 1f, 0.8f) : Color.white;
            if (_label != null) _label.fontStyle = highlighted ? FontStyle.BoldAndItalic : FontStyle.Bold;
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
