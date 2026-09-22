using System.Collections.Generic;

namespace Lumiis.Data.Mobs;

// Reine Deserialisierungs-DTOs für data/mobs.json (System.Text.Json, case-insensitive).

internal sealed class MobEntryDto
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int SpeedWire { get; set; }
    public int WanderRadiusWire { get; set; }
    public int WanderIntervalMinMs { get; set; }
    public int WanderIntervalMaxMs { get; set; }
}

internal sealed class MobCatalogDto
{
    public List<MobEntryDto> Mobs { get; set; } = new();
}
