namespace Lumiis.Data;

/// <summary>Ein Item in einem Inventar-Slot eines Charakters (M5a). ItemId referenziert data/items.json - kein
/// DB-Fremdschlüssel, Spieldaten leben in JSON, nicht in der Datenbank (siehe CLAUDE.md). SlotIndex 0-19 (20
/// Felder), Unique-Constraint auf (CharacterId, SlotIndex) siehe LumiisDbContext.</summary>
public sealed class CharacterInventoryItem
{
    public int Id { get; set; }
    public int CharacterId { get; set; }
    public Character? Character { get; set; }
    public int SlotIndex { get; set; }
    public int ItemId { get; set; }

    /// <summary>M6c: Aufwertungsstufe dieser konkreten Item-Instanz (0-9) - bleibt beim Anlegen/Ablegen
    /// erhalten (siehe World.TryEquip/TryUnequip). 0 = nicht aufgewertet.</summary>
    public int UpgradeLevel { get; set; }
}
