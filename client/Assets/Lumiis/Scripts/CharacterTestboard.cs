using System.Collections.Generic;
using Lumiis.Shared;
using Lumiis.Shared.Net;
using UnityEngine;

namespace Lumiis.Client
{
    /// <summary>F2-Testboard: Raster aus Body mit allen Rüstungen, je Down/Up/Side plus Side gespiegelt.
    /// Nur Editor/Development-Build, rein statisch (keine Bewegung/Animation nötig).</summary>
    public sealed class CharacterTestboard : MonoBehaviour
    {
        private bool _built;

        public void Build(ISpriteProvider spriteProvider, LookCatalogClient lookCatalog, int bodyId, IReadOnlyList<int> armorIds)
        {
            if (_built) return;
            _built = true;

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
    }
}
