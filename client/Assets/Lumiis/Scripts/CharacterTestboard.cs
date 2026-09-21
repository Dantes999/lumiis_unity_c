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
    }
}
