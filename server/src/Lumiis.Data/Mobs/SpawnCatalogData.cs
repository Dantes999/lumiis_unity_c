using System.Collections.Generic;

namespace Lumiis.Data.Mobs;

// Reine Deserialisierungs-DTOs für data/maps/<map>/spawns.json (System.Text.Json, case-insensitive).

internal sealed class SpawnEntryDto
{
    public int Id { get; set; }
    public int MobId { get; set; }
    public int PositionX { get; set; }
    public int PositionY { get; set; }

    /// <summary>Im Schema vorbereitet, wird in M3c bewusst nicht ausgewertet (kommt mit M4, wenn es etwas zum
    /// Wiederbeleben gibt) - siehe docs/tasks/M3c-monsters.md.</summary>
    public int RespawnSeconds { get; set; }

    /// <summary>Optional: gleiche Zahl = dieselbe Monster-Gruppe (wandert/kämpft zusammen), 0 = keine.</summary>
    public int GroupId { get; set; }
}

internal sealed class SpawnCatalogDto
{
    public List<SpawnEntryDto> Spawns { get; set; } = new();
}
