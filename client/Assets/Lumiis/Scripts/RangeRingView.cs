using UnityEngine;

namespace Lumiis.Client
{
    /// <summary>combat-visuals: ein statisch positionierter, ungefaerbter Ring, der einer Entity folgt -
    /// Reichweitenring (Abschnitt 2, IMMER sichtbar um den lokalen Spieler solange verbunden, unabhaengig
    /// von Zielauswahl) UND Buff-Ringe (Abschnitt 6, ein/ausgeblendet je aktivem Buff) teilen sich diese
    /// Klasse, nur Farbe/Sortierung/Durchmesser unterscheiden sich je Aufrufer. Unparented (kein Kind der
    /// CharacterView), damit die bestehende SortingGroup/Rang-Sortierung der Charaktere unberuehrt bleibt
    /// (siehe CLAUDE.md "Client"). Nutzt dieselbe Einheits-Ring-Sprite wie RadiusFlashView (siehe GameClient -
    /// pixelsPerUnit == pixelSize, Durchmesser 1 Weltenheit bei localScale 1), Groesse nur ueber
    /// transform.localScale gesteuert.</summary>
    public sealed class RangeRingView : MonoBehaviour
    {
        public void Initialize(Sprite unitRingSprite, float diameterWorldUnits, Color color, int sortingOrder)
        {
            var renderer = gameObject.AddComponent<SpriteRenderer>();
            renderer.sprite = unitRingSprite;
            renderer.color = color;
            renderer.sortingOrder = sortingOrder;
            transform.localScale = new Vector3(diameterWorldUnits, diameterWorldUnits, 1f);
        }

        public void SetWorldPosition(double x, double y)
        {
            transform.position = new Vector3((float)x, (float)y, 0f);
        }
    }
}
