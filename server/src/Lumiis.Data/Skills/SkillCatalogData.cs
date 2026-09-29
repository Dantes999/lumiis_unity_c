using System.Collections.Generic;

namespace Lumiis.Data.Skills;

// Reine Deserialisierungs-DTOs für data/skills.json (System.Text.Json, case-insensitive). Ein einzelnes
// Stufen-DTO trägt bewusst alle drei typspezifischen Feldnamen (nur eines davon ist je nach SkillDefinition.
// Type tatsächlich befüllt) - vermeidet drei separate DTO-Klassen für eine reine Deserialisierungsschicht.

internal sealed class SkillLevelEntryDto
{
    public int SpCost { get; set; }
    public int CooldownMs { get; set; }
    public int DamageMultiplierPercent { get; set; }
    public int AttackBonusPercent { get; set; }
    public int CooldownReductionPercent { get; set; }
}

internal sealed class SkillEntryDto
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";

    /// <summary>Nur bei Type=AreaAttack aussagekräftig (siehe SkillDefinition.RadiusWire).</summary>
    public int RadiusWire { get; set; }

    /// <summary>Nur bei Type=AttackBuff/AttackSpeedBuff aussagekräftig (siehe SkillDefinition.DurationMs).</summary>
    public int DurationMs { get; set; }

    public List<SkillLevelEntryDto> Levels { get; set; } = new();
}

internal sealed class SkillCatalogDto
{
    public List<SkillEntryDto> Skills { get; set; } = new();
}
