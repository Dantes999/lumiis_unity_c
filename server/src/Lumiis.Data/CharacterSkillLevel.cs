namespace Lumiis.Data;

/// <summary>Die gelernte Stufe eines Skills eines Charakters (M6b). SkillId referenziert data/skills.json -
/// kein DB-Fremdschlüssel, Spieldaten leben in JSON, nicht in der Datenbank (siehe CLAUDE.md). Level 1-17,
/// Unique-Constraint auf (CharacterId, SkillId) siehe LumiisDbContext. Kein Eintrag = Skill nicht gelernt
/// (Stufe 0) - analog zu CharacterInventoryItem (kein Eintrag = Slot leer).</summary>
public sealed class CharacterSkillLevel
{
    public int Id { get; set; }
    public int CharacterId { get; set; }
    public Character? Character { get; set; }
    public int SkillId { get; set; }
    public int Level { get; set; }
}
