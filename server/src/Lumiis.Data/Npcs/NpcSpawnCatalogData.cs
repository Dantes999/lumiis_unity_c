using System.Collections.Generic;

namespace Lumiis.Data.Npcs;

// Reine Deserialisierungs-DTOs für data/maps/<map>/npcs.json (System.Text.Json, case-insensitive).

internal sealed class NpcPlacementEntryDto
{
    public int Id { get; set; }
    public int NpcId { get; set; }
    public int PositionX { get; set; }
    public int PositionY { get; set; }
}

internal sealed class NpcSpawnCatalogDto
{
    public List<NpcPlacementEntryDto> Placements { get; set; } = new();
}
