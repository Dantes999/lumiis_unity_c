using System.Collections.Generic;

namespace Lumiis.Data.Progression;

// Reine Deserialisierungs-DTOs für data/progression.json (System.Text.Json, case-insensitive).

internal sealed class LevelDiffPercentEntryDto
{
    public int MaxDiff { get; set; }
    public int Percent { get; set; }
}

internal sealed class ProgressionCatalogDto
{
    public int MaxLevel { get; set; }
    public List<long> XpToNext { get; set; } = new();
    public List<LevelDiffPercentEntryDto> LevelDiffPercent { get; set; } = new();
}
