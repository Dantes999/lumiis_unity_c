using Lumiis.Data.Items;
using Lumiis.Shared.Net;

namespace Lumiis.Data;

/// <summary>Löst das sichtbare Aussehen einer Character-Zeile auf: Körper/Haare direkt aus den Spalten,
/// Ausrüstung über ItemCatalog von Item-IDs zu LookIds (seit M5a). Gemeinsam genutzt von Lumiis.Server
/// (JoinAccepted/EntitySpawn) und Lumiis.Auth (Charakterliste für die Vorschau in der Charakterauswahl),
/// damit beide garantiert dasselbe Aussehen liefern.</summary>
public static class CharacterAppearance
{
    public static Appearance FromCharacter(Character character, ItemCatalog? itemCatalog)
    {
        return new Appearance
        {
            BodyId = character.BodyId,
            HairStyleId = character.HairStyleId,
            HairColorId = character.HairColorId,
            ArmorId = ResolveLookId(character.EquippedArmorItemId ?? 0, itemCatalog),
            HelmetId = ResolveLookId(character.EquippedHelmetItemId ?? 0, itemCatalog),
            WeaponId = ResolveLookId(character.EquippedWeaponItemId ?? 0, itemCatalog),
            ShieldId = ResolveLookId(character.EquippedShieldItemId ?? 0, itemCatalog)
        };
    }

    /// <summary>ItemId -> LookId; 0 (leerer Slot), unbekannte Items oder fehlender Katalog ergeben 0.</summary>
    public static int ResolveLookId(int itemId, ItemCatalog? itemCatalog)
    {
        if (itemId == 0 || itemCatalog == null || !itemCatalog.TryGet(itemId, out ItemDefinition item))
            return 0;
        return item.LookId;
    }
}
