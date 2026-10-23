#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections.Generic;
using Lumiis.Shared;
using Lumiis.Shared.Net;
using UnityEngine;

namespace Lumiis.Client
{
    /// <summary>Debug-Testfelder für Figuren-Darstellung. Nur Editor/Development-Build, rein statisch
    /// (keine Bewegung/Animation nötig).
    /// - F2 (BuildArmorGrid): Raster aus Body mit allen Rüstungen, je Down/Up/Side plus Side gespiegelt.
    /// - F9 (BuildSortingTest): drei überlappende Figuren mit 0,3 Einheiten Y-Abstand, um die SortingGroup-
    ///   plus-Custom-Axis-Sortierung zu prüfen (untere Figur muss vorn liegen, Ebenen bleiben je Figur zusammen).</summary>
    public sealed class CharacterTestboard : MonoBehaviour
    {
        private bool _armorGridBuilt;
        private bool _sortingTestBuilt;
        private bool _weaponGridBuilt;
        private readonly List<CharacterAnimator> _attackLoopAnimators = new List<CharacterAnimator>();
        private float _nextAttackLoopAt;

        public void BuildArmorGrid(ISpriteProvider spriteProvider, LookCatalogClient lookCatalog, int bodyId, IReadOnlyList<int> armorIds)
        {
            if (_armorGridBuilt) return;
            _armorGridBuilt = true;

            Facing[] facings =
            {
                new Facing(FacingDirection.Down, false),
                new Facing(FacingDirection.Up, false),
                new Facing(FacingDirection.Side, false),
                new Facing(FacingDirection.Side, true) // "links" per Spiegeln
            };

            var armorColumns = new List<int> { 0 };
            armorColumns.AddRange(armorIds);

            const float spacingX = 1.5f;
            const float spacingY = 2.2f;

            for (int col = 0; col < armorColumns.Count; col++)
            {
                for (int row = 0; row < facings.Length; row++)
                {
                    var go = new GameObject("Testboard_Armor" + armorColumns[col] + "_" + facings[row].Direction + (facings[row].FlipX ? "Flip" : ""));
                    go.transform.SetParent(transform, false);
                    go.transform.localPosition = new Vector3(col * spacingX, -row * spacingY, 0f);

                    var view = go.AddComponent<CharacterView>();
                    view.Initialize(-1, "Testboard", spriteProvider, lookCatalog);
                    view.SetAppearance(new Appearance { BodyId = bodyId, ArmorId = armorColumns[col], HairStyleId = 1, HairColorId = 1 });
                    view.SetFacing(facings[row]);
                }
            }
        }

        /// <summary>Drei Figuren mit unterschiedlicher Rüstung/Haarfarbe, 0,3 Einheiten in Y versetzt und
        /// horizontal deckungsgleich (überlappen sich absichtlich). Bei korrekter Sortierung liegt die Figur
        /// mit dem kleinsten Y (unten) vorn, und keine Ebene einer Figur erscheint zwischen den Ebenen einer
        /// anderen.</summary>
        public void BuildSortingTest(ISpriteProvider spriteProvider, LookCatalogClient lookCatalog, int bodyId, IReadOnlyList<int> armorIds)
        {
            if (_sortingTestBuilt) return;
            _sortingTestBuilt = true;

            const float ySpacing = 0.3f;
            var facing = new Facing(FacingDirection.Down, false);

            for (int i = 0; i < 3; i++)
            {
                var go = new GameObject("SortingTest_" + i);
                go.transform.SetParent(transform, false);
                go.transform.localPosition = new Vector3(0f, -i * ySpacing, 0f);

                int armorId = armorIds.Count > 0 ? armorIds[i % armorIds.Count] : 0;

                var view = go.AddComponent<CharacterView>();
                view.Initialize(-1, "SortTest" + i, spriteProvider, lookCatalog);
                view.SetAppearance(new Appearance { BodyId = bodyId, ArmorId = armorId, HairStyleId = 1, HairColorId = (i % 2) + 1 });
                view.SetFacing(facing);
            }
        }

        /// <summary>Interaktives Waffen-Testfeld: eine Spalte je Aufwertungsstufe +0 bis +9, alle mit der
        /// ersten Waffe aus items.json (spriteKey/pivotOffset). Die Figuren führen periodisch einen Schlag aus,
        /// damit +7/+9-Glitzern, Socket-Pose und TrailRenderer sichtbar werden.</summary>
        public void BuildWeaponUpgradeGrid(ISpriteProvider spriteProvider, LookCatalogClient lookCatalog,
            ItemCatalogClient itemCatalog, int bodyId, int armorId)
        {
            if (_weaponGridBuilt) return;
            _weaponGridBuilt = true;

            ItemEntryJson weapon = itemCatalog != null ? itemCatalog.FirstOfType("Weapon") : null;
            Sprite weaponSprite = weapon != null && spriteProvider != null
                ? spriteProvider.LoadWeaponSprite(weapon.spriteKey)
                : null;
            Vector2 pivotOffset = weapon != null && weapon.pivotOffset != null
                ? new Vector2(weapon.pivotOffset.x, weapon.pivotOffset.y)
                : Vector2.zero;

            const float spacingX = 1.4f;
            var facing = new Facing(FacingDirection.Down, false);

            for (int level = ItemEntryJson.MinUpgradeLevel; level <= ItemEntryJson.MaxUpgradeLevel; level++)
            {
                var go = new GameObject("WeaponUpgrade_+" + level);
                go.transform.SetParent(transform, false);
                go.transform.localPosition = new Vector3(level * spacingX, 0f, 0f);

                var view = go.AddComponent<CharacterView>();
                view.Initialize(-1, "WeaponTest+" + level, spriteProvider, lookCatalog);
                view.SetAppearance(new Appearance { BodyId = bodyId, ArmorId = armorId, HairStyleId = 1, HairColorId = 1 });
                view.SetFacing(facing);
                view.SetWeapon(weaponSprite, pivotOffset, level);

                var animator = go.AddComponent<CharacterAnimator>();
                animator.Initialize(view);
                _attackLoopAnimators.Add(animator);

                var labelGo = new GameObject("LevelLabel");
                labelGo.transform.SetParent(go.transform, false);
                labelGo.transform.localPosition = new Vector3(0f, 1.3f, 0f);
                var label = labelGo.AddComponent<TextMesh>();
                label.text = "+" + level;
                label.characterSize = 0.12f;
                label.fontSize = 48;
                label.anchor = TextAnchor.MiddleCenter;
                label.color = level >= 9 ? new Color(0.45f, 0.7f, 1f) : (level >= 7 ? new Color(1f, 0.92f, 0.5f) : Color.white);

                var labelRenderer = labelGo.GetComponent<MeshRenderer>();
                if (labelRenderer != null) labelRenderer.sortingOrder = 100;
            }
        }

        private void Update()
        {
            if (_attackLoopAnimators.Count == 0) return;
            if (Time.time < _nextAttackLoopAt) return;

            _nextAttackLoopAt = Time.time + 1.6f;
            foreach (CharacterAnimator animator in _attackLoopAnimators)
            {
                if (animator != null)
                    animator.PlayAttack();
            }
        }
    }
}
#endif
