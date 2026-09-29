using System.Collections.Generic;

namespace Lumiis.Data.Classes;

// Reine Deserialisierungs-DTOs für data/classes.json (System.Text.Json, case-insensitive).

internal sealed class ClassEntryDto
{
    public string Name { get; set; } = "";
    public int MeleeRangeWire { get; set; }
    public int AttackCooldownMs { get; set; }

    // M6a: Formelkonstanten, siehe CLAUDE.md Abschnitt "Fortschritt: Level, XP, Werte".
    public int BaseHp { get; set; }
    public int BaseSp { get; set; }
    public int BaseAttack { get; set; }
    public int BaseDefense { get; set; }

    public int BaseSt { get; set; }
    public int BaseHt { get; set; }
    public int BaseDx { get; set; }
    public int BaseIq { get; set; }

    public int HpPerHt { get; set; }
    public int HpPerLevel { get; set; }
    public int SpPerIq { get; set; }
    public int SpPerLevel { get; set; }
    public int AttackPerSt { get; set; }
    public int HtPerDefense { get; set; }
    public int DodgeBpPerDx { get; set; }
    public int DodgeCapBp { get; set; }

    public int StatPointsPerLevel { get; set; }
    public int SkillPointsPerLevel { get; set; }

    public int RegenDelayMs { get; set; }
    public int RegenIntervalMs { get; set; }
    public int HpRegenPercent { get; set; }
    public int SpRegenPercent { get; set; }
}

internal sealed class ClassCatalogDto
{
    public List<ClassEntryDto> Classes { get; set; } = new();
}
