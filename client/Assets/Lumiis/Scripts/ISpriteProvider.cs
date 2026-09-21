using Lumiis.Shared;
using UnityEngine;

namespace Lumiis.Client
{
    /// <summary>Ebenen einer Paper-Doll-Figur, in Zeichenreihenfolge-Tabellen (siehe docs/sprite-spec.md) referenziert.</summary>
    public enum CharacterSlot
    {
        Body,
        Armor,
        Shield,
        Weapon,
        Hair,
        Helmet
    }

    /// <summary>Liefert das Sprite für eine Ebene. PlaceholderSpriteProvider erzeugt Formen zur Laufzeit; später
    /// kommt ein AtlasSpriteProvider, der echte PNGs lädt und bei fehlender Datei auf den Platzhalter zurückfällt.</summary>
    public interface ISpriteProvider
    {
        /// <summary>pivotNormalized: 0..1 im Canvas, Standard (0.5, 0) = Fußpunkt unten Mitte. Die Waffe nutzt
        /// stattdessen den Hand-Anker als Pivot, damit die Schwung-Rotation um den Griff erfolgt.</summary>
        Sprite GetSprite(CharacterSlot slot, int lookId, FacingDirection direction, string state, int frame, Vector2 pivotNormalized);
    }
}
