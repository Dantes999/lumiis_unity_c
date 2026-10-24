#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Lumiis.Client
{
    /// <summary>Zeigt die acht Lumiis-Charakter-Sheets (Krieger/Ninja/Sura/Schamane, m/w) als
    /// Paper-Doll-Vorschau. Die Sheets liegen unter Resources/Characters/{body,head,hair,armor,weapon};
    /// jede Zelle ist 48x48, der Fußpunkt liegt bei (24,44), Spalte 0 ist Idle, Zeile 0/1/2 = unten/rechts/oben.
    /// Nur für das Editor-Testlabor gedacht.</summary>
    public sealed class CharacterSheetGallery : MonoBehaviour
    {
        private const int CellPx = 48;
        private const int FootX = 24;
        private const int FootYFromBottom = 4; // 48 - 44 (Bildkoordinate von oben)
        private const float PixelsPerUnit = 32f;

        private sealed class Preset
        {
            public string Name;
            public string Body;
            public string Head;
            public string Hair;
            public string Armor;
            public string Weapon;
        }

        private static readonly Preset[] Presets =
        {
            new Preset { Name = "Krieger M", Body = "warrior_m", Head = "warrior_m", Hair = "warrior_m_1", Armor = "01_moenchsplattenpanzer", Weapon = "schwert_02" },
            new Preset { Name = "Krieger F", Body = "warrior_f", Head = "warrior_f", Hair = "warrior_f_1", Armor = "02_eisenplattenpanzer", Weapon = "schwert_04" },
            new Preset { Name = "Ninja M", Body = "ninja_m", Head = "ninja_m", Hair = "ninja_m_1", Armor = "05_lederruestung", Weapon = "dolch_01" },
            new Preset { Name = "Ninja F", Body = "ninja_f", Head = "ninja_f", Hair = "ninja_f_1", Armor = "05_lederruestung", Weapon = "bogen_05" },
            new Preset { Name = "Sura M", Body = "sura_m", Head = "sura_m", Hair = "sura_m_1", Armor = "03_tiger_plattenpanzer", Weapon = "zweihand_03" },
            new Preset { Name = "Sura F", Body = "sura_f", Head = "sura_f", Hair = "sura_f_1", Armor = "04_loewenplattenpanzer", Weapon = "schwert_06" },
            new Preset { Name = "Schamane M", Body = "shaman_m", Head = "shaman_m", Hair = "shaman_m_1", Armor = "06_stoffrobe", Weapon = "glocke_10" },
            new Preset { Name = "Schamane F", Body = "shaman_f", Head = "shaman_f", Hair = "shaman_f_1", Armor = "06_stoffrobe", Weapon = "faecher_01" },
        };

        private readonly List<Sprite> _createdSprites = new List<Sprite>();

        public void Build(Vector3 center)
        {
            const float spacingX = 2.1f;
            const float spacingY = 1.85f;

            for (int i = 0; i < Presets.Length; i++)
            {
                int col = i % 4;
                int row = i / 4;
                Vector3 position = center + new Vector3((col - 1.5f) * spacingX, (0.6f - row * spacingY), 0f);
                CreateFigure(Presets[i], position);
            }
        }

        private void CreateFigure(Preset preset, Vector3 position)
        {
            var root = new GameObject("Sheet_" + preset.Name);
            root.transform.SetParent(transform, false);
            root.transform.position = position;

            var sortingGroup = root.AddComponent<SortingGroup>();
            sortingGroup.sortingOrder = 0;

            AddLayer(root.transform, "Body", "Characters/body/" + preset.Body, 0, 0, 0);
            AddLayer(root.transform, "Armor", "Characters/armor/" + preset.Armor, 1, 0, 1);
            AddLayer(root.transform, "Head", "Characters/head/" + preset.Head, 2, 0, 2);
            AddLayer(root.transform, "Hair", "Characters/hair/" + preset.Hair, 3, 0, 3);
            // Waffen-Sheet: untere drei Zeilen sind die Vorderseite (0=unten, 1=rechts, 2=oben).
            AddLayer(root.transform, "Weapon", "Characters/weapon/" + preset.Weapon, 4, 3, 4);

            var labelGo = new GameObject("Label");
            labelGo.transform.SetParent(root.transform, false);
            labelGo.transform.localPosition = new Vector3(0f, 1.8f, 0f);
            var label = labelGo.AddComponent<TextMesh>();
            label.text = preset.Name;
            label.characterSize = 0.065f;
            label.fontSize = 36;
            label.anchor = TextAnchor.MiddleCenter;
            label.color = new Color(0.92f, 0.93f, 0.98f);

            MeshRenderer labelRenderer = labelGo.GetComponent<MeshRenderer>();
            if (labelRenderer != null) labelRenderer.sortingOrder = 100;
        }

        private void AddLayer(Transform parent, string name, string resource, int row, int col, int sortingOrder)
        {
            Texture2D texture = Resources.Load<Texture2D>(resource);
            if (texture == null)
            {
                Debug.LogWarning("Charakter-Sheet fehlt: " + resource);
                return;
            }

            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = CreateCellSprite(texture, row, col);
            renderer.sortingOrder = sortingOrder;
            _createdSprites.Add(renderer.sprite);
        }

        private static Sprite CreateCellSprite(Texture2D texture, int row, int col)
        {
            float y = texture.height - (row + 1) * CellPx;
            var rect = new Rect(col * CellPx, y, CellPx, CellPx);
            var pivot = new Vector2((float)FootX / CellPx, (float)FootYFromBottom / CellPx);
            return Sprite.Create(texture, rect, pivot, PixelsPerUnit, 0, SpriteMeshType.FullRect);
        }

        private void OnDestroy()
        {
            foreach (Sprite sprite in _createdSprites)
            {
                if (sprite != null)
                    Destroy(sprite);
            }
            _createdSprites.Clear();
        }
    }
}
#endif
