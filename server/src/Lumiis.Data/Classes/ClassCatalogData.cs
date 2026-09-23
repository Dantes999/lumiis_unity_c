using System.Collections.Generic;

namespace Lumiis.Data.Classes;

// Reine Deserialisierungs-DTOs für data/classes.json (System.Text.Json, case-insensitive).

internal sealed class ClassEntryDto
{
    public string Name { get; set; } = "";
    public int MaxHp { get; set; }
    public int Attack { get; set; }
    public int Defense { get; set; }
    public int MeleeRangeWire { get; set; }
    public int AttackCooldownMs { get; set; }
}

internal sealed class ClassCatalogDto
{
    public List<ClassEntryDto> Classes { get; set; } = new();
}
