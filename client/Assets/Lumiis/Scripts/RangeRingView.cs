using UnityEngine;

namespace Lumiis.Client
{
    /// <summary>combat-visuals Abschnitt 2: duenner, halbtransparenter Ring in MeleeRangeWire um den
    /// lokalen Spieler - IMMER sichtbar solange verbunden, unabhaengig von Zielauswahl. Unparented (kein
    /// Kind der CharacterView), damit die bestehende SortingGroup/Rang-Sortierung der Charaktere unberuehrt
    /// bleibt (siehe CLAUDE.md "Client"). Nutzt dieselbe Einheits-Ring-Sprite wie RadiusFlashView/BuffRingView
    /// (siehe GameClient - pixelsPerUnit == pixelSize, Durchmesser 1 Weltenheit bei localScale 1), Groesse
    /// nur ueber transform.localScale gesteuert.</summary>
    public sealed class RangeRingView : MonoBehaviour
    {
        private const int SortingOrder = -40;
        private static readonly Color RingColor = new Color(1f, 1f, 1f, 0.25f);

        public void Initialize(Sprite unitRingSprite, float diameterWorldUnits)
        {
            var renderer = gameObject.AddComponent<SpriteRenderer>();
            renderer.sprite = unitRingSprite;
            renderer.color = RingColor;
            renderer.sortingOrder = SortingOrder;
            transform.localScale = new Vector3(diameterWorldUnits, diameterWorldUnits, 1f);
        }

        public void SetWorldPosition(double x, double y)
        {
            transform.position = new Vector3((float)x, (float)y, 0f);
        }
    }
}
